import { defineMod } from "../api";

export default defineMod({
    id: "move_speed",
    name: "Скорость движения",
    description: "Умножает скорость движения игрока.",
    options: [{ key: "mult", label: "Множитель", default: 1.5, min: 1, max: 4, step: 0.25 }],
    onEnable(ctx) {
        const ally = ctx.cls("C_Battle_AllyUnit");
        const originals = new Map<string, number>();
        const touched: Il2Cpp.Object[] = [];
        ctx.every(1000, () => {
            for (const unit of Il2Cpp.gc.choose(ally)) {
                const f = unit.field<number>("UnitBase_MoveSpeed");
                const key = unit.handle.toString();
                if (!originals.has(key)) {
                    originals.set(key, f.value);
                    touched.push(unit);
                }
                f.value = originals.get(key)! * ctx.settings.mult;
            }
        });
        ctx.onCleanup(() => Il2Cpp.perform(() => {
            for (const unit of touched) {
                try { unit.field<number>("UnitBase_MoveSpeed").value = originals.get(unit.handle.toString())!; } catch (_) { /* объект уже уничтожен */ }
            }
        }));
    },
});
