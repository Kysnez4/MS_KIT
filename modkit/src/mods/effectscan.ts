import { defineMod } from "../api";

/**
 * Условные эффекты игры: ID -> типичное значение из таблиц (Ability / MagicCom / Class / Synergy).
 * Сканер подставляет каждый ID как глобальный эффект (`ctx.data.setEffects`), просит игру пересчитать характеристики
 * и пишет, какие поля героя, переменные AllyUnitVar_* и статики менеджера способностей изменились.
 */
const PROBES: Array<[number, number]> = [
    [27, 1], [28, 1], [29, 800], [48, 3], [50, 1], [52, 1], [60, 5], [62, 2], [63, 1], [68, 3], [69, 500], [70, 1800], [72, 3000], [74, 100],
    [78, 900], [84, 1], [86, 1], [89, 300], [91, 5000], [92, 2000], [95, 8], [98, 15], [99, 1], [102, 3], [107, 1], [108, 1], [111, -1],
    [114, 1], [116, 1], [118, 5], [122, 100], [123, 5], [128, 1], [130, 15], [131, 10000], [134, 1], [136, 4500], [137, 1], [138, 50],
    [140, 2500], [141, 1], [144, 20], [146, 20], [147, 1], [148, 1], [149, 1], [197, 1], [235, 1], [238, 10], [265, 12], [315, 100],
    [336, 3], [360, 10], [379, 50], [410, 5], [447, -40], [475, 100], [548, 8], [568, 15], [593, 1], [706, 30], [708, 75], [730, 30],
    [731, 25], [733, 2500],
];

/** Режим 1: реальные артефакты с условными/активными эффектами (Ability.ID): Охотник, Неприкосновенность, Загробный мир, Созидание, Зелье, Пандора… */
const ARTIFACTS = [164, 153, 193, 57, 65, 83, 85, 47, 201, 54, 72, 73, 176, 98, 101, 160, 171, 194, 158, 123, 199];

const FRAMES = 30;                                   // кадров на фазу (≈0,5 с)
const NUM_TYPES = new Set(["System.Boolean", "System.Int32", "System.Single"]);
const SKIP = /(Time|Timer|Pos|Vector|Rotation|Frame|Index|Count|Distance|Speed)$|^(UnitBase_|BUnit)/;   // шум; Count оставляем только у Apply*

const fmt = (v: any): string => typeof v === "number" ? String(Math.round(v * 10000) / 10000) : String(v);

/**
 * Отладка. В тестовом бою по очереди подставляет условные эффекты и логирует `[effectscan] эффект ID=значение: поле: было -> стало`.
 * Сначала две «пустые» пробы: поля, которые меняются сами (шум), в отчёт не попадают. После пробы значения возвращаются.
 */
export default defineMod({
    id: "effectscan",
    name: "Сканер условных эффектов (отладка)",
    description: "В бою по очереди подставляет условные эффекты игры и пишет в modkit.log, какие поля героя они меняют. Только для тестового боя.",
    defaultEnabled: false,
    options: [
        { key: "mode", label: "Режим: 0 эффекты, 1 артефакты", default: 1, min: 0, max: 1, step: 1 },
    ],
    onEnable(ctx) {
        const img = Il2Cpp.domain.assembly("Assembly-CSharp").image;
        const zCls = img.class("Z_Database");
        const heroCls = img.class("C_Battle_AllyUnit");
        const heroType = heroCls.type.object;
        const abilCls = img.class("V_Ability_Manager");
        const stat = abilCls.method("AbilityStatApply", 1);

        let mgr: Il2Cpp.Object | null = null;
        let applyType: number | null = null;
        // у V_Ability_Manager нет Update: берём объект из ManaGain (зовётся в бою при получении маны); обход кучи через gc слишком тяжёлый
        const mg = abilCls.tryMethod("ManaGain", 1);
        if (mg) {
            const h = Interceptor.attach(mg.virtualAddress, { onEnter(args) { if (!mgr) mgr = new Il2Cpp.Object(args[0]); } });
            ctx.onCleanup(() => h.detach());
        } else ctx.log("[effectscan] нет V_Ability_Manager.ManaGain");

        const findHero = (bm: Il2Cpp.Object): Il2Cpp.Object | null => {
            const field = bm.field<Il2Cpp.Object>("Obj_FieldAllyUnit").value;
            if (field.isNull()) return null;
            const tf = field.method<Il2Cpp.Object>("get_transform").invoke();
            for (let i = 0; i < tf.method<number>("get_childCount").invoke(); i++) {
                const go = tf.method<Il2Cpp.Object>("GetChild").invoke(i).method<Il2Cpp.Object>("get_gameObject").invoke();
                const c = go.method<Il2Cpp.Object>("GetComponent", 1).invoke(heroType);
                if (c && !c.isNull()) return c;
            }
            return null;
        };

        interface Ref { key: string; f: Il2Cpp.Field<any>; inst: boolean; type: string }
        const refs: Ref[] = [];
        const collectRefs = () => {
            refs.length = 0;
            for (const f of heroCls.fields) {
                if (!NUM_TYPES.has(f.type.name)) continue;
                if (SKIP.test(f.name) && !/^Apply/.test(f.name)) continue;
                refs.push({ key: "hero." + f.name, f, inst: !f.isStatic, type: f.type.name });
            }
            for (const f of zCls.fields) {
                if (!f.isStatic || !NUM_TYPES.has(f.type.name) || !/^(AllyUnitVar_|Game_)/.test(f.name)) continue;
                refs.push({ key: "Z." + f.name, f, inst: false, type: f.type.name });
            }
            for (const f of abilCls.fields) {
                if (!f.isStatic || !NUM_TYPES.has(f.type.name) || f.name === "Ability_SelectON") continue;
                refs.push({ key: "Abil." + f.name, f, inst: false, type: f.type.name });
            }
        };
        const read = (r: Ref, hero: Il2Cpp.Object): any => r.inst ? hero.field(r.f.name).value : r.f.value;
        const snapshot = (hero: Il2Cpp.Object): Map<string, string> => {
            const m = new Map<string, string>();
            for (const r of refs) { try { m.set(r.key, fmt(read(r, hero))); } catch (_) { /* */ } }
            return m;
        };
        const restore = (hero: Il2Cpp.Object, base: Map<string, string>) => {
            for (const r of refs) {
                const old = base.get(r.key);
                if (old === undefined) continue;
                try {
                    if (fmt(read(r, hero)) === old) continue;
                    const v = r.type === "System.Boolean" ? old === "true" : Number(old);
                    if (r.inst) hero.field(r.f.name).value = v as any; else r.f.value = v as any;
                } catch (_) { /* */ }
            }
        };
        const diff = (a: Map<string, string>, b: Map<string, string>, noise: Set<string>): string[] => {
            const out: string[] = [];
            for (const [k, v] of b) { const o = a.get(k); if (o !== undefined && o !== v && !noise.has(k)) out.push(`${k}: ${o} -> ${v}`); }
            return out;
        };

        let frames = 0, idx = 0, waited = 0, noMgr = 0, noHero = 0;
        let phase: "wait" | "A" | "B" | "done" = "wait";
        let base: Map<string, string> | null = null;
        const noise = new Set<string>();
        const art = ctx.settings.mode === 1;
        const queue: Array<[number, number]> = [[-1, 0], [-1, 0], ...(art ? ARTIFACTS.map(a => [a, 0] as [number, number]) : PROBES)];
        const obtained = () => zCls.field<Il2Cpp.Object>("List_ArtifactObtain").value;      // -1 — пустая проба (шум)
        const apply = () => (mgr as Il2Cpp.Object).method("AbilityStatApply", 1).invoke(applyType ?? 0);

        ctx.before("C_Battle_Manager", "Update", 0, bm => {
            if (phase === "done") return;
            if (frames === 0) ctx.log("[effectscan] хук боя сработал");
            if (++frames % FRAMES !== 0) return;
            try {
                const hero = findHero(bm);
                if (!hero) { if (++noHero % 10 === 1) ctx.log("[effectscan] героя не нашёл"); return; }
                if (!mgr) {
                    // ManaGain мог не позвать: через ~15 с один раз берём объект через gc (тяжело, но однократно)
                    if (++noMgr === 30) {
                        for (const o of Il2Cpp.gc.choose(abilCls)) { mgr = o; break; }
                        ctx.log(`[effectscan] менеджер через gc: ${mgr ? "найден" : "не найден"}`);
                    } else if (noMgr % 10 === 1) ctx.log("[effectscan] жду V_Ability_Manager (соберите крупицу маны)");
                    if (!mgr) return;
                }
                if (phase === "wait") {
                    if (++waited < 6) return;
                    if (applyType === null && waited < 40) return;
                    collectRefs();
                    ctx.log(`[effectscan] старт: ApplyType=${applyType ?? "не замечен (беру 0)"}, полей ${refs.length}, проб ${queue.length}`);
                    phase = "A"; idx = 0;
                }
                const [id, val] = queue[idx];
                if (phase === "A") {
                    base = snapshot(hero);
                    if (art) { if (id >= 0) obtained().method("Add").invoke(id); } else ctx.data.setEffects(id >= 0 ? [[id, val]] : []);
                    apply();
                    phase = "B";
                } else if (phase === "B") {
                    const after = snapshot(hero);
                    const d = diff(base!, after, noise);
                    if (id < 0) {
                        for (const line of diff(base!, after, new Set())) noise.add(line.split(":")[0]);
                        ctx.log(`[effectscan] пустая проба: шум ${noise.size} полей`);
                    } else {
                        ctx.log(`[effectscan] ${art ? "артефакт" : "эффект"} ${id}${art ? "" : "=" + val}: ${d.length ? d.join("; ") : "—"}`);
                    }
                    if (art) { if (id >= 0) obtained().method("Remove").invoke(id); } else ctx.data.setEffects([]);
                    apply();
                    restore(hero, base!);
                    if (++idx >= queue.length) { phase = "done"; ctx.log("[effectscan] готово"); } else phase = "A";
                }
            } catch (e) {
                ctx.log(`[effectscan] ошибка: ${e}`);
                try { ctx.data.setEffects([]); apply(); } catch (_) { /* */ }
                phase = "done";
            }
        });
    },
});
