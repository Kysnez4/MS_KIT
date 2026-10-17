// Заменяет тестовый метод, который игра не вызывает: безопасно для проверки конфликта.
ModKit.define({
    onEnable(ctx) {
        ctx.replace("C_Battle_Manager", "Test_TimeSec", 4, function () { return null; });
        ctx.log("конфликт a: метод заменён");
    },
});
