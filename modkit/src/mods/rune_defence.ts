import { defineMod } from "../api";

/**
 * Свой условный эффект: «если здоровье героя ниже порога — срабатывает эффект руны защиты» (C_Battle_AllyUnit.Unit_Rune_DefenceDivine).
 * Проверка раз в ~0,25 с; перезарядка считается с конца руны (DivineTime = 0), а не с момента срабатывания. Это событие по состоянию: ни ID эффекта, ни артефакта не нужно —
 * мод сам читает Unit_RemainHpRatio и зовёт метод игры. В журнал пишется всё, что нужно для проверки (доля HP, время руны, флаги).
 */
export default defineMod({
    id: "rune_defence",
    name: "Руна защиты при низком HP",
    description: "Когда здоровье героя ниже порога, включает эффект руны защиты (перезарядка с конца руны).",
    defaultEnabled: false,
    options: [
        { key: "thr", label: "Порог HP, %", default: 15, min: 1, max: 90, step: 1 },
        { key: "cd", label: "Перезарядка, с", default: 30, min: 5, max: 300, step: 5 },
    ],
    onEnable(ctx) {
        const img = Il2Cpp.domain.assembly("Assembly-CSharp").image;
        const heroCls = img.class("C_Battle_AllyUnit");
        const heroType = heroCls.type.object;

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
        const num = (h: Il2Cpp.Object, n: string): string => { try { return String(Math.round(Number(h.field<number>(n).value) * 1000) / 1000); } catch (_) { return "?"; } };
        const stat = (n: string): string => { try { return String(Math.round(Number(heroCls.field<number>(n).value) * 1000) / 1000); } catch (_) { return "?"; } };
        const snap = (h: Il2Cpp.Object) =>
            `hp=${num(h, "Unit_RemainHpRatio")} runeDef=${num(h, "ApplyRuneTime_Defence")} divineTime=${num(h, "DivineTime")} IsDivine=${stat("IsDivine")}`;

        let tick = 0, lastEnd = -1e9, fireAt = 0, active = false, started = false, lastLow = false;
        ctx.before("C_Battle_Manager", "Update", 0, bm => {
            if (++tick % 15 !== 0) return;
            const hero = findHero(bm);
            if (!hero) { started = false; return; }
            const ratio = Number(hero.field<number>("Unit_RemainHpRatio").value);
            if (!started) { started = true; ctx.log(`[rune_defence] герой найден: ${snap(hero)} (порог ${ctx.settings.thr}%)`); }
            const frac = ratio > 1.5 ? ratio / 100 : ratio;                    // на случай, если игра хранит проценты
            const low = frac > 0 && frac < ctx.settings.thr / 100;
            const now = Date.now() / 1000;
            if (low && !lastLow) ctx.log(`[rune_defence] HP ниже порога: ${snap(hero)}`);
            lastLow = low;
            // перезарядка считается с конца руны: пока она действует, ждём DivineTime = 0 (страховка — 60 с)
            if (active) {
                const left = Number(hero.field<number>("DivineTime").value);
                if (left <= 0.01 || now - fireAt > 60) { active = false; lastEnd = now; ctx.log(`[rune_defence] руна закончилась, перезарядка ${ctx.settings.cd} с пошла`); }
                return;
            }
            if (!low || now - lastEnd < ctx.settings.cd) return;
            fireAt = now; active = true;
            ctx.log(`[rune_defence] СРАБАТЫВАНИЕ до: ${snap(hero)}`);
            try { hero.method("Unit_Rune_DefenceDivine", 0).invoke(); } catch (e) { ctx.log(`[rune_defence] ошибка вызова: ${e}`); }
            ctx.log(`[rune_defence] после: ${snap(hero)}`);
        });
    },
});
