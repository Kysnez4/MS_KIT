import { defineMod } from "../api";

/**
 * ПРИМЕР нового артефакта через ctx.data.addArtifact: копия существующего (ID 42 «Алмаз») с другими эффектами и текстом.
 * Всё остальное (строка таблицы, иконка, пулы выпадения, окно описания, плитка в энциклопедии) делает загрузчик.
 * Эффекты берутся из уже существующих «ID эффекта» (список — modkit/docs/EFFECTS.md).
 */
export default defineMod({
    id: "data_new_artifact",
    restartRequired: true,
    name: "Новый артефакт",
    description: "Артефакт «Мощь ModKit»: атака и здоровье +100%, перезарядка −20%.",
    options: [{ key: "guaranteed", label: "Гарантированное выпадение (тест)", default: 1, min: 0, max: 1, step: 1 }],
    onEnable(ctx) {
        ctx.data.whenReady(() => {
            ctx.data.addArtifact({
                name: "Мощь ModKit",
                effects: [[4, 100], [20, 100], [6, 20]],   // эффект 4 = атака %, 20 = макс. здоровье %, 6 = перезарядка −%
                lines: ["Атака +100%", "Максимальное здоровье +100%", "Время перезарядки всех заклинаний -20%"],
                guaranteed: ctx.settings.guaranteed >= 1,
            });
        });
    },
});
