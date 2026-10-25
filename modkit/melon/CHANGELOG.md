# ModKit (MelonLoader): журнал изменений

## Мод achievements 1.1 (2026-10-25)
- Пересобран под ядро 2.0 и библиотеки (`modkit_spells`, `modkit_effects`): старый zip 1.0 был собран под ядро с `ISpells` и падал с `TypeLoadException`. Проверено на телефоне: включается, подтягивает `modkit_spells`, прогресс загружается.

## Ядро: переводы и журнал; modkit_effects 1.1; magic_balance 2.0 (2026-10-25)
- Ядро: переводы модов — `lang/<язык>.json` в zip, `ctx.Lang` (ko, en, ja, zh_cn, zh_tw, es, de, pt, ru по `Game_SystemLanguage`), `ctx.T(ключ, запасной)` (язык игры → en → ru); название и описание в окне «Моды» из ключей `name`/`description`. `modkit.log` начинается заново при каждом запуске, прошлый — `modkit.prev.log`. Проверено на телефоне: журнал, загрузка модов.
- `modkit_effects` 1.1: `Recalc()` во время паузы не снимает её (`AbilityStatApply` в конце всегда ставит `Time.timeScale`) — игра больше не идёт под окном выбора обелиска.
- `magic_balance` 2.0: числа в описаниях комбинаций обновляются по изменённым эффектам (44 из 44 на русской таблице); тексты `lines`/`desc`/`name` в balance.json по языкам (`{"ru": …, "en": …}`); `lang/ru.json`, `lang/en.json`. Перевод в игре не проверен.

## Мод legend_guard 1.0 (2026-10-25)
- Найден штраф игры за 15+ легендарных артефактов (`C_Battle_AllyUnit.StartInformation_B`): здоровье врагов +1 000 000% и при 21+ артефактах удаление трёх самых старых (`RemoveAt(0)` ×3) — из-за него артефакты пропадали и в обычной игре (docs/DIFFICULTY.md).
- `legend_guard` (включён по умолчанию) один раз меняет порог `cmp wN, #15` на 4095. Адрес не зашит: начало функции берётся у IL2CPP, инструкция ищется по шаблону; при выключении мода возвращается. Проверено на телефоне: патч ставится; в бою с 15+ легендарными не проверено.
- `modkit/tools/ghidra/decompile_all.sh` + `DecompileAll.java`: параллельная декомпиляция всех функций игры (4411 из 4412) в `modkit/decomp/<Класс>/` (в git не добавляется).

## Мод magic_balance 1.9 (2026-10-25)
- ДНК: прибавка максимального HP за новый обелиск добавляется и к текущему HP (как `Unit_HpMaxUp` в игре); раньше рос только максимум. На устройстве не проверено.

## Моды magic_balance 1.8, auto_artifact 2.3, enemy_unlock 1.5, chest_guard 1.0 (2026-10-25)
- `magic_balance`: руна Гильгамеша — игра иногда пересчитывает порог от подменённого значения (−9994999), мод теперь берёт последний нормальный порог (2500), обычная руна жадности при Гильгамеше не срабатывает. Логи взрывов боссов убраны.
- `auto_artifact`: хук `C_Battle_Manager.Update` работает раз в 10 кадров, чистка сундуков раз в ~2 с (каждый кадр давал подлагивания до 50 мс).
- `enemy_unlock`: врагов в журнал не пишет (толпы Слуг давали до 128 строк/с, запись на /sdcard грузила телефон); поле `logIds` убрано.
- `chest_guard` (новый): сундук артефактов падал, когда в пуле меньше 3–4 несобранных артефактов (`Create_Artifact` берёт элементы без проверки длины). Мод дополняет пул собранными артефактами-заглушками, после открытия прячет их слоты и возвращает списки как были. На устройстве не проверен.

## Мод enemy_unlock 1.3 (2026-10-24)
- Все значения — в `UserData/ModKit/enemy_unlock.json` (создаётся из `config/enemy_unlock.json` архива при первом включении, перечитывается при включении мода): `colliderOn`, `removeAbilities`, `keepAbilities` (ID → оставить), `debuffResistance` (ID → сопротивление), `logIds`. Значения по умолчанию как в 1.2. На устройстве не проверено.

## Мод enemy_unlock 1.2 (2026-10-24)
- После появления врага (`Start_Unit`): всем врагам включается коллайдер — по Слугам (40) и боссам проходят взрывы и урон артефактов (они бьют через `OnCollisionEnter`). Особенность 10 убирается у всех, особенность 11 — у всех, кроме боссов 41–46 (иначе их убивает мгновенное уничтожение). Слугам сопротивление дебаффам 100 → 75, коэффициенты стана/заморозки пересчитываются по формуле `Start_Unit`. На устройстве: мод грузится, коллайдеры обычным врагам включаются; Слуги и боссы (12–15 мин боя) не проверены.

## Мод custom_font 1.0 (2026-10-23)
- Замена всех шрифтов игры на `font.ttf` из архива без перезапуска: `pack_mod.py` собирает из него `font.bundle` по шаблону `font_template.bundle` (`font_bundle.py`, шаблон — `tools/unity_font_template/BuildFontTemplate.cs`). Мод сам грузит бандл и его сцену, берёт `Font` у `mk_font` и подменяет `Z_Database.Font_Apply_*`, `PFont_*` и шрифт всех `UI.Text` (проход раз в 0.5 с); при выключении возвращает исходные. Ядро не менялось. На телефоне (Zombix): бандл загрузился, 12 текстов подменено, автотест PASS; отрисовку глазами не проверял.

## Мод magic_balance 1.6 (2026-10-23)
- Изменения `artifact_rework` перенесены в `magic_balance`: числа и описания (57, 111, 118, 197, синергия 33) — в balance.json, механики (ДНК, взрыв боссов, проклятие Экскалибура, руна неуязвимости Гильгамеша) — `Mechanics.cs`. Новая зависимость `modkit_effects`. `artifact_rework` больше не нужен. Таблицы на телефоне применились, в бою не проверено.

## Мод magic_balance 1.5, artifact_rework 1.1 (2026-10-23)
- `magic_balance`: применяются разделы `artifacts` и `synergies` из balance.json (полный список эффектов строки Ability/Synergy, `lines`/`colors`). Тексты описаний сгенерированы по сравнению с таблицами (`docs/balance_diff.json`). Созидание (57) оставлено `artifact_rework`. Проверено на телефоне: 21 артефакт и 24 синергии записаны.
- `artifact_rework`: у Экскалибура проклятие отдельной красной строкой; у Созидания убран повтор «Урон всех заклинаний»; диагностика рун в журнале (`руна: …`).

## Мод artifact_rework 1.0 (2026-10-23)
- ДНК (118): эффект 52 (8%·0.85^i за `AreaChangeNum`) убран, мод даёт +8% атаки и макс. HP за каждый вызов `Start_AreaChange` в бою — обычный (`ArtifactType` 9/10) и сломанный (11) обелиск.
- Железная дева (101) / Графиня (211): боссы (особенность врага 11) пропускают взрыв при смерти в `Play_Unit_Death`; мод на `enemyDeath` вызывает `Unit_DetonateDead_Elite` (Графиня) или `Unit_DetonateDead` с броском Девы.
- Созидание (57): 123 = 2. Топор палача (111): 15 = 150 и 732 = 15 (Мясник). Экскалибур (197): 126 = 300 и атака героя −50%, пока не собран Авалон (197, 109, 73, 181).
- `autotest.py`: пакет задаётся переменной `PKG` (по умолчанию `com.vkslrzm.Zombim`).

## Мод artifact_dump 1.0, сайт баланса: артефакты и синергии (2026-10-23)
- Новый мод `artifact_dump`: один раз при запуске пишет все артефакты (`Ability`, `유형` = `아티팩트`) и строки `Synergy` со всеми колонками в `Download/ModKit/artifacts_dump.json`. Собрано, на устройстве не проверено.
- Сайт `tools/balance`: вкладки «Артефакты» и «Синергии» — имя, редкость, эффекты (до 9 / до 3), состав синергии, сравнение с базой, сумма эффектов синергии вместе с артефактами. База — из `CATALOG.md` (`gen_data.py`) или из загруженного `artifacts_dump.json`. Экспорт C#: `IArtifacts.Edit`, для синергий — `IData.Edit("Synergy", …)` и `AddArtifact`/`RemoveArtifact`. `magic_balance` новые поля `artifacts`/`synergies` пока не применяет.

## Спрайты из бандла (2026-10-23)
- `UiKit.LoadSpriteBundle`: спрайты мода из AssetBundle со сценой. `pack_mod.py` превращает папку `sprites/` с PNG в `sprites.bundle` (`sprite_bundle.py`, UnityPy): шаблон `melon/sprite_template.bundle` на 256 слотов собран один раз в Unity 2022.3.62f3 (`tools/unity_sprite_template/BuildTemplate.cs`), в слоты подставляются картинки, размеры и имена. В игре бандл грузится `AssetBundle.LoadFromFileAsync`, сцена догружается поверх, спрайты берутся у объектов `mk_<имя>`. Проверено на телефоне (Zombix, `examples/sprite_probe`): 2 спрайта, размеры 64×64 и 64×32 совпали с PNG; на экран спрайты не выводились.

## Sprite.Create (2026-10-23)
- В родной `libunity.so` игры вырезан движок (Strip Engine Code): 1048 icall из 7557, `Sprite.Create` нет ни в C#, ни в нативной части. `melon_build.sh` получил `UNITY_LIB=full`/`dev` — полная Release/Development-библиотека 2022.3.62f3 из Android Build Support (`Variations/il2cpp/*/Libs/arm64-v8a`), в ней все icall.
- `UiKit.CreateSprite` / `UiKit.CanCreateSprite`: спрайт из PNG/JPG мода через icall `Sprite::CreateSprite_Injected`. Проверено на телефоне: с полной `libunity` игра падает (и без MelonLoader) — движок вызывает вырезанные из игры C#-методы `NativeInputSystem`. С родной `libunity` `CreateSprite` возвращает `IntPtr.Zero`.

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
