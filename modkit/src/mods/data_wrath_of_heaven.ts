import { defineMod } from "../api";

/**
 * Комбинация «Гнев небес» (таблица MagicCom, ID 1): вместо «Число молний ×3» — настраиваемое число.
 * Эффект 914 — множитель числа молний, его значение лежит в паре `효과ID_07` / `효과ID_07값`.
 */
export default defineMod({
    id: "data_wrath_of_heaven",
    restartRequired: true,
    name: "Гнев небес: больше молний",
    description: "Комбинация «Гнев небес»: число молний ×N вместо ×3.",
    options: [{ key: "count", label: "Число молний ×", default: 5, min: 3, max: 12, step: 1 }],
    onEnable(ctx) {
        ctx.data.whenReady(() => {
            const n = ctx.settings.count;
            ctx.data.edit("MagicCom", 1, {
                "효과ID_07값": n,                       // значение эффекта 914 («Число молний ×»)
                "효과 설명줄03": `Число молний х${n}`,   // строка в описании комбинации
            });
            ctx.log(`Гнев небес: число молний ×${n}`);
        });
    },
});
