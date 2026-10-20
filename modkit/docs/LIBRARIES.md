# Библиотеки ModKit (`modkit_*`)

Библиотеки — моды с `"library": true`: они включаются сами, когда включён мод, который от них зависит. Подключение:

1. `mod.json` мода: `"depends": { "modkit_combat": ">=1.0" }` (для `modkit_data` — `">=1.5"`);
2. `csproj`: `<Reference Include="modkit_combat"><HintPath>путь/modkit_combat.dll</HintPath><Private>false</Private></Reference>`;
3. код: `var combat = ctx.Use<ICombat>("modkit_combat");` — один объект на мод, всё сделанное через него откатывается при выключении вашего мода.

DLL библиотек лежат в zip с одноимённым названием ([релиз](https://github.com/Kysnez4/MS_KIT/releases/tag/modkit-2.0-mods)) или собираются из `modkit/melon/mods/modkit_*`. Пометка ✅ — проверено на устройстве, ◻ — нет.

## Граф зависимостей

```
modkit_data ─┬─ modkit_effects ─┬─ modkit_spells ───┬─ modkit_companions
             │                  ├─ modkit_combat ───┤        
             │                  └─ modkit_game      └─ modkit_zones
             ├─ modkit_artifacts (+ modkit_art)
             ├─ modkit_combos
             ├─ modkit_synergies
             └─ modkit_spells_data
modkit_hero ── modkit_triggers        modkit_resources     modkit_research     modkit_art
```
`modkit_companions` зависит от combat и spells, `modkit_zones` — от combat, `modkit_triggers` — от hero. Транзитивные зависимости включаются сами; в `depends` своего мода перечисляйте только то, что используете.

| Библиотека | `ctx.Use<T>` | Раздел |
|---|---|---|
| `modkit_data` | `IData` | §1 |
| `modkit_artifacts` | `IArtifacts` | §2 |
| `modkit_combos` | `ICombos` | §3 |
| `modkit_synergies` | `ISynergies` | §4 |
| `modkit_spells_data` | `ISpellsData` | §5 |
| `modkit_effects` | `IEffects` | §6 |
| `modkit_spells` | `ISpells` | §7 |
| `modkit_combat` | `IEnemies`, `ICombat` | §8 |
| `modkit_companions` | `ICompanions` | §9 |
| `modkit_zones` | `IZones` | §10 |
| `modkit_hero` | `IHero` | §11 |
| `modkit_triggers` | `ITriggers` | §12 |
| `modkit_resources` | `IResources` | §13 |
| `modkit_research` | `IResearch` | §14 |
| `modkit_game` | `IGame` | §15 |
| `modkit_art` | `IArt` | §16 |

## 1. `modkit_data` — сырой доступ к данным игры (`namespace ModKitData` и `ModKit`)
Таблицы игры — `Z_Database.Dictionary_*` (Ability, MagicCom, Synergy, Class…); колонки — в [TABLES.md](TABLES.md).

| Член | Что |
|---|---|
| `WhenReady(fn)` | вызвать `fn` на главном потоке, когда игра загрузила таблицы (обычно через несколько секунд после старта). Делайте все правки данных внутри |
| `Table(name)` | `Table`: `Get/Set/GetRaw/SetRaw`, `IndexOf(id, колонка)`, `CloneRow`, `RemoveLast`, `MaxId`, `Length`, `Columns()`, `IdColumn` |
| `Edit(table, id, {колонка: значение})` | поменять ячейки строки; откатывается при выключении мода; предупреждает, если ту же ячейку правит другой мод |
| `Add(table, cloneId, {…})` | добавить копию строки с новым ID (возвращается) |
| `AddToList(listField, value)` | добавить число в статический `List<int>` `Z_Database` (например `List_ArtifactPossibleGrade_C`) |

Также: `Effect(id, value)` — пара «ID эффекта игры, значение» (неявно из `(int, double)`), `Commands`/`Sprites` — низкоуровневые операции для других библиотек. Правка данных — `restartRequired: true` в `mod.json`: полноценно действует после перезапуска.
```csharp
ctx.Use<IData>("modkit_data").WhenReady(() => ctx.Use<IData>("modkit_data").Edit("Ability", 41, new Dictionary<string, object> { ["이름"] = "Новое имя" }));
```

## 2. `modkit_artifacts` — новый артефакт «под ключ» ✅
Добавляет строку Ability, иконку, пулы выпадения, запись `Dictionary_ApplySynergy`, плитку в энциклопедии и окно описания.

| Член | Что |
|---|---|
| `Add(ArtifactDef)` → ID | новый артефакт |
| `AddMany(List<ArtifactDef>)` → ID | несколько за раз (быстрее) |
| `Edit(id, ArtifactChanges)` | имя, эффекты, описание, редкость |
| `Remove(id)` / `Restore(id)` | убрать из выпадения (сундуки, торговец) / вернуть |

`ArtifactDef`: `Name`, `Lines` (до 6 строк описания), `Rarity` (`C` обычное, `B` редкое, `A` героическое, `P` особое, `S` легендарное), `Icon` (ID готового артефакта, чью иконку взять), `IconFile` (путь к PNG на телефоне), `Effects` (до 9; `new Effect(id, value)`), `CloneFrom` (образец, по умолчанию 42), `Weight` (число копий в пуле, по умолчанию 5), `Guaranteed` (тест: выпадает почти всегда). ID эффектов — [EFFECTS.md](EFFECTS.md); свои эффекты (ID ≥ 10000) — `modkit_effects`. Пример — мод `artifact_pack`.

## 3. `modkit_combos` — комбинации (таблица MagicCom) ◻
`Add(ComboDef)`, `AddMany`, `Edit(id, ComboChanges)`. `ComboDef`: `Name`, `Effects`, `Lines`, `CloneFrom` (по умолчанию 1), `Requires` (ID навыков, до 3), `Icon`.

## 4. `modkit_synergies` — синергии (таблица Synergy) ✅
`Add(SynergyDef)`, `AddMany`, `Remove(id)` (очистить синергию), `AddArtifact(synergyId, artifactId)` (максимум 5), `RemoveArtifact`. `SynergyDef`: `Name`, `Effects` (до 3), `Lines`, `Members` (ID артефактов, до 5), `CloneFrom` (3), `Icon`. Библиотека расширяет `Array_SynergyObtain` под новый ID (без этого игра падает).

## 5. `modkit_spells_data` — данные заклинаний (Ability 1..21) ◻
`Get(id)` → `SpellInfo` (`Name`, `Description`, `MaxLevel`, `Slots` 2..9: слот N — уровень N), `Edit(id, SpellChanges)`, `Scale(id, effectId, множитель)`. `SpellChanges`: `Name`, `Description`, `MaxLevel`, `Slots` (замена эффекта в слоте), `Scale` и `Add` (множитель и слагаемое по ID эффекта). Числа в тексте уровней обновляются вслед за значениями. Пример — `spell_tuner`.

## 6. `modkit_effects` — свои эффекты и правка героя ✅
Движок после каждого пересчёта характеристик (`AbilityStatApply`) и раз в ~0,7 с смотрит полученные артефакты, класс, комбинации, синергии и применяет эффекты.

| Член | Что |
|---|---|
| `Define(EffectDef)` | свой эффект артефакта/комбинации (ID ≥ 10000): `Apply(view, value, ctx)` вызывается для каждого источника с этим эффектом; `Expand` — альтернативно раскрывает эффект в пары «ID игры, значение», которые игра считает сама |
| `SetGlobal((id, value)[])` | эффекты мода без артефакта; одинаковые ID суммируются; пустой массив снимает |
| `OnHeroPass(view => …)` | проход по герою после каждого пересчёта; запись задаёт результат, поля, которых проход не коснулся, возвращаются к базе |
| `UseBuiltin()` | встроенные эффекты 10001–10004: урон пули %, перезарядка −%, снарядов +N, радиус сбора % |
| `Recalc()` | пересчитать характеристики героя сразу; `false` — менеджер ещё не известен (нужен хотя бы один пересчёт в бою) |

`IHeroView`: `Field(имя)` (поле героя `UnitApply_*`), `Db(имя)` (`Z_Database.AllyUnitVar_*`), `MulPercent(имя, процент)`, `Add(имя, n)`, `Display(имя, f)` (правка только на время окна характеристик), `Raw` (сам `C_Battle_AllyUnit`). `FieldRef.Value` читает **базу** (или накопленную цель), запись задаёт цель; не пишите в поля героя напрямую. Расширение `view.Spell(id, SpellStat.Damage|Num|Cooldown|Area|Size)` (из `modkit_spells`) даёт поле заклинания по его ID. Модель эффектов подробно — [EFFECTS.md](EFFECTS.md); пример — `projectile_cap`, `kill_streak`.

## 7. `modkit_spells` — заклинания героя в бою ✅
| Член | Что |
|---|---|
| `InBattle`, `Paused` | идёт ли бой (герой жив, меню не показано), пауза |
| `Level(id)` | уровень заклинания (ID Ability 1..21); 0 — не получено |
| `CanCast(id)` | есть корутина `Play_X` и флаг `IsX_P` |
| `SetAuto(id, bool)` | выключить/включить авто-каст (возвращается при выключении мода) |
| `Cast(id, steps=2)` | один залп вручную |
| `Cooldown(id)` | перезарядка в секундах, как в игре |
| `AddActive(ActiveSpellDef)` → `IActiveSpell` | кнопка на экране боя, касты вручную |

`ActiveSpellDef`: `SpellId`, `Label`, `Cooldown`, `Burst`, `Slot`, `Row`, `Color`, `OnCast`. `IActiveSpell`: `Remaining`, `Press()`, `Remove()`. ID заклинаний (`SpellTable.Names`): 1 Магическая пуля, 2 Огненный шар, 5 Ледяные иглы, 7 Молния, 8 Электрозона, 9 Цунами, 10 Метеорит, 12 Торнадо, 13 Электричество, 14 Армагеддон, 15 Испепеление, 16 Энергопуля, 17 Метель, 18 Магический луч, 19 Магический круг, 20 Лавовая область, 21 Вспышка. Подробнее — [ACTIVE_SPELLS.md](ACTIVE_SPELLS.md).

## 8. `modkit_combat` — враги и действия ✅
`IEnemies`: `Alive`, `Near(x, y, r)` (ближайшие первыми), `Nearest(x, y)`, `HeroPos`. `IEnemy`: `Id`, `Hp`, `Pos`, `Alive`, `Raw`. Объекты врагов переиспользуются (пул): не держите ссылку между боями.
`ICombat`: `Damage(e, amount, magicId=0, criRan=0, criMult=1)`, `Freeze(e, сек)`, `Stun(e, сек)`, `Pull(e, x, y, dist)` (разовый сдвиг), `Shoot(bulletId, x, y, z, ratio)` (снаряд игры из позиции героя). Все методы возвращают `false`, если боя нет или цель мертва. Список врагов ведут хуки `Start_Unit` / `Unit_Death`, обхода кучи нет.

## 9. `modkit_companions` — союзники на двойниках героя ✅
`ICompanions`: `MaxSlots`, `Active`, `Spawn(dx, dy, spellId=0, color=null, scale=0.7, mode=Follow)` → `ICompanion` (или `null`: нет слота или боя), `Clear()`. Режимы `CompanionMode`: `Follow` (рядом), `Guard` (встаёт между героем и врагом), `Hunt` (идёт к «жирному» врагу). `ICompanion`: `Slot`, `Offset`, `Alive`, `Spell`, `Mode`, `Pos`, `Speed`, `Leash`, `StandOff`, `GuardRadius`, `Remove()`. Союзники стреляют корутиной игры `Play_Doppelganger_Attack`; детали — [COMPANIONS.md](COMPANIONS.md). Создавайте союзников в бою (на `battleStart`).

## 10. `modkit_zones` — зоны на время ◻
`IZones.Create(x, y, radius, duration, tick, onTick(врагов в радиусе))` → `IZone` (`Alive`, `Remove()`). Время — игровое.

## 11. `modkit_hero` — герой ✅
`IHero`: `Hp`, `HpMax`, `Heal(n)` (возвращает вылеченное), `Shield`, `AddShield(n)` (щит поглощает урон: потерянное здоровье возвращается за счёт щита; обнуляется в начале боя).

## 12. `modkit_triggers` — готовые условия ✅
`ITriggers`: `OnKills(N, fn(всего))`, `OnHeroDamaged(fn(урон))`, `OnLowHp(доля, пауза_сек, fn)`, `OnManaPickups(N, fn)`. Счётчики сбрасываются на `battleStart`.

## 13. `modkit_resources` — свой ресурс мода ✅
`IResources.Create(имя, max, regenPerSec)` → `IResource`: `Value`, `Max`, `Regen`, `TrySpend(n)`, `Add(n)`, `ShowBar(подпись, x, y, w, h, цвет)` (полоса видна только в бою). Заполняется до максимума на каждый `battleStart`.

## 14. `modkit_research` — дерево исследований ✅
`IResearch`: `Define(id, цена, требования…)`, `All`, `Points`, `AddPoints(n)`, `Unlocked(id)`, `CanUnlock(id)`, `Unlock(id)`. Прогресс хранится в `ctx.Store`; событие `research:unlocked` (`id`).

## 15. `modkit_game` — состояние игры ◻
`IGame`: `ClassId`, `AreaId`, `AreaLevel`, `AreaMax`, `SurvivalTime`, `Kills`, `Artifacts` (`{Id, Count}`), `SpellLevels` (ID → уровень), `LastResult` (`BattleResult`), `Practice` (`Max`=46, `Get`, `Add`, `Candidates`, `GrantRandom`), `GrantArtifact(id)`, `RemoveArtifact(id)` (с пересчётом). Библиотека рассылает событие `battleResult` (тип `BattleResult`). Практика сохраняется при ближайшем автосохранении игры; подробнее — [PRACTICE.md](PRACTICE.md).

## 16. `modkit_art` — иконки артефактов ✅
`IArt`: `SetIcon(artifactId, png)` (лучше 248×288 с прозрачностью), `ClearIcon(id)`. Подмена видна везде: энциклопедия, выбор, торговец, описание в бою. Пример — `art_swap` (`icons/<ID>.png` в zip).

## Своя библиотека
Схема в [MOD_FORMAT.md](MOD_FORMAT.md) §5. Образец минимальной библиотеки — `modkit/melon/mods/modkit_hero`; внутренние классы получают `ModContext` и откатываются через `ctx.OnCleanup`/`ctx.CleanupList()`. Если библиотеке нужны внутренности ядра (`Hooks`, `Raw`, `UiKit`), добавьте её имя в `modkit/melon/ModKit/Properties.cs` (`InternalsVisibleTo`) или пользуйтесь публичными `Hooks.AddBefore/AddAfter`.
