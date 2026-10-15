// Скрипт мода. id, название, описание и настройки берутся из mod.json (META) и главнее значений здесь.
ModKit.define({
    onEnable(ctx) {
        ctx.data.whenReady(() => {
            ctx.data.edit("MagicCom", 1, { "효과ID_07값": 8, "효과 설명줄03": "Число молний х8" });
        });
    },
});
