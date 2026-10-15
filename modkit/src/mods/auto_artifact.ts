import { defineMod, type ModContext } from "../api";
import { Table } from "../data";

/**
 * «Артефакт на старте боя»: при входе в бой сразу кладёт в список артефактов героя артефакт с заданным ID
 * (ID — в docs/CATALOG.md) и просит игру пересчитать характеристики. Выбор сундука и обычное получение не затрагиваются.
 */
/** Игра ведёт счётчик артефактов по синергиям (Z_Database.Array_SynergyObtain[ID синергии]) только при штатном получении — выставляем сами. */
function syncSynergies(ctx: ModContext, list: Il2Cpp.Object): void {
    const have = new Set<number>();
    for (let i = 0; i < list.field<number>("_size").value; i++) have.add(Number(list.field<Il2Cpp.Array<number>>("_items").value.get(i)));
    const arr = Il2Cpp.domain.assembly("Assembly-CSharp").image.class("Z_Database").field<Il2Cpp.Array<number>>("Array_SynergyObtain").value;
    const on = Il2Cpp.domain.assembly("Assembly-CSharp").image.class("Z_Database").field<Il2Cpp.Object>("List_ApplySynergyON").value;
    const t = new Table("Synergy");
    const done: string[] = [];
    for (let i = 0; i < t.length; i++) {
        const id = t.get(i, t.idColumn);
        if (typeof id !== "number" || id < 0 || id >= arr.length) continue;
        let n = 0;
        for (let k = 1; k <= 5; k++) if (have.has(Number(t.get(i, `ID_0${k}`)))) n++;
        if (n > Number(arr.get(id))) { arr.set(id, n); done.push(`${id}=${n}/${t.get(i, "개수")}`); }
        // синергия собрана -> флаг включения (его читает пересчёт характеристик)
        const need = Number(t.get(i, "개수"));
        if (need > 0 && n >= need && !on.isNull() && id < on.method<number>("get_Count").invoke() && on.method<number>("get_Item").invoke(id) === 0) {
            on.method("set_Item").invoke(id, 1);
            done.push(`ON[${id}]=1`);
        }
    }
    ctx.log(`[auto_artifact] счётчики синергий: ${done.join(", ") || "без изменений"}`);
}

/** Диагностика: содержимое Z_Database.Dictionary_ApplySynergy[ID артефакта − 41] (словарь синергий артефакта, по нему строятся полоски в окне артефакта). */
function dumpSynergyDicts(ctx: ModContext, ids: number[]): void {
    const list = Il2Cpp.domain.assembly("Assembly-CSharp").image.class("Z_Database").field<Il2Cpp.Object>("Dictionary_ApplySynergy").value;
    if (list.isNull()) { ctx.log("[auto_artifact] Dictionary_ApplySynergy: null"); return; }
    const count = list.method<number>("get_Count").invoke();
    for (const id of ids) {
        const idx = id - 41;
        if (idx < 0 || idx >= count) { ctx.log(`[auto_artifact] Dictionary_ApplySynergy[${idx}] вне списка (${count})`); continue; }
        const d = list.method<Il2Cpp.Object>("get_Item").invoke(idx);
        const entries = d.field<Il2Cpp.Array<any>>("_entries").value.handle.add(0x20);
        const used = d.field<number>("_count").value;
        const out: string[] = [];
        for (let i = 0; i < used; i++) {
            const e = entries.add(i * 16);
            if (e.add(4).readS32() < -1) continue;
            out.push(`${e.add(8).readS32()}:${e.add(12).readS32()}`);
        }
        ctx.log(`[auto_artifact] синергии артефакта ${id}: {${out.join(", ")}}`);
    }
}

export default defineMod({
    id: "auto_artifact",
    name: "Артефакт на старте боя",
    description: "Сразу выдаёт артефакт с выбранным ID в начале боя (ID — см. docs/CATALOG.md).",
    options: [
        { key: "id", label: "ID артефакта (0 — нет)", default: 210, min: 0, max: 9999, step: 1 },
        { key: "id2", label: "ID артефакта 2 (0 — нет)", default: 0, min: 0, max: 9999, step: 1 },
        { key: "id3", label: "ID артефакта 3 (0 — нет)", default: 0, min: 0, max: 9999, step: 1 },
        { key: "id4", label: "ID артефакта 4 (0 — нет)", default: 0, min: 0, max: 9999, step: 1 },
        { key: "id5", label: "ID артефакта 5 (0 — нет)", default: 0, min: 0, max: 9999, step: 1 },
    ],
    onEnable(ctx) {
        let lastMgr: NativePointer | null = null;
        let pending = false;
        let applyArg = 0;
        let stage = 0;
        const zdb = () => Il2Cpp.domain.assembly("Assembly-CSharp").image.class("Z_Database");
        const obtained = () => zdb().field<Il2Cpp.Object>("List_ArtifactObtain").value;
        // игра зовёт AbilityStatApply при старте героя: запоминаем её аргумент и, если артефактов ещё нет, просим выдать наш
        const asm = Il2Cpp.domain.assembly("Assembly-CSharp").image;
        const stat = asm.class("V_Ability_Manager").method("AbilityStatApply", 1);
        const h1 = Interceptor.attach(stat.virtualAddress, {
            onEnter(args) { lastMgr = args[0]; applyArg = args[1].toInt32(); },
            onLeave() { if (stage === 0) pending = true; },
        });
        // новый бой: список опять пуст -> можно выдавать снова
        const h3 = Interceptor.attach(asm.class("C_Battle_Manager").method("Update", 0).virtualAddress, {
            onEnter() {
                try {
                    const ids = ["id", "id2", "id3", "id4", "id5"].map(k => Math.round(ctx.settings[k] ?? 0)).filter(v => v > 0);
                    const size = () => obtained().field<number>("_size").value;
                    if (stage === 1 && size() < ids.length) stage = 0;           // новый бой: список снова короткий (класс может выдать свой стартовый артефакт)
                    if (!pending || !lastMgr) return;
                    pending = false;
                    if (ids.length === 0) return;                                // все ID = 0: ничего не выдаём
                    const list = obtained();
                    for (const id of ids) list.method("Add").invoke(id);
                    stage = 1;
                    syncSynergies(ctx, list);
                    dumpSynergyDicts(ctx, ids);
                    ctx.log(`[auto_artifact] выданы артефакты ${ids.join(", ")}`);
                } catch (e) { ctx.log("[auto_artifact] " + e); }
            },
        });
        ctx.onCleanup(() => { h1.detach(); h3.detach(); });
    },
});
