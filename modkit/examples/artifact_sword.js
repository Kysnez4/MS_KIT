// ПРИМЕР внешнего мода: новый артефакт без пересборки. Положите файл в
//   Android/data/com.vkslrzm.Zombie/files/modkit/mods/   (или Download/ModKit)
// и нажмите «+» в окне «Моды». Эффекты — пары [ID эффекта, значение], список ID — docs/EFFECTS.md.
ModKit.define({
    id: "ext_artifact_sword",
    name: "Артефакт «Клинок бури»",
    description: "Атака +40%, скорость +20%. Выпадает как обычный артефакт класса A.",
    onEnable(ctx) {
        ctx.data.whenReady(() => {
            ctx.data.addArtifact({
                name: "Клинок бури",
                effects: [[4, 40], [30, 20]],
                lines: ["Атака +40%", "Скорость передвижения +20%"],
                // cloneFrom: 42,        // с какого артефакта брать иконку и редкость (по умолчанию 42, класс A)
                // weight: 5,            // вес выпадения (число копий в пуле), у обычных артефактов 5
                // guaranteed: true,     // тест: выпадает почти всегда
            });
        });
    },
});
