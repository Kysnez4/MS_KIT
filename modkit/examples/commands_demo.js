// ДЕМО всех команд правки данных (внешний мод). Каждая команда откатывается при выключении мода.
ModKit.define({
    id: "ext_commands_demo",
    defaultEnabled: true,
    name: "Демо команд (артефакты/комбинации/синергии)",
    description: "Проверка addArtifact/editArtifact/removeArtifact/addCombo/synergy*.",
    onEnable(ctx) {
        ctx.data.whenReady(() => {
            const d = ctx.data;
            ctx.log("шаг 1: addArtifact");
            const id = d.addArtifact({ name: "Тест-клинок", effects: [[4, 40]], rarity: "S", icon: 41 });
            ctx.log("шаг 2: editArtifact");
            d.editArtifact(id, { effects: [[4, 60], [30, 10]], lines: ["Атака +60%", "Скорость +10%"], rarity: "P" });
            ctx.log("шаг 3: removeArtifact");
            d.removeArtifact(43);                       // «Вирус» больше не выпадает
            ctx.log("шаг 4: editArtifact");
            d.editArtifact(210, { effects: [[170, 50], [287, 50]] });   // Сюрикен
            ctx.log("шаг 5: editCombo");
            d.editCombo(33, { effects: [[158, 33], [851, 2000]], lines: ["Призывает 〈3〉 дополнительных [фантома].", "Число пуль [1].", "Урон пуль х20"] });
            ctx.log("шаг 6: addCombo");
            const c = d.addCombo({ name: "Тест-комбо", cloneFrom: 1, effects: [[911, 8], [914, 5]] });
            ctx.log("шаг 7: addSynergy");
            const s = d.addSynergy({ name: "Тест-синергия", effects: [[4, 25]], members: [41, 42] });
            ctx.log("шаг 8: synergyAddArtifact");
            d.synergyAddArtifact(s, id);
            ctx.log("шаг 9: synergyRemoveArtifact");
            d.synergyRemoveArtifact(s, 41);
            // пакет: несколько артефактов одним запросом (иконки и синергии готовятся один раз)
            const ids = d.addArtifacts([
                { name: "Пакет 1", effects: [[4, 10]], rarity: "C", icon: 41 },
                { name: "Пакет 2", effects: [[4, 20]], rarity: "B", icon: 42 },
                { name: "Пакет 3", effects: [[4, 30]], rarity: "A", icon: 43 },
            ]);
            ctx.log("пакет артефактов: " + ids.join(","));
            ctx.log(`демо: артефакт ${id}, комбо ${c}, синергия ${s}`);
        });
    },
});
