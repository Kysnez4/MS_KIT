import { defineMod } from "../api";

/**
 * Тест «Охотник без артефакта»: после каждого пересчёта игрой (AbilityStatApply) пишет Z_Database.AllyUnitVar_PowerKillCondition = 100
 * (то же значение, что ставит артефакт «Охотник» ID 164) и раз в 2 с логирует, как реагируют поля героя ApplyPowerKill* и атака.
 * Ожидание: игра сама выведет ApplyPowerKillCondition = 100 и начнёт копить Sum (+1% атаки за каждого 100-го убитого, потолок 25%).
 */
export default defineMod({
    id: "huntertest",
    name: "Тест: Охотник без артефакта (отладка)",
    description: "Пишет AllyUnitVar_PowerKillCondition=100 после пересчёта и логирует ApplyPowerKill* и атаку. Только для тестового боя.",
    defaultEnabled: false,
    options: [
        { key: "cond", label: "Убийств на шаг (0 — выкл.)", default: 100, min: 0, max: 1000, step: 10 },
    ],
    onEnable(ctx) {
        const img = Il2Cpp.domain.assembly("Assembly-CSharp").image;
        const zCls = img.class("Z_Database");
        const heroCls = img.class("C_Battle_AllyUnit");
        const stat = img.class("V_Ability_Manager").method("AbilityStatApply", 1);
        const varF = zCls.field<number>("AllyUnitVar_PowerKillCondition");
        const sFields = ["ApplyPowerKillCondition", "ApplyPowerKillNum", "ApplyPowerKillInc", "ApplyPowerKillSum"].map(n => heroCls.field<number>(n));

        const h1 = Interceptor.attach(stat.virtualAddress, {
            onLeave() {
                const c = Math.round(ctx.settings.cond);
                if (c > 0) varF.value = c;
                ctx.log(`[huntertest] после пересчёта: Var=${varF.value}`);
            },
        });
        ctx.onCleanup(() => h1.detach());

        // игра заполняет статики героя только внутри пересчёта и сбрасывает их в «выкл.» (Condition = 9999999, Inc = 0): держим сами
        let tick = 0, last = "";
        ctx.before("C_Battle_Manager", "Update", 0, () => {
            const c = Math.round(ctx.settings.cond);
            if (c > 0 && tick % 10 === 0 && sFields[0].value !== c) { sFields[0].value = c; sFields[2].value = 1; }
            if (++tick % 120 !== 0) return;
            const s = sFields.map(f => `${f.name.replace("ApplyPowerKill", "")}=${f.value}`).join(" ");
            const line = `Var=${varF.value} ${s}`;
            if (line !== last) { last = line; ctx.log(`[huntertest] ${line}`); }
        });
        ctx.onCleanup(() => { try { varF.value = 0; } catch (_) { /* */ } });
    },
});
