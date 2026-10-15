import { defineMod } from "../api";

/** Отладка: пишет в лог ID предметов, которые игра создаёт (CreateItem_Position / CreateItem_Character) — чтобы найти ID сундука. */
export default defineMod({
    id: "chestprobe",
    name: "Зонд предметов (отладка)",
    description: "Пишет в лог ID и число создаваемых предметов; сундук ищем по появлению в бою.",
    options: [],
    onEnable(ctx) {
        const mgr = Il2Cpp.domain.assembly("Assembly-CSharp").image.class("C_Battle_Manager");
        const hooks: InvocationListener[] = [];
        const seen = new Map<string, number>();
        const watch = (name: string, params: number) => {
            const m = mgr.tryMethod(name, params);
            if (!m) { ctx.log(`[chestprobe] нет метода ${name}`); return; }
            hooks.push(Interceptor.attach(m.virtualAddress, {
                onEnter(args) {
                    const key = `${name} id=${args[1].toInt32()} n=${args[2].toInt32()}`;
                    const c = (seen.get(key) ?? 0) + 1;
                    seen.set(key, c);
                    if (c <= 3) ctx.log(`[chestprobe] ${key} (раз ${c})`);
                },
            }));
        };
        watch("CreateItem_Position", 4);
        watch("CreateItem_Character", 2);
        ctx.onCleanup(() => hooks.forEach(h => h.detach()));
    },
});
