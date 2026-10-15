import { defineMod } from "../api";

/** Тестовая выдача ресурсов: в главном меню выставляет золото и алмазы (ObscuredInt в Z_Database) и проверяет чтением. */
export default defineMod({
    id: "grant",
    name: "Выдать ресурсы (тест)",
    description: "При входе в главное меню выставляет золото и алмазы (золото 0 — не трогать). Выключи после теста.",
    options: [
        { key: "gold", label: "Золото, млн", default: 50, min: 0, max: 2000, step: 10 },
        { key: "gems", label: "Алмазы", default: 2000, min: 0, max: 100000, step: 1000 },
    ],
    onEnable(ctx) {
        let done = false;
        let calls = 0;
        ctx.after("A_MainMenuInformation", "Update", 0, self => {
            if (done || ++calls % 60 !== 0) return;
            const z = Il2Cpp.domain.assembly("Assembly-CSharp").image.class("Z_Database");
            const oi = Il2Cpp.domain.assembly("ACTk.Runtime").image.class("CodeStage.AntiCheat.ObscuredTypes.ObscuredInt");
            const mk = oi.methods.find(m => m.name === "op_Implicit" && m.parameters[0].type.name === "System.Int32")!;
            const rd = oi.methods.find(m => m.name === "op_Implicit" && m.parameters[0].type.name.endsWith("ObscuredInt"))!;
            const want: [string, number][] = [
                ["Game_Money_Gold", ctx.settings.gold * 1_000_000],
                ["Game_Money_Gem", ctx.settings.gems],
            ];
            for (const [name, v] of want) {
                if (v <= 0) continue;
                const f = z.field(name);
                f.value = mk.invoke(v) as any;
                const back = rd.invoke(f.value as any);
                ctx.log(`${name}: записано ${v}, прочитано ${back}`);
            }
            done = true;
        });
    },
});
