// Аддон: берёт API библиотеки (она уже включена, т.к. указана в depends) и слушает её событие.
ModKit.define({
    onEnable(ctx) {
        const lib = ctx.mod("test_lib");
        ctx.log("API библиотеки: " + (lib ? "есть" : "НЕТ"));
        ctx.events.on("test_lib:kill", e => { if (e.total % 25 === 0) ctx.log("убито " + e.total + ", появилось " + lib.spawned()); });
        ctx.events.on("bossSpawn", e => ctx.log("босс: id=" + e.unitId + " x" + e.count));
        ctx.events.on("battleEnd", () => ctx.log("бой окончен: убито " + lib.kills() + " из " + lib.spawned()));
    },
});
