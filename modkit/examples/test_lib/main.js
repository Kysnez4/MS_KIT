// Библиотечный мод: считает убийства в бою и публикует API через ctx.expose.
ModKit.define({
    onEnable(ctx) {
        let kills = 0, spawned = 0;
        ctx.events.on("battleStart", () => { kills = 0; spawned = 0; ctx.log("battleStart: счётчики сброшены"); });
        ctx.events.on("enemySpawn", () => { spawned++; });
        ctx.events.on("enemyDeath", () => {
            kills++;
            ctx.events.emit("test_lib:kill", { total: kills });
        });
        ctx.expose({ kills: () => kills, spawned: () => spawned });
        ctx.log("библиотека запущена");
    },
});
