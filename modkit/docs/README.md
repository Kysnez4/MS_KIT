# Документация для авторов модов (ModKit, C#)

Здесь всё, чтобы написать мод для *Magic Survival* на **Zombix (MelonLoader)** с нуля и поддерживать его. Моды — обычные C#-библиотеки (`.dll`) в zip-архиве; их запускает ядро ModKit внутри игры. Читайте по порядку:

| # | Документ | Зачем |
|---|---|---|
| 1 | [GETTING_STARTED.md](GETTING_STARTED.md) | Подготовка рабочего места, первый мод за 15 минут: сборка, упаковка, установка, журнал |
| 2 | [MOD_FORMAT.md](MOD_FORMAT.md) | `mod.json`, `ModDef`, настройки, зависимости и версии, библиотеки, жизненный цикл, ошибки |
| 3 | [API.md](API.md) | Справочник ядра: `ModContext`, хуки, события, таймеры, хранилище, окна и экран |
| 4 | [LIBRARIES.md](LIBRARIES.md) | 16 библиотек `modkit_*`: бой, заклинания, эффекты, данные игры, артефакты и др. |
| 5 | [COOKBOOK.md](COOKBOOK.md) | Готовые рецепты (код собирается: `modkit/melon/examples/cookbook`) |
| 6 | [DEBUGGING.md](DEBUGGING.md) | Журналы, автотест на телефоне, вылеты, типичные ошибки |

## Справочники по данным игры
- [CATALOG.md](CATALOG.md) — артефакты, комбинации, синергии игры; [TABLES.md](TABLES.md) — колонки таблиц `Z_Database.Dictionary_*`.
- [EFFECTS.md](EFFECTS.md) — ID эффектов, которые можно писать в артефакты и комбинации; [CONDITIONAL.md](CONDITIONAL.md) — условные эффекты.
- [CLASSES.md](CLASSES.md), [CLASS_MENU.md](CLASS_MENU.md), [PRACTICE.md](PRACTICE.md), [AREAS.md](AREAS.md), [DIFFICULTY.md](DIFFICULTY.md), [ULTIMATES.md](ULTIMATES.md), [COMPANIONS.md](COMPANIONS.md), [ACTIVE_SPELLS.md](ACTIVE_SPELLS.md) — разборы механик игры.

## Рабочие заметки (для любопытных)
[MELONLOADER.md](MELONLOADER.md) — дневник портирования и грабли; [DECOMP.md](DECOMP.md) — как читать код игры (Ghidra); [API_WISHLIST.md](API_WISHLIST.md), [MODS_PHASE_A.md](MODS_PHASE_A.md), [MODS_PHASE_BC.md](MODS_PHASE_BC.md) — планы и разборы.
Старая версия на Frida/TypeScript — [FRIDA.md](FRIDA.md).

## Условные обозначения
✅ проверено на устройстве, ◻ собрано, но в игре не проверялось. Игра: Magic Survival 612 (Unity 2022.3, IL2CPP, arm64).
