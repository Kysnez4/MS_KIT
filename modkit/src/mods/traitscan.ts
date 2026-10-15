import { defineMod } from "../api";

/** ID трейтов (Ability, 유형 = 특성). */
const TRAITS = [362, 363, 364, 365, 366, 367, 371, 372, 373, 374, 375, 376, 380, 381, 382, 383, 384, 385, 389, 390, 391, 392, 393, 394,
    398, 399, 400, 401, 402, 403, 407, 408, 409, 410, 411, 412, 416, 417, 418, 419, 420, 421, 425, 426, 427, 428, 429, 430, 434, 435, 436,
    437, 438, 439, 443, 444, 445, 446, 447, 448, 452, 453, 454, 455, 456, 457, 461, 462, 463, 464, 465, 466, 470, 471, 472, 473, 474, 475,
    479, 480, 481, 482, 483, 484, 488, 489, 490, 491, 492, 493, 497, 498, 499, 500, 501, 502, 506, 507, 508, 509, 510, 511, 515, 516, 517,
    518, 519, 520, 524, 525, 526, 527, 528, 529, 533, 534, 535, 536, 537, 538, 542, 543, 544, 545, 546, 547];

const FRAMES = 30;                       // кадров на фазу (≈0,5 с)
const HERO_FIELD = /^(Is.+|.+ComID|UnitApply_.+|UnitBase_.+)$/;
const KEEP_FIELD = /^(Is.+|.+ComID)$/;   // флаги и ID комбинаций игра сама не сбрасывает — возвращаем вручную
const NUM_TYPES = new Set(["System.Boolean", "System.Int32", "System.Single"]);

const fmt = (v: any): string => typeof v === "number" ? String(Math.round(v * 10000) / 10000) : String(v);

/**
 * Отладка: по очереди «выучивает» каждый трейт (кладёт его ID в Z_Database.List_AllyAbilityLearn), просит игру пересчитать способности
 * (V_Ability_Manager.AbilityStatApply) и пишет в лог, какие флаги/поля героя и переменные AllyUnitVar_* изменились. Потом откатывает.
 * Запускается один раз после включения мода, когда начался бой. Запускать в тестовом бою!
 */
export default defineMod({
    id: "traitscan",
    name: "Сканер трейтов (отладка)",
    description: "В бою по очереди включает каждый трейт и пишет в modkit.log, какие флаги героя он меняет. Только для тестового боя.",
    defaultEnabled: false,
    options: [],
    onEnable(ctx) {
        const img = Il2Cpp.domain.assembly("Assembly-CSharp").image;
        const zCls = img.class("Z_Database");
        const heroCls = img.class("C_Battle_AllyUnit");
        const heroType = heroCls.type.object;
        const stat = img.class("V_Ability_Manager").method("AbilityStatApply", 1);

        let mgr: Il2Cpp.Object | null = null;
        let applyType: number | null = null;
        ctx.after("V_Ability_Manager", "Update", 0, self => { mgr = self; });
        const hk = Interceptor.attach(stat.virtualAddress, { onEnter(args) { applyType = args[1].toInt32(); } });
        ctx.onCleanup(() => hk.detach());

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

        const learnList = (): Il2Cpp.Object => zCls.field<Il2Cpp.Object>("List_AllyAbilityLearn").value;
        const listCount = (l: Il2Cpp.Object) => l.method<number>("get_Count").invoke();

        const snapshot = (hero: Il2Cpp.Object): Map<string, string> => {
            const m = new Map<string, string>();
            for (const f of heroCls.fields) {
                if (!HERO_FIELD.test(f.name) || !NUM_TYPES.has(f.type.name)) continue;
                try { m.set("hero." + f.name, fmt(f.isStatic ? f.value : hero.field(f.name).value)); } catch (_) { /* */ }
            }
            for (const f of zCls.fields) {
                if (!f.isStatic || !f.name.startsWith("AllyUnitVar_") || !NUM_TYPES.has(f.type.name)) continue;
                try { m.set("Z." + f.name, fmt(f.value)); } catch (_) { /* */ }
            }
            return m;
        };

        const restoreFlags = (hero: Il2Cpp.Object, base: Map<string, string>) => {
            for (const f of heroCls.fields) {
                if (!KEEP_FIELD.test(f.name) || f.isStatic || !NUM_TYPES.has(f.type.name)) continue;
                const old = base.get("hero." + f.name);
                if (old === undefined) continue;
                try {
                    const cur = hero.field(f.name);
                    if (fmt(cur.value) !== old) cur.value = (f.type.name === "System.Boolean" ? old === "true" : Number(old)) as any;
                } catch (_) { /* */ }
            }
        };

        const diff = (a: Map<string, string>, b: Map<string, string>): string[] => {
            const out: string[] = [];
            for (const [k, v] of b) { const o = a.get(k); if (o !== undefined && o !== v) out.push(`${k.replace(/^hero\.|^Z\./, m => m)}: ${o} -> ${v}`); }
            return out;
        };

        // ---- конечный автомат: фаза 0 — ждём бой, затем для каждого трейта фаза A (добавить+пересчитать) и фаза B (снять результат, откатить)
        let frames = 0, idx = -1, phase: "wait" | "A" | "B" | "done" = "wait", waited = 0;
        let base: Map<string, string> | null = null;
        let added = 0;
        const queue: number[] = [-1, ...TRAITS];   // -1 — пустая проба (шум)
        const apply = () => (mgr as Il2Cpp.Object).method("AbilityStatApply", 1).invoke(applyType ?? 0);

        ctx.after("C_Battle_Manager", "Update", 0, bm => {
            if (phase === "done" || ++frames % FRAMES !== 0) return;
            try {
                const hero = findHero(bm);
                if (!hero || !mgr) return;
                if (phase === "wait") {
                    if (++waited < 6) return;            // ≈3 с после появления героя
                    if (applyType === null && waited < 40) return;   // ждём первый вызов, чтобы узнать ApplyType игры
                    ctx.log(`[traitscan] старт: ApplyType=${applyType ?? "не замечен (беру 0)"}, проб ${queue.length}`);
                    phase = "A"; idx = 0;
                }
                const id = queue[idx];
                if (phase === "A") {
                    base = snapshot(hero);
                    if (id >= 0) {
                        const l = learnList();
                        l.method("Add").invoke(id);
                        added = id;
                    }
                    apply();
                    phase = "B";
                } else if (phase === "B") {
                    const after = snapshot(hero);
                    const d = diff(base!, after);
                    ctx.log(`[traitscan] ${id < 0 ? "пустая проба" : "трейт " + id}: ${d.length ? d.join("; ") : "—"}`);
                    if (id >= 0) {
                        const l = learnList();
                        const n = listCount(l);
                        if (n > 0 && l.method<number>("get_Item").invoke(n - 1) === added) l.method("RemoveAt").invoke(n - 1);
                        else l.method("Remove").invoke(added);
                    }
                    apply();
                    restoreFlags(hero, base!);
                    if (++idx >= queue.length) { phase = "done"; ctx.log("[traitscan] готово"); } else phase = "A";
                }
            } catch (e) {
                ctx.log(`[traitscan] ошибка: ${e}`);
                if (phase === "B" && added) { try { learnList().method("Remove").invoke(added); apply(); } catch (_) { /* */ } }
                phase = "done";
            }
        });
    },
});
