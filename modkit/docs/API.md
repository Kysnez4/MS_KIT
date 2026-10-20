# ModKit API (C#, MelonLoader): справочник и инструкции

Единая точка для авторов модов. Ядро: `modkit/melon/ModKit` (`ModKit.dll`), моды: `modkit/melon/mods/*` (zip). Всё ниже собрано из кода; пометка **[не проверено]** — на устройстве не запускалось.

## 1. Как устроен мод

```csharp
public class MyMod : IKitMod {
    public ModDef Define() => new ModDef {
        Id = "my_mod", Name = "…", Version = "1.0",              // версию поднимать при каждой правке: загрузчик обновляет по ней
        Options = { new ModOption { Key = "n", Label = "…", Default = 1, Min = 0, Max = 9 } },
        OnEnable = ctx => { /* хуки, события, таймеры; всё снимается при выключении */ },
    };
}
```
Сборка и заливка: `dotnet build -c Release` в папке мода → `python modkit/melon/pack_mod.py <id>` → zip в `/sdcard/Download/ModKit/`. Ядро (`ModKit.dll`) заливает `bash modkit/tools/modkit_deploy.sh`.
Ссылку на `Il2CppACTk.Runtime` моду добавлять не нужно, пока он не читает `ObscuredInt/Float` сам (для этого есть `IGame` из `modkit_game`).

## 1а. Ядро и библиотеки (Factorio-style)

Ядро (`ModKit.dll`) — только «мод-менеджер»: контракт мода, `config.json`, зависимости, хуки, события, реестры, zip-загрузка, окно «Моды», UI-примитивы, `ctx.Timer/Store/Tags/Screen/Toast`, `BattleState` (бой идёт / пауза). Всё, что знает устройство игры, живёт в библиотеках-модах (`library: true`): они включаются сами, когда включён мод, который от них зависит.

```csharp
// mod.json: "depends": { "modkit_combat": ">=1.0", "modkit_spells": ">=1.0" }
var enemies = ctx.Use<IEnemies>("modkit_combat");     // один объект на мод, пока он включён
var spells  = ctx.Use<ISpells>("modkit_spells");
```
`ctx.Use<T>(id)` = `ctx.Mod<ILibrary<T>>(id).For(ctx)`: библиотека привязывает хуки, таймеры и правки к вызывающему моду, поэтому всё откатывается при выключении именно этого мода. В `csproj` нужна ссылка на DLL библиотеки (`Mod.props` даёт ядро; пример — `mods/combat_probe`).

| Библиотека | Что даёт (`ctx.Use<T>`) | Зависит от |
|---|---|---|
| `modkit_data` | `IData`: `WhenReady`, `Table`, `Edit`, `Add`, `AddToList`; `Table`, `Effect`, `Commands`, `Sprites` | — |
| `modkit_artifacts` | `IArtifacts`: `Add/AddMany/Edit/Remove/Restore`, `ArtifactDef` | data, art |
| `modkit_combos` | `ICombos`: `Add/AddMany/Edit`, `ComboDef` | data |
| `modkit_synergies` | `ISynergies`: `Add/AddMany/Remove/AddArtifact/RemoveArtifact`, `SynergyDef` | data |
| `modkit_spells_data` | `ISpellsData`: `Get/Edit/Scale`, `SpellInfo`, `SpellChanges` | data |
| `modkit_effects` | `IEffects`: `Define`, `SetGlobal`, `UseBuiltin`, `OnHeroPass`, `Recalc`; `EffectDef`, `IHeroView` | data |
| `modkit_spells` | `ISpells` (уровни, авто-каст, `AddActive`), `view.Spell(id, SpellStat)` | effects |
| `modkit_combat` | `IEnemies` и `ICombat` (обе через `modkit_combat`) | effects |
| `modkit_companions` | `ICompanions` | combat, spells |
| `modkit_zones` | `IZones` | combat |
| `modkit_hero` | `IHero` | — |
| `modkit_triggers` | `ITriggers` | hero |
| `modkit_resources` | `IResources.Create(имя, max, regen)` → `IResource` | — |
| `modkit_research` | `IResearch` | — |
| `modkit_game` | `IGame` (+ событие `battleResult`) | effects |
| `modkit_art` | `IArt` (и `ArtIcons`) | — |

Были в ядре, убраны: `ctx.Game/Enemies/Combat/Spells/Hero/Companions/Triggers/Research/Zones/Art/Resource(...)`, `ctx.DefineEffect/SetGlobalEffects/UseBuiltinEffects/OnHeroPass/RecalcStats`, `ModKitData.Data.For(ctx)`. Убраны совсем: `IData.AddClass/SelectClass` и `ClassDef` (ломали меню классов; свои классы — `CustomClass` из `class_menu`). `CustomClass` теперь в сборке `class_menu` (потребителю нужны ссылка на неё и `depends: class_menu`). Ниже в справочнике `IGame`, `IEnemies` и т. д. — интерфейсы этих библиотек; остальные имена из таблиц (`Alive`, `Damage`, …) не менялись.
Загрузчик грузит zip в порядке зависимостей (библиотеки первыми), поэтому библиотеки могут ссылаться друг на друга.

## 2. События (теги)

Подписка: `ctx.Events.On("имя", o => …)` (payload — `EventData`) или `ctx.Events.On<T>(t => …)` (типизированное). Свои: `ctx.Events.Emit("мой_мод:событие", данные)`.

| Событие | Когда | Данные |
|---|---|---|
| `mainMenu` | построено главное меню | `menu` |
| `battleStart` | после `C_Battle_Manager.Start` | `manager` |
| `battleEnd` | **до** `End_Battle` | `manager` |
| **`battleResult`** / тип `BattleResult` | **после** `End_Battle` (новое) | `Time`, `Kills`, `AreaId`, `AreaLevel`, `ClassId`, `PracticeGained[24]`, `PracticeGainedTotal` |
| `areaChange` | `Start_AreaChange` (только путь «Обелиск», не обычная смена подзон) | `manager` |
| `enemySpawn` / `enemyDeath` | `Start_Unit` / `Unit_Death` | `enemy` |
| `bossSpawn` | `Create_UnitEnemyBoss` | `manager`, `unitId`, `count` |
| `modEnabled` / `modDisabled` | включение/выключение мода | `id` |
| `RegistryChanged` (тип) | изменение общего реестра | `Registry`, `Id`, `Owner`, `Added` |

## 3. `IGame` — состояние игры (новое, [не проверено])

| Член | Что |
|---|---|
| `ClassId` | `Game_BattleClassID` (выбранный класс игры) |
| `AreaId`, `AreaLevel`, `AreaMax` | зона 1..14, сложность (0/1 «Хард»), число открытых зон |
| `SurvivalTime`, `Kills` | время (с) и убийства текущего боя |
| `Artifacts` | список `{Id, Count}` из `List_ArtifactObtain` |
| `SpellLevels` | `{ID заклинания → уровень}` из `List_AllyMagicLevel`, только > 0 |
| `LastResult` | итог последнего боя (`BattleResult`) |
| `Practice` | практика классов: `Max` (46), `Get(id)`, `Add(id, n)`, `Candidates()`, `GrantRandom(n)` |

Практика сохраняется при ближайшем автосохранении игры; окно награды игры изменений мода не показывает.

## 4. Остальные возможности ядра

| Область | API |
|---|---|
| Хуки | `ctx.Before/After/Replace(класс, метод, число_параметров, fn)`; хуки внутри функции недоступны |
| Периодика | `ctx.Every(мс, fn)` (главный поток) |
| Герой | `IEffects.OnHeroPass(view => …)`, `IEffects.Define`, `IEffects.SetGlobal` (эффекты подставляются при пересчёте, `docs/EFFECTS.md`) |
| Заклинания | `ISpells`: `Level`, `CanCast`, `SetAuto`, `Cast`, `Cooldown`, `AddActive` (кнопка активного каста) |
| Данные игры | `new Table("Class")`: `Get/Set/CloneRow/IndexOf`; библиотека `modkit_data` (`Edit/Add/AddToList`, откат при выключении) |
| Реестры | `ctx.Registry<T>("classes")` — общие данные между модами (классы `CustomClass`) |
| Интерфейс | `UiKit` (окна, кнопки, `PanView` — карта с прокруткой, `Pager`), `OnMainMenu`, `OpenSettings` |
| Диагностика | `ctx.Log/Warn/Error/Guard/Mark`; журнал `UserData/ModKit/modkit.log`, `gamelog.txt` |

## 4а. Бой: враги и действия (новое, собрано, **[не проверено]** на устройстве)

| Член | Что |
|---|---|
| `IEnemies` | `Alive`, `Near(x, y, r)` (ближайшие первыми), `Nearest(x, y)`, `HeroPos`. Список ведут хуки `Start_Unit`/`Unit_Death`, обхода кучи нет. `IEnemy`: `Id`, `Hp`, `Pos`, `Alive`, `Raw` |
| `ICombat` | `Damage(e, amount, magicId, criRan, criMult)` → `Unit_Damage`; `Freeze`, `Stun` → `Status_Freeze/Stun`; `Pull(e, x, y, dist)` — разовый сдвиг; `Shoot(bulletId, x, y, z, ratio)` → `Bullet_Create` героя |
| `IEffects.Recalc()` | `AbilityStatApply(2)` сразу; false, если менеджер ещё не пойман (нужен хотя бы один пересчёт в бою) |

Что проверить первым на устройстве: растёт ли `Alive.Count` в бою и не остаются ли в нём мёртвые (пул врагов переиспользуется); наносит ли `Damage` урон и показывает ли число; не тянет ли `Pull` врага сквозь стены.

Новое в ядре (собрано; **проверено на устройстве** только `Enemies.Alive/Nearest/HeroPos`, `Combat.Damage`, события `manaPickup`/`enemyDeath`, `Store`, `Resource` с полосой — по `combat_probe`; остальное **не проверено**):

| Член | Что |
|---|---|
| `IHero` | `Hp`, `HpMax`, `Heal(n)`, `Shield`/`AddShield(n)` (щит возвращает потерянное здоровье после `Unit_Damage`) |
| `IResources.Create(имя, max, regen)` | свой ресурс («Надежда»): `Value`, `TrySpend`, `Add`, `ShowBar(подпись, x, y, w, h, цвет)`; заполняется на `battleStart`; полоса видна только в бою |
| `ctx.Timer` | `After`/`Every(сек)` по игровому времени (на паузе стоит) |
| `IZones` | `Create(x, y, r, длит., тик, onTick(враги))` |
| `ctx.Store` | постоянный JSON мода: `Get/Set/Has/Remove` (`UserData/ModKit/store/<id>.json`) |
| **`IArt`** | `SetIcon(id, png)` / `ClearIcon(id)`: своя иконка артефакта везде сразу (энциклопедия, выбор, торговец, описание в бою). Родной спрайт лежит в сжатом атласе, поэтому поверх `Image` кладётся `RawImage`: хук `V_Artifact_Manager.Show_ArtifactDetail` (по ID) + обход активных `Image` каждые 150 мс (по указателю спрайта; у клонов спрайт копируется `Object.Instantiate`). Пример: мод `art_swap` (`icons/<ID>.png` в zip) |
| `ctx.Tags` | теги `Set/Of/With` на заклинаниях, артефактах, врагах |
| `IGame.GrantArtifact/RemoveArtifact` | правка `List_ArtifactObtain` + пересчёт |
| События | `heroDamaged`(`amount`), `manaPickup`(`amount`), `itemPickup`(`cellId`), `enemyDeath` теперь с `unitId` |
| `UiKit.AddHudLabel/AddHudBar` | подпись и полоса на экране без перехвата касаний |
| **`ctx.Screen`** | где игра: `Name` (`mainMenu`/`battle`/`paused`/`spellSelect`/`artifactSelect`/`dead`/`loading`), `Buttons()` и `Texts()` с координатами в пикселях экрана (как у `adb input tap`), `FindButton`, `Click(текст/кнопка)` |

Ещё добавлено (**проверено на устройстве** по `api_probe` и `combat_probe`, кроме отмеченного):

| Член | Что |
|---|---|
| `ITriggers` | `OnKills(N, fn)`, `OnHeroDamaged(fn)`, `OnLowHp(доля, пауза, fn)`, `OnManaPickups(N, fn)`; счётчики сбрасываются на `battleStart` |
| `IResearch` | `Define(id, цена, требования…)`, `AddPoints`, `Unlocked`, `CanUnlock`, `Unlock`; прогресс в `ctx.Store`, событие `research:unlocked` |
| `ctx.Toast(текст, сек)` | подпись на экране по игровому времени |
| `ctx.ReadAsset(путь)` | байты файла из zip мода |
| `view.Spell(id, SpellStat.Damage/Num/Cooldown/Area/Size)` | поле заклинания героя в `OnHeroPass` |
| `ActiveSpellDef.Row` | второй ряд кнопок активных заклинаний (**не проверено**) |
| Профилировщик хуков | хук дольше 8 мс пишется в журнал как «медленный хук» (до 30 строк) |

`itemPickup` теперь хук `C_Battle_Item.Obtain_Item` (EventData: `type` = `ObtainItemType`, `objType` = `Type_ObjItem`) — **проверено**: `objType=1` — шарики (type 1–3), `objType=3` — руны/бонусы (type 15, 21, 34; руна защиты — вероятно 34, подобрана последней), `objType=4` — type 18 (вероятно сундук). Не сделано: `Companions`, изменение результата в хуках (`Replace` остаётся единственным способом), декларативные моды (`content.json`).


## 5. Ограничения (чтобы не наступать повторно)

- `GameObject.Find` недоступен (ICall не резолвится): искать через `FindObjectsOfType(Il2CppType.Of<Transform>())`.
- `ObscuredInt/Float` читать через `GetDecrypted()`; писать только через обёртку игры, не по смещению. Сохраняемые массивы с контрольной суммой не править (`Array_JournalProgress`, `Array_AreaBestScoreNum`, [58]/[59] в практике).
- Класс игры задаёт `Start_Ability` один раз (запись `90000+класс`); подмена `Game_BattleClassID` позже не работает — подменять строку таблицы `Class` (мод `class_menu`).
- Слоты эффектов класса: 1..7 (слот 7 — мастерство, практика ≥46), 8 и 9 игра у школ не читает (`CLASS_MENU.md`).
- Практика за бой: только в ветке «новый журнал»; `class_practice` добирает её сам (`PRACTICE.md`).

## 6. Планы (не сделано)

Событие «награда» с заменой значения до окна наград (нужен хук внутри `End_Battle`, недоступен); список артефактов с названиями из таблицы; теги предметов/эффектов в таблицах; единая подписка на «выбор класса».

## 7. Эмулятор (проверка без телефона)

Пробовали Android-эмулятор из SDK (AVD `Pixel_5`, x86_64 + трансляция arm64, API 37.2): `emulator -avd Pixel_5 -no-snapshot -gpu swiftshader_indirect`. Игра (патченый `D:\tools\lemon\out\base.apk`, пакет `com.vkslrzm.Zombim`) **запускается**, но MelonLoader не стартует: нет `UserData/ModKit`, журнала нет. Вероятная причина — нативные хуки LemonLoader/Il2CppInterop не работают под трансляцией ARM [не проверено]. Для проверки модов пока нужен телефон. Голый `qemu-system-aarch64` без образа Android не подходит (образа нет).
