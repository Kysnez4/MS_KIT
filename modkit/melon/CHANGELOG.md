# ModKit (MelonLoader): журнал изменений

## Мод magic_balance 1.3 (2026-10-22)
- Правки проверяются заново при каждом входе в главное меню (`OnMainMenu`): если игра перечитала таблицы, отличия записываются снова. `scale`/`add` и новые комбинации повторно не применяются.
- Диагностика: при открытии карточки комбинации в энциклопедии в журнал пишутся её эффекты и текст.

## Мод magic_balance 1.1 (2026-10-22)
- Описания атрибутов и комбинаций приведены к новым значениям эффектов: в `balance.json` у 16 атрибутов и 50 комбинаций поля `lines`/`colors`, мод пишет их в `효과 설명줄01..06` и `설명줄NN 색깔` (атрибуты — `Ability`, комбинации — `ComboChanges.Lines` + цвета в `MagicCom`). Собрано, на устройстве не проверено.

## Мод magic_balance 1.0 (2026-10-22)
- Новый мод `magic_balance`: применяет `balance.json` сайта `modkit/tools/balance` (встроен в DLL) — эффекты уровней заклинаний, эффекты атрибутов (строки `Ability` по полю `game`), эффекты комбинаций и новые комбинации. Пишет только отличия от таблиц игры, всё откатывается при выключении. Собрано, на устройстве не проверено.

## 2.0 — ядро и библиотеки (2026-10-20)

Ядро ModKit разрезано по образцу Factorio: ядро осталось «мод-менеджером», знание об устройстве игры переехало в библиотеки-моды.

### Что изменилось для авторов модов
- Из `ModContext` убраны `Game`, `Enemies`, `Combat`, `Spells`, `Hero`, `Companions`, `Triggers`, `Research`, `Zones`, `Art`, `Resource(...)`, `DefineEffect`, `SetGlobalEffects`, `UseBuiltinEffects`, `OnHeroPass`, `RecalcStats`.
- Вместо них: `ctx.Use<T>("id библиотеки")`, например `ctx.Use<ICombat>("modkit_combat")`. Библиотека привязывает хуки, таймеры и правки к вызывающему моду, всё откатывается при его выключении. В `mod.json` — `depends`, в `csproj` — ссылка на DLL библиотеки.
- `ModKitData.Data.For(ctx)` заменён на `ctx.Use<IData>("modkit_data")` и отдельные библиотеки рецептов.
- Удалены `ClassDef`, `AddClass`, `SelectClass` (ломали меню классов). Свои классы — `CustomClass` из `class_menu`.
- `Table` теперь в `modkit_data`.

### Ядро
- Новое: `BattleState` (бой идёт, пауза, герой), `ILibrary<T>`, `ctx.Use<T>`, `ctx.CleanupList()`.
- Загрузчик грузит zip в порядке зависимостей (библиотеки первыми), библиотеки могут ссылаться друг на друга.

### Библиотеки (16)
`modkit_data` (сырой доступ к таблицам), `modkit_artifacts`, `modkit_combos`, `modkit_synergies`, `modkit_spells_data`, `modkit_effects`, `modkit_spells`, `modkit_combat` (`IEnemies` + `ICombat`), `modkit_companions`, `modkit_zones`, `modkit_hero`, `modkit_triggers`, `modkit_resources`, `modkit_research`, `modkit_game`, `modkit_art`. Граф зависимостей — в `modkit/docs/API.md`, раздел 1а.

### Моды и примеры
Переведены на новый API: `achievements`, `active_pack`, `api_probe`, `area_mechanics`, `art_swap`, `artifact_pack`, `auto_artifact`, `autotest`, `class_menu`, `class_practice`, `combat_probe`, `kill_streak`, `projectile_cap`, `spell_tuner`; примеры `active_demo`, `cast_probe`, `class_pack`, `data_demo`, `effects_demo`, `spell_demo`. Пример `class_demo` удалён.

### Проверка
Сборка ядра, библиотек и модов без ошибок; загрузка всех zip в хостовом харнессе; на телефоне: загрузка 39 модов без ошибок, автотест (бой 120 с, выбор артефактов, счётчики синергий) — PASS, `combat_probe`/`api_probe`/`kill_streak` — враги, урон, зоны, союзники, триггеры, эффекты работают. Не прогонялись под нагрузкой: `artifact_pack`, `spell_tuner`, `class_practice`.

### Как собрать
`python modkit/melon/pack_mod.py <имя> ...` — в порядке зависимостей (сначала библиотеки). Ядро: `dotnet build -c Release` в `modkit/melon/ModKit`, заливка `bash modkit/tools/modkit_deploy.sh`.

### Благодарности и ИИ
Загрузчик: LemonLoader / MelonLoader, Il2CppInterop, Harmony. Рефакторинг и перенос выполнены с помощью Claude Code (Claude Sonnet 5.5); код проверен сборкой и прогоном на устройстве.

## Zombix на MelonLoader (2026-10-20)
Сборка игры с MelonLoader (LemonLoader) как отдельное приложение `com.vkslrzm.Zombix` (ставится рядом с оригиналом, свои сохранения). `PKG=com.vkslrzm.Zombix bash modkit/tools/melon_build.sh` (`BUILD_ONLY=1` — без установки на телефон). Проверено на устройстве: первый запуск генерирует сборки IL2CPP (несколько минут), затем ModKit загружает 28 zip без ошибок. Ядро и моды лежат отдельным архивом `Zombix-melon-ModKit.zip` (папки `Mods` и `UserData` — в `/sdcard/MelonLoader/com.vkslrzm.Zombix/`).

## Документация для авторов модов (2026-10-20)
Переписана с нуля: `modkit/docs/README.md` (оглавление), `GETTING_STARTED`, `MOD_FORMAT`, `API`, `LIBRARIES`, `COOKBOOK` (код собирается: `modkit/melon/examples/cookbook`), `DEBUGGING`. Руководство по Frida-версии переехало в `FRIDA.md`.
