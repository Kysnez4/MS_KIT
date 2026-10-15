import { defineMod } from "../api";

/**
 * ПРИМЕР правки существующей записи: артефакт «ASI» (ID 41) вместо «Атака +30%» даёт +N%.
 * Колонки таблиц названы по-корейски, их список — в modkit/docs/TABLES.md.
 */
export default defineMod({
    id: "data_edit_artifact",
    restartRequired: true,
    name: "Артефакт ASI усилен",
    description: "ASI (ID 41) даёт больше атаки.",
    options: [{ key: "attack", label: "Атака, %", default: 60, min: 30, max: 300, step: 30 }],
    onEnable(ctx) {
        ctx.data.whenReady(() => {
            const attack = ctx.settings.attack;
            ctx.data.edit("Ability", 41, {
                "효과ID_01값": attack,                    // значение первого эффекта (эффект 4 = «Атака, %»)
                "효과 설명줄01": `Атака +${attack}%`,     // текст в описании артефакта
            });
            ctx.log(`ASI теперь даёт +${attack}% атаки`);
        });
    },
});
