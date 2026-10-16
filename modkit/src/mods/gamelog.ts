import { defineMod } from "../api";
import { DIR, chmod644 } from "../log";
import { openChecklist } from "../uikit";

const OUT = DIR + "/gamelog.txt";
const OBSCURED = new Set(["ObscuredInt", "ObscuredFloat", "ObscuredDouble", "ObscuredBool", "ObscuredLong", "ObscuredShort", "ObscuredByte", "ObscuredUInt"]);
const SKIP_FIELD = /^(LSoundList_|Array_Float_AutoSave|Array_Int_AutoSave)/;
/** Классы, по полям которых строятся снимки состояния. */
const INSTANCE_CLASSES = ["C_Battle_Manager", "V_Ability_Manager"];
/** Классы, чьи вызовы методов пишутся в журнал. */
const TRACE_CLASSES = ["C_Battle_Manager", "C_Battle_AllyUnit", "V_Ability_Manager", "Z_Database"];
/** Менеджер артефактов/сундуков/торговца: вызовы пишутся сразу, с числовыми аргументами. */
const ARTIFACT_CLASS = "V_Artifact_Manager";
const SKIP_METHOD = /^(Update|FixedUpdate|LateUpdate|OnGUI|get_|set_|\.ctor|\.cctor)/;

/** Группы значений: включена, пока настройка != 0 (по умолчанию все включены). Меняются на экране «Настройки» без перезапуска. */
const GROUPS: Array<{ key: string; label: string }> = [
    { key: "g_vars", label: "Z_Database: характеристики исследований и эффектов (AllyUnitVar_*)" },
    { key: "g_lists", label: "Z_Database: списки и массивы (артефакты, способности, пулы выпадения)" },
    { key: "g_other", label: "Z_Database: прочее (Game_*, AllyUnitGlobal_* и др.)" },
    { key: "g_heroStat", label: "Герой: характеристики (UnitApply_*, BUnitApply_*)" },
    { key: "g_heroOther", label: "Герой: остальные поля" },
    { key: "g_mgrBattle", label: "Менеджер боя (C_Battle_Manager)" },
    { key: "g_mgrAbility", label: "Менеджер способностей (V_Ability_Manager)" },
    { key: "g_calls", label: "Вызовы методов (CALLS)" },
    { key: "g_art", label: "Сундуки, выбор артефактов, торговец (ART, OFFER)" },
    { key: "g_status", label: "Окно характеристик героя: названия и значения строк (STATUS)" },
];

/** Шумные источники: по умолчанию не пишутся. Настройка != 0 означает «не писать». Ключ значения — «группа.поле», вызова — «Класс.метод». */
const EXCLUDES: Array<{ key: string; label: string; value?: string; call?: string }> = [
    { key: "x_kill", label: "Счётчик убийств (Game_KillMonsterNum)", value: "Z_Database.Game_KillMonsterNum" },
    { key: "x_manastock", label: "Запас маны (AllyUnitGlobal_ManaStock)", value: "Z_Database.AllyUnitGlobal_ManaStock" },
    { key: "x_timesec", label: "Секунды боя (Time_Sec)", value: "C_Battle_Manager.Time_Sec" },
    { key: "x_timescale", label: "Масштаб времени (ApplyTimeScale)", value: "C_Battle_Manager.ApplyTimeScale" },
    { key: "x_enemylvl", label: "Уровень врагов (EnemyLevelNum)", value: "C_Battle_Manager.EnemyLevelNum" },
    { key: "x_shield", label: "Задержка щита героя (ShieldDelay)", value: "hero.ShieldDelay" },
    { key: "x_bullet", label: "Вызовы: создание снарядов (Bullet_Create)", call: "C_Battle_AllyUnit.Bullet_Create" },
    { key: "x_collision", label: "Вызовы: столкновения (OnCollisionStay)", call: "C_Battle_AllyUnit.OnCollisionStay" },
    { key: "x_managain", label: "Вызовы: получение маны (ManaGain)", call: "V_Ability_Manager.ManaGain" },
    { key: "x_statebar", label: "Вызовы: полоска состояния (Display_StateBar)", call: "C_Battle_AllyUnit.Display_StateBar" },
];
const NOISY_CHANGES = 20;     // изменений за окно — и поле считается «шумным» (таймеры, координаты)
const WINDOW_MS = 10000;
const HOT_CALLS_PER_FLUSH = 400;
const CALLS_FLUSH_MS = 2000;
/** Снимок не должен занимать больше доли времени: следующий — не раньше, чем через столько длительностей предыдущего. */
const TICK_COOLDOWN = 4;

function round(v: number): number {
    return Math.round(v * 10000) / 10000;
}

/** Читает скалярное поле (в т.ч. Obscured*) через мост IL2CPP — медленный путь, только для Obscured*, enum и констант. null — тип не поддерживается. */
function readSlow(f: Il2Cpp.Field): string | null {
    const t = f.type;
    if (t.class.isEnum) return String(f.value);
    const v = f.value as any;
    if (typeof v === "number") return String(round(v));
    if (typeof v === "boolean") return v ? "true" : "false";
    if (typeof v === "bigint") return v.toString();
    if (v && typeof v === "object" && OBSCURED.has(t.name.split(".").pop()!)) {
        const d = v.box().method("GetDecrypted").invoke();
        return typeof d === "number" ? String(round(d)) : String(d);
    }
    return null;
}

/** Размер элемента массива в байтах и разбор блока памяти. null — тип не поддерживается. */
function elementReader(typeName: string): { size: number; parse: (buf: ArrayBuffer) => string[] } | null {
    const fmt = (a: ArrayLike<number | bigint>) => Array.from(a, v => typeof v === "bigint" ? v.toString() : String(round(v)));
    switch (typeName) {
        case "System.Int32": return { size: 4, parse: b => fmt(new Int32Array(b)) };
        case "System.UInt32": return { size: 4, parse: b => fmt(new Uint32Array(b)) };
        case "System.Single": return { size: 4, parse: b => fmt(new Float32Array(b)) };
        case "System.Double": return { size: 8, parse: b => fmt(new Float64Array(b)) };
        case "System.Boolean": case "System.Byte": return { size: 1, parse: b => fmt(new Uint8Array(b)) };
        case "System.Int16": return { size: 2, parse: b => fmt(new Int16Array(b)) };
        case "System.Int64": return { size: 8, parse: b => fmt(new BigInt64Array(b)) };
        default: return null;
    }
}

/** Скалярные типы, читаемые прямо из блока памяти: размер и разбор значения. */
const SCALAR_READERS: Record<string, { size: number; read: (dv: DataView, o: number) => string }> = {
    "System.Int32": { size: 4, read: (dv, o) => String(dv.getInt32(o, true)) },
    "System.UInt32": { size: 4, read: (dv, o) => String(dv.getUint32(o, true)) },
    "System.Single": { size: 4, read: (dv, o) => String(round(dv.getFloat32(o, true))) },
    "System.Double": { size: 8, read: (dv, o) => String(round(dv.getFloat64(o, true))) },
    "System.Boolean": { size: 1, read: (dv, o) => dv.getUint8(o) !== 0 ? "true" : "false" },
    "System.Byte": { size: 1, read: (dv, o) => String(dv.getUint8(o)) },
    "System.Int16": { size: 2, read: (dv, o) => String(dv.getInt16(o, true)) },
    "System.Int64": { size: 8, read: (dv, o) => dv.getBigInt64(o, true).toString() },
};

const ARRAY_LENGTH = 0x18;      // Il2CppArray: max_length
const ARRAY_DATA = 0x20;        // данные массива идут с этого смещения
const MAX_ELEMS = 400;          // длиннее — пишем только длину
const CACHED_PTR = 0x10;        // UnityEngine.Object.m_CachedPtr: 0 у уничтоженного объекта

/** Одинаковы ли два блока памяти (по 4 байта, хвост по байтам). */
function sameBytes(a: ArrayBuffer, b: ArrayBuffer): boolean {
    const n = a.byteLength;
    if (n !== b.byteLength) return false;
    const w = n >> 2;
    const a32 = new Uint32Array(a, 0, w), b32 = new Uint32Array(b, 0, w);
    for (let i = 0; i < w; i++) if (a32[i] !== b32[i]) return false;
    if (n & 3) {
        const a8 = new Uint8Array(a), b8 = new Uint8Array(b);
        for (let i = w << 2; i < n; i++) if (a8[i] !== b8[i]) return false;
    }
    return true;
}

/** К какой группе настроек относится поле источника снимка. */
function groupOf(label: string, f: Il2Cpp.Field): string {
    if (label === "Z_Database") {
        if (f.name.startsWith("AllyUnitVar_")) return "g_vars";
        const t = f.type.name;
        return t.endsWith("[]") || t.startsWith("System.Collections.Generic.List<") ? "g_lists" : "g_other";
    }
    if (label === "hero") return f.name.startsWith("UnitApply_") || f.name.startsWith("BUnitApply_") ? "g_heroStat" : "g_heroOther";
    if (label === "C_Battle_Manager") return "g_mgrBattle";
    return "g_mgrAbility";
}

/**
 * Поле для снимка. Скаляры сравниваются по байтам блока памяти, разбор значения — только у изменившихся.
 * Массивы и списки читаются по указателю (содержимое меняется на месте). Obscured*, enum и константы — через мост, только при изменении.
 */
interface Spec {
    key: string;
    group: string;
    field: Il2Cpp.Field;
    off: number;
    size: number;
    kind: "scalar" | "slow" | "const" | "array" | "list";
    read?: (dv: DataView, o: number) => string;
    elem?: { size: number; parse: (buf: ArrayBuffer) => string[] };
    last?: ArrayBuffer;
}

interface Source {
    label: string;
    isStatic: boolean;
    cls: Il2Cpp.Class;
    /** Указатель на объект-владельца (null — сейчас недоступен). Для статических — база статических полей. */
    base: () => NativePointer | null;
    specs: Spec[];
    lo: number;
    hi: number;
    built: boolean;
    active: Spec[];
    sig: string;
    prev: ArrayBuffer | null;
    prevBase: string;
}

interface Hook {
    name: string;
    n: number;
    lastN: number;
    win: number;
    skip: boolean;
    listener: InvocationListener | null;
}

export default defineMod({
    id: "gamelog",
    name: "Журнал игры (отладка)",
    description: "Пишет в gamelog.txt все изменения значений и вызовы методов.",
    options: [
        { key: "interval", label: "Интервал снимка, с", default: 0.7, min: 0.5, max: 5, step: 0.5 },
        ...GROUPS.map(g => ({ key: g.key, label: g.label, default: 1, min: 0, max: 1, step: 1, hidden: true })),
        ...EXCLUDES.map(e => ({ key: e.key, label: e.label, default: 1, min: 0, max: 1, step: 1, hidden: true })),
    ],
    openSettings(ctx) {
        openChecklist({
            name: "ModKitGamelogSettings",
            title: "Журнал игры",
            subtitle: "Отметьте, что писать в gamelog.txt. Меняется сразу, без перезапуска.",
            sections: [
                { header: "Писать", items: GROUPS.map(g => ({ key: g.key, label: g.label })) },
                { header: "Не писать (шумные источники)", items: EXCLUDES.map(e => ({ key: e.key, label: e.label })) },
            ],
            get: key => ctx.settings[key] !== 0,
            set: (key, v) => ctx.setSetting(key, v ? 1 : 0),
        });
    },
    onEnable(ctx) {
        const file = new File(OUT, "a");
        chmod644(OUT);
        const t0 = Date.now();
        const w = (s: string) => {
            try { file.write(`[${((Date.now() - t0) / 1000).toFixed(1)}] ${s}\n`); } catch (_) { /* нет файла */ }
        };
        const flush = () => { try { file.flush(); } catch (_) { /* */ } };
        ctx.onCleanup(() => { try { file.close(); } catch (_) { /* */ } });
        w("===== сессия начата =====");

        // ---- настройки (читаются на лету, меняются на экране «Настройки») ----
        const on = (group: string) => ctx.settings[group] !== 0;
        const excludedValues = () => new Set(EXCLUDES.filter(e => e.value && ctx.settings[e.key] !== 0).map(e => e.value!));
        const excludedCalls = () => EXCLUDES.filter(e => e.call && ctx.settings[e.key] !== 0).map(e => e.call!);
        let exValues = excludedValues();
        let exCalls = excludedCalls();
        let callsOn = on("g_calls");
        let artOn = on("g_art");
        let statusOn = on("g_status");
        const tickMs = () => Math.max(300, Math.round((ctx.settings.interval ?? 0.7) * 1000));
        w(`настройки: выключено групп ${GROUPS.filter(g => !on(g.key)).length}, исключений ${exValues.size + exCalls.length}, интервал ${tickMs()} мс`);

        // ---- захват живых объектов через хуки ----
        const seen: Record<string, { ptr: NativePointer; at: number }> = {};
        const grab = (cls: string, method: string, n: number) => {
            const m = ctx.cls(cls).tryMethod(method, n);
            if (!m) return;
            const l = Interceptor.attach(m.virtualAddress, { onEnter(args) { seen[cls] = { ptr: args[0], at: Date.now() }; } });
            ctx.onCleanup(() => l.detach());
        };
        grab("C_Battle_Manager", "Update", 0);
        grab("V_Ability_Manager", "AbilityStatApply", 1);
        grab("V_Ability_Manager", "Update", 0);

        // ---- снимки состояния ----
        const fresh = (c: string): NativePointer | null => {
            const s = seen[c];
            return s && Date.now() - s.at < 2000 ? s.ptr : null;
        };
        const sources: Source[] = [];
        const mkSource = (label: string, cls: Il2Cpp.Class, isStatic: boolean, base: () => NativePointer | null): Source => ({
            label, isStatic, cls, base, specs: [], lo: 0, hi: 0, built: false, active: [], sig: "", prev: null, prevBase: "",
        });
        const zdb = ctx.cls("Z_Database");
        sources.push(mkSource("Z_Database", zdb, true, () => zdb.staticFieldsData));
        for (const c of INSTANCE_CLASSES) sources.push(mkSource(c, ctx.cls(c), false, () => fresh(c)));

        // герой — компонент среди детей объекта поля менеджера боя; ищем через Unity один раз и кешируем, пока объект жив
        const heroCls = ctx.cls("C_Battle_AllyUnit");
        const unitOff = ctx.cls("C_Battle_Manager").field("Obj_FieldAllyUnit").offset;
        let heroUnit = "";
        let heroPtr: NativePointer | null = null;
        const findHero = (): NativePointer | null => {
            const mgr = fresh("C_Battle_Manager");
            if (!mgr) return null;
            const unitPtr = mgr.add(unitOff).readPointer();
            if (unitPtr.isNull()) return null;
            if (heroPtr && heroUnit === unitPtr.toString() && !heroPtr.add(CACHED_PTR).readPointer().isNull()) return heroPtr;
            heroPtr = null;
            const tf = new Il2Cpp.Object(unitPtr).method<Il2Cpp.Object>("get_transform").invoke();
            const type = heroCls.type.object;
            for (let i = 0; i < tf.method<number>("get_childCount").invoke(); i++) {
                const go = tf.method<Il2Cpp.Object>("GetChild").invoke(i).method<Il2Cpp.Object>("get_gameObject").invoke();
                const c = go.method<Il2Cpp.Object>("GetComponent", 1).invoke(type);
                if (c && !c.isNull()) { heroPtr = c.handle; heroUnit = unitPtr.toString(); break; }
            }
            return heroPtr;
        };
        sources.push(mkSource("hero", heroCls, false, findHero));

        let listOffsets: { items: number; size: number } | null = null;

        /** Классифицирует поля источника и вычисляет границы блока памяти. Один раз на источник. */
        const build = (src: Source) => {
            const t = Date.now();
            for (const f of src.cls.fields) {
                if (f.isStatic !== src.isStatic || SKIP_FIELD.test(f.name)) continue;
                let spec: Spec | null = null;
                try {
                    const ft = f.type, tn = ft.name;
                    const base = { key: `${src.label}.${f.name}`, group: groupOf(src.label, f), field: f, off: f.offset };
                    if (src.isStatic && (f.isLiteral || f.isThreadStatic)) {
                        spec = { ...base, size: 0, kind: "const" };
                    } else if (SCALAR_READERS[tn]) {
                        spec = { ...base, size: SCALAR_READERS[tn].size, kind: "scalar", read: SCALAR_READERS[tn].read };
                    } else if (ft.class.namespace === "CodeStage.AntiCheat.ObscuredTypes" || ft.class.isEnum) {
                        spec = { ...base, size: ft.class.valueTypeSize, kind: "slow" };
                    } else if (tn.endsWith("[]")) {
                        const elem = elementReader(tn.replace(/\[\]$/, ""));
                        if (elem) spec = { ...base, size: Process.pointerSize, kind: "array", elem };
                    } else if (tn.startsWith("System.Collections.Generic.List<")) {
                        const m = /^System\.Collections\.Generic\.List<(System\.\w+)>$/.exec(tn);
                        const elem = m && elementReader(m[1]);
                        if (elem) {
                            spec = { ...base, size: Process.pointerSize, kind: "list", elem };
                            listOffsets ??= { items: ft.class.field("_items").offset, size: ft.class.field("_size").offset };
                        }
                    }
                } catch (_) { /* тип без поддержки */ }
                if (spec && spec.kind !== "const" && (spec.off < 0 || spec.size <= 0)) spec = null;
                if (spec) src.specs.push(spec);
            }
            const mem = src.specs.filter(s => s.kind !== "const");
            src.lo = mem.length ? Math.min(...mem.map(s => s.off)) & ~3 : 0;
            src.hi = mem.length ? (mem.reduce((m, s) => Math.max(m, s.off + s.size), 0) + 3) & ~3 : 0;
            src.built = true;
            w(`подготовка ${src.label}: полей ${src.specs.length}, блок ${src.hi - src.lo} байт, ${Date.now() - t} мс`);
            // самопроверка: смещения статических полей должны давать те же значения, что и мост IL2CPP
            if (src.isStatic) {
                const base = src.base()!;
                const dv = new DataView(base.add(src.lo).readByteArray(src.hi - src.lo)!);
                let bad = 0, checked = 0;
                for (const s of src.specs) {
                    if (s.kind !== "scalar" || checked >= 30) continue;
                    const ref = readSlow(s.field);
                    if (ref === null) continue;                        // мост не умеет читать (например Int64)
                    checked++;
                    if (s.read!(dv, s.off - src.lo) !== ref) bad++;
                }
                if (bad) {
                    w(`ОШИБКА: смещения статических полей ${src.label} не совпали с мостом (${bad} из ${checked}), источник отключён`);
                    src.specs = [];
                }
            }
        };

        const prevVals = new Map<string, string>();
        const winChanges = new Map<string, number>();
        const muted = new Set<string>();
        let windowStart = Date.now();
        let sigAll = "";

        /** Содержимое массива/списка (null — не поддерживается, undefined — не изменилось с прошлого раза). */
        const readRef = (s: Spec, ptr: NativePointer, force: boolean): string | undefined => {
            let items: NativePointer, n: number, label: string;
            if (ptr.isNull()) { s.last = undefined; return "null"; }
            if (s.kind === "array") {
                n = ptr.add(ARRAY_LENGTH).readU32();
                items = ptr;
                label = "len";
            } else {
                n = ptr.add(listOffsets!.size).readS32();
                items = ptr.add(listOffsets!.items).readPointer();
                label = "count";
            }
            if (n > MAX_ELEMS) { s.last = undefined; return `${label}=${n}`; }
            if (n === 0 || items.isNull()) { s.last = undefined; return "[]"; }
            const buf = items.add(ARRAY_DATA).readByteArray(n * s.elem!.size);
            if (!buf) return undefined;
            const same = !force && s.last !== undefined && sameBytes(buf, s.last);
            s.last = buf;
            return same ? undefined : "[" + s.elem!.parse(buf).join(",") + "]";
        };

        const snapshot = (src: Source) => {
            if (!src.built) build(src);
            if (src.sig !== sigAll) {                                   // группы/исключения изменились: пересчёт, значения сверяются заново
                src.sig = sigAll;
                src.active = src.specs.filter(s => on(s.group) && !exValues.has(s.key));
                src.prev = null;
            }
            if (!src.active.length || !src.specs.length) return;
            const base = src.base();
            if (!base) { src.prev = null; src.prevBase = ""; return; }
            const baseStr = base.toString();
            if (baseStr !== src.prevBase) { src.prev = null; src.prevBase = baseStr; }   // другой объект: сверяем значения целиком
            const raw = base.add(src.lo).readByteArray(src.hi - src.lo);
            if (!raw) { src.prev = null; return; }
            const dv = new DataView(raw);
            const c32 = new Uint32Array(raw, 0, raw.byteLength >> 2);
            const prev = src.prev;
            const p32 = prev ? new Uint32Array(prev, 0, prev.byteLength >> 2) : null;
            const p8 = prev ? new Uint8Array(prev) : null;
            const c8 = prev ? new Uint8Array(raw) : null;
            const force = !prev;
            let baseline = 0;

            const emit = (key: string, val: string) => {
                const old = prevVals.get(key);
                prevVals.set(key, val);
                if (old === undefined) {
                    w(`INIT ${key} = ${val}`);
                    baseline++;
                    return;
                }
                if (old === val) return;
                const n = (winChanges.get(key) ?? 0) + 1;
                winChanges.set(key, n);
                if (muted.has(key)) return;
                if (n > NOISY_CHANGES) {
                    muted.add(key);
                    w(`NOISY ${key} (часто меняется, дальше не пишу), последнее: ${val}`);
                    return;
                }
                w(`SET ${key}: ${old} -> ${val}`);
            };

            for (const s of src.active) {
                try {
                    const o = s.off - src.lo;
                    if (s.kind === "array" || s.kind === "list") {
                        const val = readRef(s, base.add(s.off).readPointer(), force);
                        if (val !== undefined) emit(s.key, val);
                        continue;
                    }
                    if (s.kind === "const") {
                        if (force) { const v = readSlow(s.field); if (v !== null) emit(s.key, v); }
                        continue;
                    }
                    if (!force) {                                       // изменились ли байты поля
                        let diff = false;
                        if (s.size === 4 && (o & 3) === 0) diff = c32[o >> 2] !== p32![o >> 2];
                        else for (let i = 0; i < s.size; i++) if (c8![o + i] !== p8![o + i]) { diff = true; break; }
                        if (!diff) continue;
                    }
                    let val: string | null;
                    if (s.kind === "scalar") val = s.read!(dv, o);
                    else val = readSlow(src.isStatic ? s.field : (s.field as any).bind(new Il2Cpp.Object(base)));
                    if (val !== null) emit(s.key, val);
                } catch (_) { /* поле недоступно */ }
            }
            src.prev = raw;
            if (baseline) w(`-- ${src.label}: начальных значений ${baseline}`);
        };

        const tick = () => {
            exValues = excludedValues();                                 // настройки могли измениться на экране «Настройки»
            exCalls = excludedCalls();
            callsOn = on("g_calls");
            artOn = on("g_art");
            statusOn = on("g_status");
            refreshCalls();
            sigAll = GROUPS.map(g => on(g.key) ? 1 : 0).join("") + "|" + [...exValues].join(",");
            if (Date.now() - windowStart > WINDOW_MS) {
                windowStart = Date.now();
                winChanges.clear();
            }
            for (const src of sources) {
                try { snapshot(src); } catch (e) { src.prev = null; w(`ошибка снимка ${src.label}: ${e}`); }
            }
            flush();
        };
        // снимок читает память игры: из фонового потока это конфликтует с главным потоком игры (зависания на старте),
        // поэтому выполняем на главном потоке — из Update меню и боя, не чаще раза в интервал из настроек
        // и не больше доли времени: после долгого снимка пауза пропорционально длиннее
        let nextTick = 0;
        const onMain = () => {
            const now = Date.now();
            if (now < nextTick) return;
            try { tick(); } catch (e) { w(`ошибка снимка: ${e}`); }
            const dt = Date.now() - now;
            nextTick = now + Math.max(tickMs(), dt * TICK_COOLDOWN);
            if (dt > 100) w(`МЕДЛЕННО: снимок занял ${dt} мс (главный поток; отключите тяжёлые группы в «Настройки»)`);
        };
        for (const cn of ["A_MainMenuInformation", "C_Battle_Manager"]) {
            const l = Interceptor.attach(ctx.cls(cn).method("Update", 0).virtualAddress, { onEnter: onMain });
            ctx.onCleanup(() => l.detach());
        }

        // ---- трассировка вызовов ----
        const hooks: Hook[] = [];
        let callsSig = "";
        /** Помечает исключённые вызовы заранее, чтобы в обработчике не сравнивать строки на каждый вызов. */
        const refreshCalls = () => {
            const sig = exCalls.join(",");
            if (sig === callsSig) return;
            callsSig = sig;
            for (const h of hooks) h.skip = exCalls.some(p => h.name.startsWith(p + "("));
        };
        for (const c of TRACE_CLASSES) {
            for (const m of ctx.cls(c).methods) {
                if (SKIP_METHOD.test(m.name) || m.virtualAddress.isNull()) continue;
                const h: Hook = {
                    name: `${c}.${m.name}(${m.parameters.map(p => p.type.name.split(".").pop()).join(",")})`,
                    n: 0, lastN: 0, win: 0, skip: exCalls.some(p => `${c}.${m.name}(`.startsWith(p + "(")), listener: null,
                };
                h.listener = Interceptor.attach(m.virtualAddress, {
                    onEnter() {
                        if (!callsOn || h.skip) return;
                        h.n++;
                        if (++h.win === HOT_CALLS_PER_FLUSH) {
                            h.listener!.detach();
                            w(`HOT ${h.name} (> ${HOT_CALLS_PER_FLUSH} вызовов за ${CALLS_FLUSH_MS / 1000}с, трассировку снял)`);
                        }
                    },
                });
                ctx.onCleanup(() => h.listener!.detach());
                hooks.push(h);
            }
        }
        callsSig = exCalls.join(",");
        w(`трассировка: методов под наблюдением ${hooks.length}`);


        // ---- окно характеристик героя (то, что игрок видит на паузе): тексты строк при каждом обновлении ----
        const texts = (arr: Il2Cpp.Array<Il2Cpp.Object>): string[] => {
            const out: string[] = [];
            for (let i = 0; i < arr.length; i++) {
                const t = arr.get(i);
                out.push(t.isNull() ? "" : (t.method<Il2Cpp.String>("get_text").invoke().content ?? "").replace(/\s+/g, " ").trim());
            }
            return out;
        };
        let labelsLogged = false;
        let lastStatus = "";
        const statusMethod = ctx.cls("C_Battle_AllyUnit").tryMethod("Display_BattleStatus", 0);
        if (statusMethod) {
            const l = Interceptor.attach(statusMethod.virtualAddress, {
                onEnter(args) { (this as any).self = args[0]; },
                onLeave() {
                    if (!statusOn) return;
                    try {
                        const hero = new Il2Cpp.Object((this as any).self);
                        const values = texts(hero.field<Il2Cpp.Array<Il2Cpp.Object>>("Array_BattleStatus_Value").value);
                        const mgr = seen["C_Battle_Manager"];
                        if (!labelsLogged && mgr) {
                            const names = texts(new Il2Cpp.Object(mgr.ptr).field<Il2Cpp.Array<Il2Cpp.Object>>("Array_TextLanguage_BattleStatus").value);
                            w("STATUS названия строк: " + names.map((n, i) => `${i}=${n}`).join(" | "));
                            labelsLogged = true;
                        }
                        const line = values.map((v, i) => `${i}=${v}`).join(" | ");
                        if (line === lastStatus) return;                 // окно обновляется постоянно, а значения не меняются
                        lastStatus = line;
                        w("STATUS значения: " + line);
                    } catch (e) { w("ошибка STATUS: " + e); }
                },
            });
            ctx.onCleanup(() => l.detach());
        } else w("STATUS: метод Display_BattleStatus не найден");

        // ---- сундуки, выбор артефактов, торговец ----
        const am = ctx.cls(ARTIFACT_CLASS);
        const ints = (o: Il2Cpp.Object, f: string): string => {
            try {
                const l = o.field<Il2Cpp.Object>(f).value;
                if (l.isNull()) return "null";
                const n = l.method<number>("get_Count").invoke();
                const out: number[] = [];
                for (let i = 0; i < n; i++) out.push(Number(l.method("get_Item").invoke(i)));
                return "[" + out.join(",") + "]";
            } catch (e) { return "<ошибка " + e + ">"; }
        };
        const dumpOffer = (self: Il2Cpp.Object, why: string) => {
            const costs = (() => {
                try {
                    const l = am.field<Il2Cpp.Object>("List_MerchantSelectCost").value;
                    const n = l.method<number>("get_Count").invoke();
                    return Array.from({ length: n }, (_, i) => Number(l.method("get_Item").invoke(i)));
                } catch (_) { return []; }
            })();
            w(`OFFER ${why}: ArtifactType=${zdb.field<number>("ArtifactType").value} select=${ints(self, "List_ArtifactSelectID")} merchant=${ints(self, "List_MerchantSelectID")} costs=[${costs.join(",")}]`);
        };
        for (const m of am.methods) {
            if (m.name.startsWith("<") || SKIP_METHOD.test(m.name) || m.virtualAddress.isNull()) continue;
            const isInt = m.parameters.map(p => p.type.name === "System.Int32");
            const sig = `${ARTIFACT_CLASS}.${m.name}`;
            const l = Interceptor.attach(m.virtualAddress, {
                onEnter(args) {
                    (this as any).self = args[0];
                    if (!artOn) return;
                    const a = m.parameters.map((p, i) => isInt[i] ? `${p.name}=${args[i + 1].toInt32()}` : p.name).join(" ");
                    w(`ART ${sig}(${a})`);
                },
                onLeave() {
                    if (!artOn) return;
                    if (/^(Create_Artifact|Create_Merchant|Button_Artifact_Change|Button_Artifact_Select)$/.test(m.name)) {
                        try { dumpOffer(new Il2Cpp.Object((this as any).self), m.name); } catch (e) { w("ошибка OFFER: " + e); }
                    }
                },
            });
            ctx.onCleanup(() => l.detach());
        }

        // редкие вызовы пишем по мере появления, частые — сводкой
        ctx.every(CALLS_FLUSH_MS, () => {
            const lines: string[] = [];
            for (const h of hooks) {
                const d = h.n - h.lastN;
                if (d > 0) lines.push(`${h.name}×${d}`);
                h.lastN = h.n;
                h.win = 0;
            }
            if (lines.length) w("CALLS " + lines.join("  "));
        });
    },
});
