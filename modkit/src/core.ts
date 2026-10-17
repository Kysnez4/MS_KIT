import type { ModContext, ModData, ModDef } from "./api";
import { addArtifact, addArtifacts } from "./artifacts";
import * as cmd from "./commands";
import * as fx from "./effects";
import { PORTRAIT_ARRAY, Table, ensureSprite, intList, type Cell } from "./data";
import { emit, makeEvents } from "./events";
import { CONFIG_PATH, log, readText, removeFile, writeText } from "./log";

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
    /** Мод сейчас запущен (onEnable выполнен). `enabled` — желание пользователя: мод может быть включён, но заблокирован. */
    active: boolean;
    /** Постоянная причина, по которой мод не может запуститься: нет зависимости, не та версия, цикл. */
    problem?: string;
    /** Причина последнего отказа при запуске: конфликт с другим модом, зависимость не запустилась. */
    fault?: string;
    /** Предупреждения работающего мода (пересечение правок данных с другим модом). */
    warnings: string[];
    api?: unknown;
}

export const modVersion = (st: ModState): string | undefined => st.def.version ?? st.def.meta?.version;

const saved: Saved = { enabled: {}, settings: {} };
export const mods: ModState[] = [];
let ready = false;
/** Порядок запуска: зависимости раньше зависимых (считается в resolveAll). */
let startOrder: ModState[] = [];

// ---------------------------------------------------------------- зависимости и версии

const parseVer = (v: string): number[] => v.split(/[.\-+]/).map(p => parseInt(p, 10) || 0);
export function compareVersions(a: string, b: string): number {
    const x = parseVer(a), y = parseVer(b);
    for (let i = 0; i < Math.max(x.length, y.length); i++) {
        const d = (x[i] ?? 0) - (y[i] ?? 0);
        if (d) return d < 0 ? -1 : 1;
    }
    return 0;
}

/** Версия подходит под диапазон: `*`, `1.2` (не ниже), `>=1.0 <2.0`, `=1.5`. */
export function satisfies(version: string | undefined, range: string): boolean {
    const re = /(>=|<=|>|<|=)?\s*(\d[\w.\-+]*|\*)/g;
    let m: RegExpExecArray | null;
    while ((m = re.exec(range))) {
        if (m[2] === "*") continue;
        if (!version) return false;
        const c = compareVersions(version, m[2]);
        const ok = { ">=": c >= 0, "<=": c <= 0, ">": c > 0, "<": c < 0, "=": c === 0 }[m[1] ?? ">="]!;
        if (!ok) return false;
    }
    return true;
}

/** Пересчитывает постоянные проблемы зависимостей и порядок запуска. Вызывать при любом изменении набора модов. */
function resolveAll(): void {
    const byId = new Map(mods.map(m => [m.def.id, m] as const));
    for (const m of mods) m.problem = undefined;
    for (const m of mods) {
        for (const [id, range] of Object.entries(m.def.depends ?? {})) {
            const d = byId.get(id);
            const need = range === "*" ? "" : " " + range;
            if (!d) { m.problem ||= `нужен мод «${id}»${need}`; continue; }
            if (!satisfies(modVersion(d), range)) {
                m.problem ||= `нужен «${d.def.name}»${need}, установлена ${modVersion(d) ?? "без версии"}`;
            }
        }
    }
    const state = new Map<ModState, 1 | 2>();
    const order: ModState[] = [];
    const stack: ModState[] = [];
    const visit = (m: ModState): void => {
        if (state.get(m) === 2) return;
        state.set(m, 1);
        stack.push(m);
        const edges: Array<[ModState, boolean]> = [];
        for (const id of Object.keys(m.def.depends ?? {})) { const d = byId.get(id); if (d) edges.push([d, true]); }
        for (const id of m.def.loadAfter ?? []) { const d = byId.get(id); if (d) edges.push([d, false]); }
        for (const [d, hard] of edges) {
            if (state.get(d) === 1) {
                if (!hard) continue;
                const cycle = stack.slice(stack.indexOf(d));
                const text = "циклическая зависимость: " + [...cycle, d].map(x => x.def.id).join(" → ");
                for (const c of cycle) c.problem ||= text;
                continue;
            }
            visit(d);
            if (hard && d.problem) m.problem ||= `зависимость «${d.def.name}» недоступна: ${d.problem}`;
        }
        stack.pop();
        state.set(m, 2);
        order.push(m);
    };
    for (const m of mods) visit(m);
    startOrder = order;
}

/** Включённые, но не запущенные моды, у которых препятствие исчезло (новая зависимость, освободился метод). */
function retryBlocked(): void {
    if (!ready) return;
    for (const m of startOrder) if (m.enabled && !m.active && !m.problem) runEnable(m);
}

const dependentsOf = (st: ModState): ModState[] => mods.filter(m => m.enabled && m.def.depends && st.def.id in m.def.depends);

// ---------------------------------------------------------------- конфликты

/** Методы, заменённые через ctx.replace: ключ -> id мода. */
const replaced = new Map<string, string>();
/** Ячейки таблиц, изменённые через data.edit: ключ -> id мода. */
const editedCells = new Map<string, string>();

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
    for (const st of startOrder) setEnabled(st, prev[st.def.id] ?? false);
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
            const keys: string[] = [];
            for (const col of Object.keys(changes)) {
                const key = `${table}#${id}#${col}`;
                const other = editedCells.get(key);
                if (other && other !== st.def.id) {
                    const w = `«${col}» (${table} #${id}) уже правит мод ${other}`;
                    st.warnings.push(w);
                    log(`[${st.def.id}] предупреждение: ${w}`);
                } else { editedCells.set(key, st.def.id); keys.push(key); }
            }
            st.cleanups.push(() => { for (const k of keys) editedCells.delete(k); });
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
        events: makeEvents(id, () => st.enabled, st.cleanups),
        expose(api) { st.api = api; },
        mod: <T>(modId: string) => mods.find(m => m.def.id === modId && m.active)?.api as T | undefined,
        replace(className, method, paramCount, impl) {
            const key = `${className}.${method}(${paramCount})`;
            const owner = replaced.get(key);
            if (owner && owner !== id) {
                const err = new Error(`конфликт с модом «${owner}»: оба заменяют ${key}`);
                (err as any).conflict = true;
                throw err;
            }
            const m = findMethod(className, method, paramCount);
            m.implementation = impl as any;
            replaced.set(key, id);
            st.cleanups.push(() => { replaced.delete(key); m.revert(); });
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
    for (const st of mods) if (st.active) callMainMenu(st);
    emit("mainMenu", { menu });
}

function runEnable(st: ModState, deferRevert = false): boolean {
    if (st.active) return true;
    const id = st.def.id;
    st.fault = undefined;
    if (st.problem) { log(`[${id}] не запущен: ${st.problem}`); return true; }
    // зависимости включаются автоматически и запускаются раньше
    for (const depId of Object.keys(st.def.depends ?? {})) {
        const d = mods.find(m => m.def.id === depId)!;
        if (!d.enabled) { d.enabled = true; log(`[${id}] включает зависимость ${depId}`); }
        runEnable(d, deferRevert);
        if (!d.active) {
            st.fault = `зависимость «${d.def.name}» не запустилась` + (d.fault ? `: ${d.fault}` : "");
            log(`[${id}] не запущен: ${st.fault}`);
            return true;
        }
    }
    st.active = true;
    st.warnings = [];
    try {
        st.ctx = makeContext(st);
        st.def.onEnable(st.ctx);
        callMainMenu(st);
        log(`мод включён: ${id}`);
        emit("modEnabled", { id });
        return true;
    } catch (e) {
        const conflict = !!(e as any)?.conflict;
        log(`${conflict ? "конфликт модов" : "ошибка включения"} ${id}: ${e}`);
        runDisable(st);
        if (conflict) {
            // конфликт — не сбой игры: мод остаётся включённым в настройках, но ждёт, пока освободится метод
            st.fault = String((e as Error).message ?? e);
            return true;
        }
        st.enabled = false;
        if (!deferRevert) revertToPrevious(`${id}: ${e}`);
        return false;
    }
}

function runDisable(st: ModState): void {
    if (!st.active) return;
    try { st.def.onDisable?.(makeContext(st)); } catch (e) { log(`ошибка onDisable ${st.def.id}: ${e}`); }
    for (const c of st.cleanups.splice(0).reverse()) {
        try { c(); } catch (e) { log(`ошибка отката ${st.def.id}: ${e}`); }
    }
    st.active = false;
    st.api = undefined;
    st.warnings = [];
    log(`мод выключен: ${st.def.id}`);
    emit("modDisabled", { id: st.def.id });
}

export function register(defs: ModDef[]): void {
    load();
    for (const def of defs) {
        const settings: Record<string, number> = {};
        for (const o of def.options ?? []) settings[o.key] = saved.settings[def.id]?.[o.key] ?? o.default;
        mods.push({ def, enabled: saved.enabled[def.id] ?? def.defaultEnabled ?? false, settings, cleanups: [], active: false, warnings: [] });
    }
    resolveAll();
}

/** Добавляет мод, загруженный из файла. false — мод с таким id уже есть. */
export function addExternal(def: ModDef): boolean {
    if (mods.some(m => m.def.id === def.id)) return false;
    const settings: Record<string, number> = {};
    for (const o of def.options ?? []) settings[o.key] = saved.settings[def.id]?.[o.key] ?? o.default;
    const st: ModState = { def, enabled: saved.enabled[def.id] ?? def.defaultEnabled ?? false, settings, cleanups: [], active: false, warnings: [] };
    mods.push(st);
    resolveAll();
    if (ready && st.enabled) runEnable(st);
    retryBlocked();
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
    for (const st of startOrder) if (st.enabled && !runEnable(st, true)) failed ||= st.def.id;
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
    if (value) {
        runEnable(st);
    } else {
        // выключение зависимости выключает зависимые моды
        const off = (m: ModState): void => {
            for (const d of dependentsOf(m)) { log(`[${d.def.id}] выключен: выключена зависимость ${m.def.id}`); d.enabled = false; off(d); runDisable(d); }
        };
        off(st);
        runDisable(st);
        retryBlocked();
    }
    save();
}

/**
 * Обновляет установленный мод новой версией (тот же id): выключает его и зависимые, подменяет определение,
 * запускает обратно. Включённость и настройки сохраняются. При ошибке возвращается прежняя версия и бросается исключение.
 */
export function replaceMod(st: ModState, def: ModDef): void {
    const oldDef = st.def, oldSettings = st.settings;
    const wasEnabled = st.enabled;
    const range = (m: ModState) => m.def.depends?.[st.def.id];
    // новая версия не должна ломать зависимые моды
    for (const m of mods) {
        const r = range(m);
        if (m !== st && r && !satisfies(def.version ?? def.meta?.version, r)) {
            throw new Error(`мод «${m.def.name}» требует ${st.def.id} ${r}, а в обновлении ${def.version ?? def.meta?.version ?? "версия не указана"}`);
        }
    }
    const chain = startOrder.filter(m => m !== st && m.active && dependentNames(st).includes(m.def.name));
    for (const m of [...chain].reverse()) runDisable(m);
    runDisable(st);

    const load = (d: ModDef, saved0: Record<string, number>): void => {
        st.def = d;
        st.settings = {};
        for (const o of d.options ?? []) {
            const v = saved0[o.key] ?? o.default;
            st.settings[o.key] = Math.min(o.max, Math.max(o.min, v));
        }
        st.problem = st.fault = undefined;
        resolveAll();
    };
    const restart = (): boolean => {
        if (wasEnabled) runEnable(st, true);
        const ok = !wasEnabled || st.active;
        for (const m of chain) if (m.enabled) runEnable(m, true);
        return ok;
    };

    load(def, oldSettings);
    if (restart()) { save(); log(`мод обновлён: ${def.id} ${modVersion(st) ?? ""}`); return; }
    const why = st.problem ?? st.fault ?? "ошибка при запуске";
    log(`обновление ${def.id} не удалось (${why}): возвращаю прежнюю версию`);
    runDisable(st);
    st.enabled = wasEnabled;
    load(oldDef, oldSettings);
    restart();
    save();
    throw new Error(`новая версия не запустилась: ${why}. Осталась прежняя`);
}

/** Моды, которые сломаются без этого (включённые зависимые, в том числе через цепочку). */
export function dependentNames(st: ModState): string[] {
    const out = new Set<ModState>();
    const walk = (m: ModState): void => { for (const d of dependentsOf(m)) if (!out.has(d)) { out.add(d); walk(d); } };
    walk(st);
    return [...out].map(m => m.def.name);
}

/** Удаляет установленный zip-мод: выключает (вместе с зависимыми), стирает файл, убирает из списка. */
export function removeMod(st: ModState): void {
    if (!st.def.meta) throw new Error("встроенный мод удалить нельзя");
    setEnabled(st, false);
    if (st.def.source && !removeFile(st.def.source)) log(`[${st.def.id}] файл ${st.def.source} удалить не удалось`);
    const i = mods.indexOf(st);
    if (i >= 0) mods.splice(i, 1);
    delete saved.settings[st.def.id];
    delete saved.enabled[st.def.id];
    resolveAll();
    save();
    log(`мод удалён: ${st.def.id}`);
}

/** Меняет число в настройке; мод перезапускается, чтобы подхватить значение. */
export function setSetting(st: ModState, key: string, value: number): void {
    st.settings[key] = value;
    if (ready && st.active) {
        runDisable(st);
        runEnable(st);
    }
    save();
}
