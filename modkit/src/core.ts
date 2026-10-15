import type { ModContext, ModData, ModDef } from "./api";
import { addArtifact, addArtifacts } from "./artifacts";
import * as cmd from "./commands";
import * as fx from "./effects";
import { PORTRAIT_ARRAY, Table, ensureSprite, intList, type Cell } from "./data";
import { CONFIG_PATH, log, readText, writeText } from "./log";

interface Saved {
    enabled: Record<string, boolean>;
    settings: Record<string, Record<string, number>>;
    /** Набор модов, с которым игра запускалась и применялась в прошлый раз (id -> включён). */
    session?: Record<string, boolean>;
    /** Запуск ещё не подтверждён как стабильный: если флаг найден на старте, прошлая игра упала. */
    booting?: boolean;
}

export interface ModState {
    def: ModDef;
    enabled: boolean;
    settings: Record<string, number>;
    cleanups: Array<() => void>;
    ctx?: ModContext;
}

const saved: Saved = { enabled: {}, settings: {} };
export const mods: ModState[] = [];
let ready = false;

function load(): void {
    const t = readText(CONFIG_PATH);
    if (!t) return;
    try {
        const j = JSON.parse(t);
        saved.enabled = j.enabled ?? {};
        saved.settings = j.settings ?? {};
        saved.session = j.session;
        saved.booting = j.booting;
    } catch (e) {
        log("config.json повреждён, игнорирую: " + e);
    }
}

/** Включённые моды сейчас: id -> true/false. */
function currentEnabled(): Record<string, boolean> {
    const r: Record<string, boolean> = {};
    for (const m of mods) r[m.def.id] = m.enabled;
    return r;
}

export interface SessionDiff {
    /** Названия модов, включённых в прошлой сессии. */
    before: string[];
    /** Названия модов, включённых сейчас. */
    now: string[];
}

/** Набор модов расходится с прошлой применённой сессией? Определяется один раз при старте. */
let startupDiff: SessionDiff | null = null;
export function sessionDiff(): SessionDiff | null {
    return startupDiff;
}

function computeDiff(): SessionDiff | null {
    const prev = saved.session;
    if (!prev) return null;
    const cur = currentEnabled();
    const ids = new Set([...Object.keys(prev), ...Object.keys(cur)]);
    let differs = false;
    for (const id of ids) if ((prev[id] ?? false) !== (cur[id] ?? false)) differs = true;
    if (!differs) return null;
    const names = (set: Record<string, boolean>) => mods.filter(m => set[m.def.id]).map(m => m.def.name);
    return { before: names(prev), now: names(cur) };
}

/** Запоминает текущий набор модов как «применённый» (вызывается при старте без расхождений, после выбора и перед выходом). */
export function markSession(): void {
    saved.session = currentEnabled();
    saved.booting = false;
    startupDiff = null;
    save();
}

/** Откат к набору прошлого запуска после ошибки мода или падения игры. */
let reverting = false;
function revertToPrevious(reason: string): void {
    if (reverting || !saved.session) return;
    reverting = true;
    try {
        log(`ОШИБКА: ${reason}. Моды сброшены к набору прошлого запуска`);
        restoreSession();
    } finally {
        reverting = false;
    }
}

const STABLE_AFTER_MS = 60000;

/** Вернуть моды к набору прошлой сессии. */
export function restoreSession(): void {
    const prev = saved.session ?? {};
    for (const st of mods) setEnabled(st, prev[st.def.id] ?? false);
    markSession();
}

function save(): void {
    saved.enabled = {};
    saved.settings = {};
    for (const m of mods) {
        saved.enabled[m.def.id] = m.enabled;
        saved.settings[m.def.id] = m.settings;
    }
    try {
        writeText(CONFIG_PATH, JSON.stringify(saved, null, 2));
    } catch (e) {
        log("не удалось сохранить config.json: " + e);
    }
}

function findClass(name: string): Il2Cpp.Class {
    const [asmName, clsName] = name.includes(":") ? name.split(":") : ["Assembly-CSharp", name];
    const klass = Il2Cpp.domain.assembly(asmName).image.tryClass(clsName);
    if (!klass) throw new Error(`класс не найден: ${name}`);
    return klass;
}

function findMethod(className: string, method: string, paramCount: number): Il2Cpp.Method {
    const m = findClass(className).tryMethod(method, paramCount);
    if (!m) throw new Error(`метод не найден: ${className}.${method}(${paramCount})`);
    return m;
}

// ---------------------------------------------------------------- отложенные правки данных (whenReady)

interface ReadyItem { st: ModState; fn: () => void }
const readyQueue: ReadyItem[] = [];
let readyHooked = false;

function tablesLoaded(): boolean {
    try { return new Table("Ability").length > 0 && new Table("MagicCom").length > 0 && new Table("Synergy").length > 0; } catch (_) { return false; }
}

function installReadyHook(): void {
    if (readyHooked) return;
    readyHooked = true;
    const upd = Il2Cpp.domain.assembly("Assembly-CSharp").image.class("A_MainMenuInformation").method("Update", 0);
    let calls = 0;
    Interceptor.attach(upd.virtualAddress, {
        onEnter() {
            if (!readyQueue.length || ++calls % 10 !== 0) return;       // Update вызывается каждый кадр: смотрим очередь раз в ~10 кадров
            if (!tablesLoaded()) return;
            // очередь: за один заход применяется правка ОДНОГО мода, остальные ждут следующих кадров (без рывка и без гонок между модами)
            const item = readyQueue.shift()!;
            if (!item.st.enabled) return;
            try { item.fn(); } catch (e) {
                log(`[${item.st.def.id}] ошибка в data.whenReady: ${e}`);
                revertToPrevious(`${item.st.def.id}: ${e}`);
            }
        },
    });
    log("отложенные правки данных: хук на главном потоке (A_MainMenuInformation.Update)");
}

function makeData(st: ModState, getCtx: () => ModContext): ModData {
    const rowOf = (t: Table, id: number) => {
        const i = t.indexOf(id);
        if (i < 0) throw new Error(`${t.name}: нет строки с ID ${id}`);
        return i;
    };
    return {
        whenReady(fn) {
            // правки таблиц и пулов выполняем на ГЛАВНОМ потоке Unity, когда построено главное меню (игра закончила загрузку данных).
            // Из фонового потока во время загрузки это гонка с игрой: зависание на старте или SIGABRT.
            const item = { st, fn };
            readyQueue.push(item);
            installReadyHook();
            st.cleanups.push(() => { const i = readyQueue.indexOf(item); if (i >= 0) readyQueue.splice(i, 1); });
        },
        table: name => new Table(name),
        edit(table, id, changes) {
            const t = new Table(table);
            const i = rowOf(t, id);
            const undo: Array<[string, Cell]> = [];
            for (const [col, v] of Object.entries(changes)) undo.push([col, t.set(i, col, v)]);
            st.cleanups.push(() => Il2Cpp.perform(() => {
                const ti = t.indexOf(id);
                if (ti >= 0) for (const [col, old] of undo) t.set(ti, col, old);
            }));
        },
        add(table, cloneId, changes) {
            const t = new Table(table);
            const i = t.cloneRow(rowOf(t, cloneId));
            const newId = t.maxId() + 1;
            t.set(i, t.idColumn, newId);
            const portraits = PORTRAIT_ARRAY[table];
            if (portraits) {
                // иконка берётся у строки-образца, иначе индекс ID выйдет за границы массива спрайтов
                const was = ensureSprite(portraits, newId, cloneId);
                log(`[${st.def.id}] ${portraits}: длина была ${was}, иконка ID ${newId} = иконка ID ${cloneId}`);
            }
            for (const [col, v] of Object.entries(changes)) t.set(i, col, v);
            st.cleanups.push(() => Il2Cpp.perform(() => {
                const ti = t.indexOf(newId);
                if (ti === t.length - 1) t.removeLast();
                else log(`[${st.def.id}] строку ${newId} в ${table} нельзя убрать: после неё добавлены другие`);
            }));
            return newId;
        },
        addArtifact: def => addArtifact(def, st.cleanups, st.def.id),
        addArtifacts: defs => addArtifacts(defs, st.cleanups, st.def.id),
        addCombos: defs => cmd.addCombos(defs, st.cleanups, st.def.id),
        addSynergies: defs => cmd.addSynergies(defs, st.cleanups, st.def.id),
        editArtifact: (id, ch) => cmd.editArtifact(id, ch, st.cleanups, st.def.id),
        removeArtifact: id => cmd.removeArtifact(id, st.cleanups, st.def.id),
        restoreArtifact: id => cmd.restoreArtifact(id, st.def.id),
        editCombo: (id, ch) => cmd.editCombo(id, ch, st.cleanups, st.def.id),
        addCombo: def => cmd.addCombo(def, st.cleanups, st.def.id),
        addSynergy: def => cmd.addSynergy(def, st.cleanups, st.def.id),
        removeSynergy: id => cmd.removeSynergy(id, st.cleanups, st.def.id),
        synergyAddArtifact: (s, a) => cmd.synergyAddArtifact(s, a, st.cleanups, st.def.id),
        synergyRemoveArtifact: (s, a) => cmd.synergyRemoveArtifact(s, a, st.cleanups, st.def.id),
        defineEffect: def => fx.defineEffect(def, getCtx(), st.cleanups),
        setEffects: pairs => fx.setGlobalEffects(st.def.id, pairs, st.cleanups),
        useBuiltinEffects: () => fx.useBuiltinEffects(getCtx(), st.cleanups),
        setSprite(arrayField, index, copyFrom) {
            ensureSprite(arrayField, index, copyFrom);
        },
        addToList(listField, value) {
            const l = intList(listField);
            l.method("Add").invoke(value);
            st.cleanups.push(() => Il2Cpp.perform(() => { l.method("Remove").invoke(value); }));
        },
    };
}

function makeContext(st: ModState): ModContext {
    const id = st.def.id;
    const attach = (className: string, method: string, paramCount: number, kind: "enter" | "leave", fn: (self: Il2Cpp.Object) => void) => {
        const m = findMethod(className, method, paramCount);
        const listener = Interceptor.attach(m.virtualAddress, {
            onEnter(args) {
                (this as any).self = args[0];
                if (kind === "enter") run(args[0]);
            },
            onLeave() {
                if (kind === "leave") run((this as any).self);
            },
        });
        const run = (ptr: NativePointer) => {
            if (!st.enabled) return;
            try {
                fn(new Il2Cpp.Object(ptr));
            } catch (e) {
                log(`[${id}] ошибка в ${className}.${method}: ${e}`);
            }
        };
        st.cleanups.push(() => listener.detach());
    };
    return {
        id,
        settings: st.settings,
        data: makeData(st, () => st.ctx!),
        setSetting(key, value) { st.settings[key] = value; save(); },
        log: msg => log(`[${id}] ${msg}`),
        cls: findClass,
        replace(className, method, paramCount, impl) {
            const m = findMethod(className, method, paramCount);
            m.implementation = impl as any;
            st.cleanups.push(() => m.revert());
        },
        after: (c, m, n, fn) => attach(c, m, n, "leave", fn),
        before: (c, m, n, fn) => attach(c, m, n, "enter", fn),
        every(ms, fn) {
            const h = setInterval(() => {
                if (!st.enabled) return;
                Il2Cpp.perform(() => {
                    try { fn(); } catch (e) { log(`[${id}] ошибка в every: ${e}`); }
                });
            }, ms);
            st.cleanups.push(() => clearInterval(h));
        },
        onCleanup: fn => st.cleanups.push(fn),
    };
}

let lastMenu: Il2Cpp.Object | null = null;

function callMainMenu(st: ModState): void {
    if (!lastMenu || !st.ctx || !st.def.onMainMenu) return;
    try {
        st.def.onMainMenu(st.ctx, lastMenu);
    } catch (e) {
        log(`ошибка onMainMenu ${st.def.id}: ${e}`);
    }
}

/** Вызывается из ui.ts после построения главного меню: моды могут добавить свои элементы. */
export function notifyMainMenu(menu: Il2Cpp.Object): void {
    lastMenu = menu;
    for (const st of mods) if (st.enabled) callMainMenu(st);
}

function runEnable(st: ModState, deferRevert = false): boolean {
    try {
        st.ctx = makeContext(st);
        st.def.onEnable(st.ctx);
        callMainMenu(st);
        log(`мод включён: ${st.def.id}`);
        return true;
    } catch (e) {
        log(`ошибка включения ${st.def.id}: ${e}`);
        runDisable(st);
        st.enabled = false;
        if (!deferRevert) revertToPrevious(`${st.def.id}: ${e}`);
        return false;
    }
}

function runDisable(st: ModState): void {
    try { st.def.onDisable?.(makeContext(st)); } catch (e) { log(`ошибка onDisable ${st.def.id}: ${e}`); }
    for (const c of st.cleanups.splice(0).reverse()) {
        try { c(); } catch (e) { log(`ошибка отката ${st.def.id}: ${e}`); }
    }
    log(`мод выключен: ${st.def.id}`);
}

export function register(defs: ModDef[]): void {
    load();
    for (const def of defs) {
        const settings: Record<string, number> = {};
        for (const o of def.options ?? []) settings[o.key] = saved.settings[def.id]?.[o.key] ?? o.default;
        mods.push({ def, enabled: saved.enabled[def.id] ?? def.defaultEnabled ?? false, settings, cleanups: [] });
    }
}

/** Добавляет мод, загруженный из файла. false — мод с таким id уже есть. */
export function addExternal(def: ModDef): boolean {
    if (mods.some(m => m.def.id === def.id)) return false;
    const settings: Record<string, number> = {};
    for (const o of def.options ?? []) settings[o.key] = saved.settings[def.id]?.[o.key] ?? o.default;
    const st: ModState = { def, enabled: saved.enabled[def.id] ?? def.defaultEnabled ?? false, settings, cleanups: [] };
    mods.push(st);
    if (ready && st.enabled) runEnable(st);
    return true;
}

/** Включает моды, помеченные включёнными. Вызывать внутри Il2Cpp.perform. */
export function start(): void {
    ready = true;
    const crashed = saved.booting === true;
    if (crashed && saved.session) {
        // прошлый запуск не дожил до подтверждения стабильности — возвращаем набор, который работал
        log("ОШИБКА: прошлый запуск завершился аварийно. Моды сброшены к набору последнего стабильного запуска");
        for (const st of mods) st.enabled = saved.session[st.def.id] ?? false;
    }
    startupDiff = crashed ? null : computeDiff();
    let failed = "";
    for (const st of mods) if (st.enabled && !runEnable(st, true)) failed ||= st.def.id;
    saved.booting = true;
    if (failed) revertToPrevious(`не включился мод ${failed}`);
    else if (!startupDiff && !crashed) { saved.session = currentEnabled(); save(); }
    else save();
    // через минуту без падения запуск считается стабильным
    setTimeout(() => {
        if (startupDiff) { saved.booting = false; save(); }
        else markSession();
    }, STABLE_AFTER_MS);
}

export function setEnabled(st: ModState, value: boolean): void {
    if (st.enabled === value) return;
    st.enabled = value;
    if (!ready) { save(); return; }
    if (value) runEnable(st); else runDisable(st);
    save();
}

/** Меняет число в настройке; мод перезапускается, чтобы подхватить значение. */
export function setSetting(st: ModState, key: string, value: number): void {
    st.settings[key] = value;
    if (ready && st.enabled) {
        runDisable(st);
        runEnable(st);
    }
    save();
}
