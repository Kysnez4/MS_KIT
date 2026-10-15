// ПРИМЕР внешнего мода: СВОИ эффекты артефактов (customEffects). Положите в Download/ModKit/ (или
// Android/data/com.vkslrzm.Zombie/files/modkit/mods/) и нажмите «+» в окне «Моды».
//
// 1) ctx.data.useBuiltinEffects() подключает встроенные эффекты:
//      10001 урон магической пули %      10002 перезарядка заклинаний −%
//      10003 снарядов магической пули и молний +N      10004 радиус сбора предметов %
// 2) ctx.data.defineEffect({...}) регистрирует свой: id >= 10000, apply(hero, value, ctx) вызывается после каждого
//    пересчёта характеристик игрой. hero.field("UnitApply_...").value отдаёт БАЗУ и не накапливается — просто меняйте его.
// 3) Артефакт создаётся обычным addArtifact с effects: [[ID, значение]].
ModKit.define({
    id: "ext_custom_effect_demo",
    name: "Демо: свои эффекты артефактов",
    description: "Артефакт «Демо-самоцвет»: пуля +50%, перезарядка −10%, радиус сбора +30%, свой эффект: огненный шар +100%.",
    onEnable(ctx) {
        ctx.data.useBuiltinEffects();
        ctx.data.defineEffect({
            id: 10100,
            name: "Урон огненного шара %",
            apply(hero, value, ctx) {
                hero.field("UnitApply_FireballDamage").value *= 1 + value / 100;
                // то же через хелперы: hero.mulPercent("UnitApply_FireballDamage", value); hero.add("UnitApply_FireballNum", 1);
                // статические переменные игры: hero.db("AllyUnitVar_MagicSizeRatioU").value += 5;
            },
        });
        ctx.data.whenReady(() => {
            const id = ctx.data.addArtifact({
                name: "Демо-самоцвет",
                effects: [[10001, 50], [10002, 10], [10004, 30], [10100, 100]],
                lines: ["Урон магической пули +50%", "Перезарядка −10%", "Радиус сбора +30%", "Урон огненного шара +100%"],
                guaranteed: true,        // тест: выпадает почти всегда
            });
            ctx.log("артефакт с кастомными эффектами: ID " + id);
            // комбинация тем же способом (образец 1, требования навыков — как у образца):
            // ctx.data.addCombo({ name: "Демо-комбо", cloneFrom: 1, effects: [[10003, 1]], lines: ["Снарядов и молний +1"] });
        });
    },
});
