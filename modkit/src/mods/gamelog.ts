import { defineMod } from "../api";
import { DIR, chmod644 } from "../log";
import { openChecklist } from "../uikit";

const OUT = DIR + "/gamelog.txt";
const SCALARS = new Set(["System.Int32", "System.Single", "System.Double", "System.Boolean", "System.Int64", "System.Int16", "System.Byte", "System.UInt32"]);
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
const HOT_CALLS_PER_WINDOW = 400;

function round(v: number): number {
    return Math.round(v * 10000) / 10000;
}

/** Читает скалярное поле (в т.ч. Obscured*). null — тип не поддерживается. */
function readScalar(f: Il2Cpp.Field, typeName: string): string | null {
    const v = f.value as any;
    if (typeof v === "number") return String(round(v));
    if (typeof v === "boolean") return v ? "true" : "false";
    if (typeof v === "bigint") return v.toString();
    if (v && typeof v === "object" && OBSCURED.has(typeName.split(".").pop()!)) {
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

/** Числа из массива IL2CPP одним чтением памяти (данные идут с отступа 0x20), а не по элементу через вызовы. */
function readBlock(arr: Il2Cpp.Array<any>, count: number, elem: string): string | null {
    const r = elementReader(elem);
    if (!r) return null;
    if (count > 400) return `len=${count}`;
    if (count === 0) return "[]";
    const buf = arr.handle.add(0x20).readByteArray(count * r.size);
    return buf ? "[" + r.parse(buf).join(",") + "]" : null;
}

function readArray(f: Il2Cpp.Field): string | null {
    const v = f.value as any;
    if (!v || v.isNull?.()) return "null";
    const arr = v as Il2Cpp.Array<any>;
    return readBlock(arr, arr.length, f.type.name.replace(/\[\]$/, ""));
}

function readList(f: Il2Cpp.Field): string | null {
    const v = f.value as any;
    if (!v || v.isNull?.()) return "null";
    const m = /^System\.Collections\.Generic\.List<(System\.\w+)>$/.exec(f.type.name);
    if (!m || !elementReader(m[1])) return null;
    const n = v.field("_size").value as number;
    if (n > 400) return `count=${n}`;
    const items = v.field("_items").value as Il2Cpp.Array<any>;
    return n === 0 ? "[]" : readBlock(items, n, m[1]);
}

function readField(f: Il2Cpp.Field): string | null {
    if (SKIP_FIELD.test(f.name)) return null;
    const t = f.type;
    const tn = t.name;
    if (SCALARS.has(tn) || t.class.namespace === "CodeStage.AntiCheat.ObscuredTypes") return readScalar(f, tn);
    if (t.class.isEnum) return String(f.value);
    if (tn.endsWith("[]")) return readArray(f);
    if (tn.startsWith("System.Collections.Generic.List<")) return readList(f);
    return null;
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
        type Source = { label: string; fields: Il2Cpp.Field[]; holder: () => Il2Cpp.Object | null };
        const sources: Source[] = [];
        const zdb = ctx.cls("Z_Database");
        sources.push({ label: "Z_Database", fields: zdb.fields.filter(f => f.isStatic), holder: () => null });
        for (const c of INSTANCE_CLASSES) {
            const k = ctx.cls(c);
            sources.push({
                label: c,
                fields: k.fields.filter(f => !f.isStatic),
                holder: () => {
                    const s = seen[c];
                    return s && Date.now() - s.at < 2000 ? new Il2Cpp.Object(s.ptr) : null;
                },
            });
        }
        const heroCls = ctx.cls("C_Battle_AllyUnit");
        sources.push({
            label: "hero",
            fields: heroCls.fields.filter(f => !f.isStatic),
            holder: () => {
                const s = seen["C_Battle_Manager"];
                if (!s || Date.now() - s.at > 2000) return null;
                const field = new Il2Cpp.Object(s.ptr).field<Il2Cpp.Object>("Obj_FieldAllyUnit").value;
                if (field.isNull()) return null;
                const tf = field.method<Il2Cpp.Object>("get_transform").invoke();
                const type = heroCls.type.object;
                for (let i = 0; i < tf.method<number>("get_childCount").invoke(); i++) {
                    const go = tf.method<Il2Cpp.Object>("GetChild").invoke(i).method<Il2Cpp.Object>("get_gameObject").invoke();
                    const c = go.method<Il2Cpp.Object>("GetComponent", 1).invoke(type);
                    if (c && !c.isNull()) return c;
                }
                return null;
            },
        });

        const prev = new Map<string, string>();
        const winChanges = new Map<string, number>();
        const muted = new Set<string>();
        let first = new Set<string>();
        let windowStart = Date.now();

        const tick = () => {
            exValues = excludedValues();                                 // настройки могли измениться на экране «Настройки»
            exCalls = excludedCalls();
            if (Date.now() - windowStart > WINDOW_MS) {
                windowStart = Date.now();
                winChanges.clear();
            }
            for (const src of sources) {
                let holder: Il2Cpp.Object | null = null;
                if (!src.fields.length) continue;
                if (src.label !== "Z_Database") {
                    holder = src.holder();
                    if (!holder) { first.delete(src.label); continue; }
                }
                const fresh = !first.has(src.label);
                first.add(src.label);
                let baseline = 0;
                for (const f of src.fields) {
                    if (!on(groupOf(src.label, f))) continue;                    // группа выключена: поле даже не читаем
                    let val: string | null;
                    try { val = readField(holder ? holder.field(f.name) : f); } catch (_) { continue; }
                    if (val === null) continue;
                    const key = `${src.label}.${f.name}`;
                    if (exValues.has(key)) continue;
                    const old = prev.get(key);
                    prev.set(key, val);
                    if (old === undefined) {
                        w(`INIT ${key} = ${val}`);
                        baseline++;
                        continue;
                    }
                    if (old === val) continue;
                    const n = (winChanges.get(key) ?? 0) + 1;
                    winChanges.set(key, n);
                    if (muted.has(key)) continue;
                    if (n > NOISY_CHANGES) {
                        muted.add(key);
                        w(`NOISY ${key} (часто меняется, дальше не пишу), последнее: ${val}`);
                        continue;
                    }
                    w(`SET ${key}: ${old ?? "?"} -> ${val}`);
                }
                if (baseline) w(`-- ${src.label}: начальных значений ${baseline}`);
            }
            flush();
        };
        // снимок читает тысячи полей через IL2CPP: из фонового потока это конфликтует с главным потоком игры (зависания на старте),
        // поэтому выполняем на главном потоке — из Update меню и боя, не чаще раза в интервал из настроек
        let lastTick = 0;
        const onMain = () => {
            const now = Date.now();
            if (now - lastTick < tickMs()) return;
            lastTick = now;
            try { tick(); } catch (e) { w(`ошибка снимка: ${e}`); }
            const dt = Date.now() - now;
            if (dt > 100) w(`МЕДЛЕННО: снимок занял ${dt} мс (главный поток; отключите тяжёлые группы в «Настройки»)`);
        };
        for (const cn of ["A_MainMenuInformation", "C_Battle_Manager"]) {
            const l = Interceptor.attach(ctx.cls(cn).method("Update", 0).virtualAddress, { onEnter: onMain });
            ctx.onCleanup(() => l.detach());
        }

        // ---- трассировка вызовов ----
        const counts = new Map<string, number>();
        const winCalls = new Map<string, number>();
        let attached = 0;
        for (const c of TRACE_CLASSES) {
            for (const m of ctx.cls(c).methods) {
                if (SKIP_METHOD.test(m.name) || m.virtualAddress.isNull()) continue;
                const name = `${c}.${m.name}(${m.parameters.map(p => p.type.name.split(".").pop()).join(",")})`;
                const l = Interceptor.attach(m.virtualAddress, {
                    onEnter() {
                        if (!on("g_calls") || exCalls.some(p => name.startsWith(p + "("))) return;
                        counts.set(name, (counts.get(name) ?? 0) + 1);
                        const n = (winCalls.get(name) ?? 0) + 1;
                        winCalls.set(name, n);
                        if (n === HOT_CALLS_PER_WINDOW) {
                            l.detach();
                            w(`HOT ${name} (> ${HOT_CALLS_PER_WINDOW} вызовов за ${WINDOW_MS / 1000}с, трассировку снял)`);
                        }
                    },
                });
                ctx.onCleanup(() => l.detach());
                attached++;
            }
        }
        w(`трассировка: методов под наблюдением ${attached}`);


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
        const statusMethod = ctx.cls("C_Battle_AllyUnit").tryMethod("Display_BattleStatus", 0);
        if (statusMethod) {
            const l = Interceptor.attach(statusMethod.virtualAddress, {
                onEnter(args) { (this as any).self = args[0]; },
                onLeave() {
                    if (!on("g_status")) return;
                    try {
                        const hero = new Il2Cpp.Object((this as any).self);
                        const values = texts(hero.field<Il2Cpp.Array<Il2Cpp.Object>>("Array_BattleStatus_Value").value);
                        const mgr = seen["C_Battle_Manager"];
                        if (!labelsLogged && mgr) {
                            const names = texts(new Il2Cpp.Object(mgr.ptr).field<Il2Cpp.Array<Il2Cpp.Object>>("Array_TextLanguage_BattleStatus").value);
                            w("STATUS названия строк: " + names.map((n, i) => `${i}=${n}`).join(" | "));
                            labelsLogged = true;
                        }
                        w("STATUS значения: " + values.map((v, i) => `${i}=${v}`).join(" | "));
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
                    if (!on("g_art")) return;
                    const a = m.parameters.map((p, i) => isInt[i] ? `${p.name}=${args[i + 1].toInt32()}` : p.name).join(" ");
                    w(`ART ${sig}(${a})`);
                },
                onLeave() {
                    if (!on("g_art")) return;
                    if (/^(Create_Artifact|Create_Merchant|Button_Artifact_Change|Button_Artifact_Select)$/.test(m.name)) {
                        try { dumpOffer(new Il2Cpp.Object((this as any).self), m.name); } catch (e) { w("ошибка OFFER: " + e); }
                    }
                },
            });
            ctx.onCleanup(() => l.detach());
        }

        // редкие вызовы пишем по мере появления, частые — сводкой
        const lastSummary = new Map<string, number>();
        ctx.every(2000, () => {
            const lines: string[] = [];
            for (const [name, n] of counts) {
                const d = n - (lastSummary.get(name) ?? 0);
                if (d > 0) lines.push(`${name}×${d}`);
                lastSummary.set(name, n);
            }
            if (lines.length) w("CALLS " + lines.join("  "));
            winCalls.clear();
        });
    },
});
