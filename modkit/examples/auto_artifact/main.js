"use strict";
const { openNumberForm } = ModKit.ui;
const { Table } = ModKit.data;

const SLOTS = 8;
const idKey = (i) => i === 0 ? "id" : `id${i + 1}`;
const cntKey = (i) => `cnt${i + 1}`;

/**
 * «Артефакт на старте боя»: при входе в бой сразу кладёт в список артефактов героя артефакт с заданным ID
 * (ID — в docs/CATALOG.md) и просит игру пересчитать характеристики. Выбор сундука и обычное получение не затрагиваются.
 */
/** Игра ведёт счётчик артефактов по синергиям (Z_Database.Array_SynergyObtain[ID синергии]) только при штатном получении — выставляем сами. */
function syncSynergies(ctx, list) {
    const have = new Set();
    for (let i = 0; i < list.field("_size").value; i++) have.add(Number(list.field("_items").value.get(i)));
    const arr = Il2Cpp.domain.assembly("Assembly-CSharp").image.class("Z_Database").field("Array_SynergyObtain").value;
    const on = Il2Cpp.domain.assembly("Assembly-CSharp").image.class("Z_Database").field("List_ApplySynergyON").value;
    const t = new Table("Synergy");
    const done = [];
    for (let i = 0; i < t.length; i++) {
        const id = t.get(i, t.idColumn);
        if (typeof id !== "number" || id < 0 || id >= arr.length) continue;
        let n = 0;
        for (let k = 1; k <= 5; k++) if (have.has(Number(t.get(i, `ID_0${k}`)))) n++;
        if (n > Number(arr.get(id))) { arr.set(id, n); done.push(`${id}=${n}/${t.get(i, "개수")}`); }
        // синергия собрана -> флаг включения (его читает пересчёт характеристик)
        const need = Number(t.get(i, "개수"));
        if (need > 0 && n >= need && !on.isNull() && id < on.method("get_Count").invoke() && on.method("get_Item").invoke(id) === 0) {
            on.method("set_Item").invoke(id, 1);
            done.push(`ON[${id}]=1`);
        }
    }
    ctx.log(`[auto_artifact] счётчики синергий: ${done.join(", ") || "без изменений"}`);
}

/** Диагностика: содержимое Z_Database.Dictionary_ApplySynergy[ID артефакта − 41] (словарь синергий артефакта, по нему строятся полоски в окне артефакта). */
function dumpSynergyDicts(ctx, ids) {
    const list = Il2Cpp.domain.assembly("Assembly-CSharp").image.class("Z_Database").field("Dictionary_ApplySynergy").value;
    if (list.isNull()) { ctx.log("[auto_artifact] Dictionary_ApplySynergy: null"); return; }
    const count = list.method("get_Count").invoke();
    for (const id of ids) {
        const idx = id - 41;
        if (idx < 0 || idx >= count) { ctx.log(`[auto_artifact] Dictionary_ApplySynergy[${idx}] вне списка (${count})`); continue; }
        const d = list.method("get_Item").invoke(idx);
        const entries = d.field("_entries").value.handle.add(0x20);
        const used = d.field("_count").value;
        const out = [];
        for (let i = 0; i < used; i++) {
            const e = entries.add(i * 16);
            if (e.add(4).readS32() < -1) continue;
            out.push(`${e.add(8).readS32()}:${e.add(12).readS32()}`);
        }
        ctx.log(`[auto_artifact] синергии артефакта ${id}: {${out.join(", ")}}`);
    }
}

ModKit.define({
    id: "auto_artifact",
    name: "Артефакт на старте боя",
    description: "Сразу выдаёт выбранные артефакты (ID и количество — в «Настройки») в начале боя.",
    options: [
        ...Array.from({ length: SLOTS }, (_, i) => ({ key: idKey(i), label: `ID артефакта ${i + 1}`, default: i === 0 ? 210 : 0, min: 0, max: 9999, step: 1, hidden: true })),
        ...Array.from({ length: SLOTS }, (_, i) => ({ key: cntKey(i), label: `Количество ${i + 1}`, default: 1, min: 0, max: 99, step: 1, hidden: true })),
    ],
    openSettings(ctx) {
        openNumberForm({
            name: "ModKitAutoArtifactSettings",
            title: "Артефакт на старте боя",
            subtitle: "ID артефакта (0 — нет, см. docs/CATALOG.md) и сколько копий выдать. Применится в следующем бою.",
            columns: ["ID", "Кол-во"],
            rows: Array.from({ length: SLOTS }, (_, i) => ({
                label: `Слот ${i + 1}`,
                fields: [{ key: idKey(i), min: 0, max: 9999 }, { key: cntKey(i), min: 0, max: 99 }],
            })),
            get: key => ctx.settings[key] ?? 0,
            set: (key, v) => ctx.setSetting(key, v),
        });
    },
    onEnable(ctx) {
        let lastMgr = null;
        let pending = false;
        let warm = 0;
        let capped = 0;
        let applyArg = 0;
        let stage = 0;
        const zdb = () => Il2Cpp.domain.assembly("Assembly-CSharp").image.class("Z_Database");
        const obtained = () => zdb().field("List_ArtifactObtain").value;
        let tick = 0;
        const countOf = (id) => {
            const l = obtained();
            const items = l.field("_items").value;
            let n = 0;
            for (let i = 0; i < l.field("_size").value; i++) if (Number(items.get(i)) === id) n++;
            return n;
        };
        const wantOf = (ids) => { const m = new Map(); for (const id of ids) m.set(id, (m.get(id) ?? 0) + 1); return m; };
        /** Копий одного ID в настройках — столько и нужно (несколько одинаковых ID = несколько копий). Лишнее убираем только вскоре после выдачи. */
        const trimExtra = (ids) => {
            const l = obtained();
            for (const [id, want] of wantOf(ids)) {
                let extra = countOf(id) - want;
                while (extra-- > 0) {
                    const items = l.field("_items").value;
                    for (let i = l.field("_size").value - 1; i >= 0; i--) {
                        if (Number(items.get(i)) === id) { l.method("RemoveAt").invoke(i); break; }
                    }
                    ctx.log(`[auto_artifact] убрана лишняя копия артефакта ${id}`);
                }
            }
        };
        let trimUntil = 0;
        // игра зовёт AbilityStatApply при старте героя: запоминаем её аргумент и, если артефактов ещё нет, просим выдать наш
        const asm = Il2Cpp.domain.assembly("Assembly-CSharp").image;
        const stat = asm.class("V_Ability_Manager").method("AbilityStatApply", 1);
        const h1 = Interceptor.attach(stat.virtualAddress, {
            onEnter(args) { lastMgr = args[0]; applyArg = args[1].toInt32(); },
            onLeave() { if (stage === 0) pending = true; if (capped++ < 5) ctx.log(`[auto_artifact] AbilityStatApply (stage=${stage})`); },
        });
        // новый бой: список опять пуст -> можно выдавать снова
        const h3 = Interceptor.attach(asm.class("C_Battle_Manager").method("Update", 0).virtualAddress, {
            onEnter() {
                try {
                    const ids = [];
                    for (let i = 0; i < SLOTS; i++) {
                        const id = Math.round(ctx.settings[idKey(i)] ?? 0), n = Math.round(ctx.settings[cntKey(i)] ?? 1);
                        if (id > 0) for (let k = 0; k < n; k++) ids.push(id);
                    }
                    const size = () => obtained().field("_size").value;
                    if (stage === 1 && size() < ids.length) { stage = 0; warm = 0; }   // новый бой: список снова короткий (класс может выдать свой стартовый артефакт)
                    // AbilityStatApply может быть вызван до включения мода (бой уже загружен или продолжается) — тогда ждём ~2 с боя и выдаём сами
                    if (stage === 0 && !pending && ++warm === 120) { pending = true; ctx.log("[auto_artifact] AbilityStatApply не было — выдаю по таймеру"); }
                    // при возврате в уже идущий бой игра сама восстанавливает список (с нашими артефактами) — убираем лишние копии
                    if (stage === 1 && Date.now() < trimUntil && ++tick % 30 === 0) trimExtra(ids);
                    if (!pending) return;
                    pending = false;
                    if (ids.length === 0) return;                                // все ID = 0: ничего не выдаём
                    const list = obtained();
                    const given = [];
                    for (const [id, want] of wantOf(ids)) {
                        const have = countOf(id);                                // уже есть (восстановлен из сохранения боя) — докладываем только недостающее
                        for (let k = have; k < want; k++) { list.method("Add").invoke(id); given.push(id); }
                    }
                    stage = 1;
                    trimUntil = Date.now() + 15000;
                    if (!given.length) { ctx.log("[auto_artifact] артефакты уже в списке (возврат в бой) — повторно не выдаю"); return; }
                    syncSynergies(ctx, list);
                    dumpSynergyDicts(ctx, given);
                    ctx.log(`[auto_artifact] выданы артефакты ${given.join(", ")}`);
                } catch (e) { ctx.log("[auto_artifact] " + e); }
            },
        });
        ctx.onCleanup(() => { h1.detach(); h3.detach(); });
    },
});
