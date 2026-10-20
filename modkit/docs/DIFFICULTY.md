# Сложность: здоровье, урон, число врагов и боссов — разведка C3

Статус: чтение `dump.cs`, `tables/UnitStat.json`, `tables/Explain.json`, журнала `g.txt` и дизассемблера `libil2cpp.so` (Python + capstone; RVA из `dump.cs`; адрес в Ghidra = RVA + 0x100000). Ghidra-MCP не использовалась, на устройстве ничего не запускалось, код ядра/модов не менялся.
Пометки: **[П]** подтверждено по дампу/ассемблеру/таблице/журналу, **[Г]** догадка, нужна проверка на устройстве. Версия игры 612.

## 1. Кратко

1. **Безопасное и точное место для множителей — метод `C_Battle_EnemyUnit.Start_Unit(int unitId, float varA, float varB)`** (RVA 0x1094d4c): им создаются **все** враги, включая боссов (`Create_UnitEnemy`, `Create_UnitEnemySpecial`, `Create_UnitEnemyBoss` зовут его). После него готовы поля `UnitApply_HpMax`, `UnitApply_Attack`, `ApplyBulletAttack`, `UnitApply_MoveSpeed`, `UnitApply_UnitSize`, `Unit_Hp` — обычные `float`, никаких `Obscured*`, в сохранение не попадают [П]. Событие ядра `enemySpawn` — это как раз `After` на `Start_Unit`.
2. Базовые характеристики врагов лежат в таблице `UnitStat` (`Z_Database.Dictionary_UnitStat`, 51 строка, 46 рабочих; колонки здоровье/атака/скорость/размер/мана) — правятся через `ModKit.Table` до боя, откатываются при выключении мода [П].
3. «Рост врагов со временем» делает корутина `C_Battle_Manager.EnemyLevelUp` (MoveNext 0x10d11b0): на каждом из ≈77 шагов **добавляет** к статикам `Z_Database.EnemyUnitVar_HpMaxRatioU / AttackRatioU / MoveSpeedRatioU` приращения из списков зоны и переписывает паузу спавна. Множители на этом пути мод лучше не трогать (приращения копятся к уже изменённому значению) — масштабировать итог в `Start_Unit` [П].
4. Число врагов: поле `C_Battle_Manager.EnemyCreateNum` (+0x930, `int`, число за пачку) и `ApplyEnemyCreateDelay` (+0x940, `float`, пауза между пачками) — читаются в `Create_UnitEnemy.MoveNext` (RVA 0x10cf79c) [П]. Масштабируются парой хуков Before/After на `MoveNext` (подменить на время вызова, вернуть).
5. Босс: рождается в `Create_UnitEnemyBoss(int, int)` (RVA 0x10b171c) из поздних шагов `EnemyLevelUp` и проходит через тот же `Start_Unit` (ID 41–46 в `UnitStat` — «боссовые» строки: 10–24 тыс. HP, 100% сопротивления). Отличить от рядовых — по `UnitID >= 41` [П].
6. Для эффектов героя уже есть **три штатных статовых эффекта «враги»**: 54 (макс. HP всех врагов ±%), 55 (скорость ±%), 33 (размер ±%) — проходят через `AllyUnitVar_EnemyHpRatio/EnemyMoveSpeedRatio/EnemySizeRatio`, без кода (EFFECTS.md); для их применения достаточно `SetGlobalEffects` [П по EFFECTS.md и чтению `AllyUnitVar_*`].
7. Античит ACTk: враги, их поля и статики `EnemyUnitVar_*`, `AllyUnitVar_*` — обычные `float`/`int`. **ObscuredInt/ObscuredFloat**: `Game_Money_Gold/Gem`, `Game_SurvivalTime`, `Game_ResearchPoint/Level`, `Game_SciencePoint`, а в `C_Battle_Manager` — `Kill_RewardGold`, `Kill_RewardGoldInc`, `Apply_Kill_RewardGoldInc`, `ApplyRewardGold`, `ApplyRewardGem`, `Reward_ItemCellGold`, `CulGameNumA`. Их не писать (награды: только через настройки множителей без прямых записей, см. §8) [П].

## 2. Таблица UnitStat — базовые характеристики врагов [П]

`Dictionary_UnitStat` — `List<Dictionary<string,object>>`, **индекс = ID** (`Start_Unit` читает `list[UnitID]` на 0x1095160..0x10954d8; чтение — при **каждом** спауне). Колонки: `마나량` (мана за убийство) → `ManaGainNum` (+0x4ac), `공격력` → `UnitBase_Attack` (+0x37c), `최대 체력` → `UnitBase_HpMax` (+0x380), `상태이상 저항력` → `UnitBase_DebuffResistance` (+0x388), `즉사 저항력` → `UnitBase_DeathAttackResistance` (+0x38c), `이동속도` → `UnitBase_MoveSpeed` (+0x390), `유닛크기` → `UnitBase_UnitSize` (+0x394), `특수능력01..05`, `제거 모션ID/크기`. Названия в таблице корейские; перевод — мой (латиница).

| ID | Враг | Мана | Атака | HP | Скорость | Размер | Особенности |
|---|---|---|---|---|---|---|---|
| 1 | Swarm (мал.) | 3 | 10 | 100 | 34 | 125 | — |
| 6 | Swarm (ср.) | 4 | 15 | 200 | 32 | 100 | — |
| 7 | Minion | 4 | 30 | 200 | 32 | 105 | 1 |
| 8 | Stinger | 4 | 15 | 100 | 29 | 125 | 1, 2 |
| 11 | Swarm (бол.) | 5 | 20 | 300 | 32 | 90 | — |
| 12 | Drone | 5 | 40 | 300 | 32 | 95 | 1 |
| 13 | Interceptor | 5 | 40 | 300 | 32 | 75 | 1 |
| 14 | Punisher | 5 | 40 | 300 | 32 | 85 | 1 |
| 16 | Hive | 6 | 20 | 500 | 28 | 80 | 12 |
| 17 | Dust | 6 | 20 | 500 | 20 | 85 | — |
| 18 | Rampage | 6 | 40 | 250 | 29 | 70 | 1, 2 |
| 19 | Horizon | 6 | 20 | 300 | 29 | 70 | 1, 2 |
| 21 | Mine | 8 | 50 | 400 | 30 | 55 | 1 |
| 22 | Howling | 8 | 50 | 400 | 30 | 50 | 1 |
| 23 | Phantom | 8 | 25 | 400 | 30 | 55 | — |
| 24 | Trooper | 6 | 40 | 1500 | 30 | 85 | 1 |
| 26 | Atlas | 10 | 30 | 600 | 24 | 60 | 12, 10; сопр. 50 |
| 27 | Overlord | 10 | 60 | 600 | 24 | 55 | 1; сопр. 50 |
| 28 | Goliath | 10 | 60 | 600 | 24 | 55 | 1; сопр. 50 |
| 29 | Sentinel | 50 | 60 | 2400 | 24 | 70 | 1, 30; сопр. 50 |
| 31 | Scarab | 5 | 30 | 150 | 29 | 85 | 1, 2 |
| 32 | Corrupt | 5 | 20 | 300 | 32 | 90 | — |
| 33 | Seraph | 50 | 30 | 4000 | 18 | 120 | 12, 10, 30; сопр. 50 |
| 34 | Oculus | 8 | 25 | 500 | 18 | 70 | сопр. 50 |
| 35–38 | Human / Fairy / Spirit / Demon | 10 | 40 | 900 / 1200 / 1200 / 1200 | 28 | 105–110 | 1, 2, 24, 25; сопр. 50 |
| 39 | Jack-o'-lantern | 10 | 40 | 900 | 28 | 110 | 1, 2, 24; сопр. 50 |
| 40 | Servant | 10 | 40 | 1200 | 22 | 95 | 1, 2, 24, 25; сопр. 100 |
| 41 | Citadel (босс) | 500 | 50 | 15000 | 14 | 105 | 12, 11, 10, 6; сопр. 100/100 |
| 42 | Nemesis (босс) | 500 | 50 | 18000 | 9 | 110 | то же |
| 43 | Apostle (босс) | 500 | 50 | 24000 | 9 | 115 | то же |
| 44 | Amadeus (босс) | 500 | 50 | 12000 | 0 | 120 | то же |
| 45 | Watcher (босс) | 500 | 50 | 12000 | 9 | 135 | то же |
| 46 | Revenant (босс) | 500 | 50 | 10000 | 12 | 100 | то же |
| 4, 5 | Horizon / Oculus (ч/б) | 0 | 0 | 0 | 0 | 0 | служебные |

Прочие строки (ID 0, 2, 3, 9, 10, 15, 20, 25, 30, 47–50) — заглушки. «Особенности» — значения `특수능력01..05` (флаги поведения: 1 — ближняя атака, 2 — рывок, 10/11/12/24/25/30 — другие умения; смысл не расшифрован [Г]). Состав врагов по зонам — AREAS.md §7.

## 3. Как Start_Unit считает характеристики [П]

Читается из `Z_Database` (статики) и героя; формулы восстановлены по ассемблеру (0x1098380..0x1099318), константа 0.01 проверена по литералу (0x636000+0x1d4):
- **Здоровье:** `UnitApply_HpMax = round( UnitBase_HpMax × EnemyUnitVar_HpMaxRatio_Bonus × EnemyUnitVar_HpMaxRatio_Special × (EnemyUnitVar_HpMaxRatioU × Unit_HpMaxRatio_Bonus + 100)/100 × max(0, (100 − EnemyUnitVar_HpMaxRatioD)/100) × C_Battle_AllyUnit.ApplyEnemyHpRatio × C_Battle_Manager.Apply_ArmageddonEnemyHpRatio ) + UnitApply_HpMax_Bonus` (округление «до чётного»; поля `+0x3c0` и `+0x3a0` — у самого врага). `ApplyEnemyHpRatio` (статик героя +0x20) считается из `Z_Database.AllyUnitVar_EnemyHpRatio` (эффект 54).
- **Атака контактом:** `UnitApply_Attack = round( UnitBase_Attack × (100 + EnemyUnitVar_AttackRatioU)/100 × max(0, (100 − EnemyUnitVar_AttackRatioD)/100) )`. Это поле читает `Unit_Attack` (0x109b614).
- **Атака снарядами:** `ApplyBulletAttack` (+0x3f0) считается так же из собственного значения снаряда (0x1098a94..0x1098aec, потолок 100×); читают `Unit_BulletAttack` (корутина) и `C_Battle_AllyBullet.BulletAttack`.
- **Скорость:** `UnitApply_MoveSpeed` (+0x3c4) из `UnitBase_MoveSpeed` и `EnemyUnitVar_MoveSpeedRatioU/D`, `AllyUnitVar_EnemyMoveSpeedRatio` (эффект 55).
- **Размер:** `UnitApply_UnitSize` (+0x3d4) из `UnitBase_UnitSize`, `EnemyUnitVar_UnitSizeRatioU/D`, `AllyUnitVar_EnemySizeRatio` (эффект 33).
- **«Получаемый урон» врага:** `UnitApply_HitDamageRatio` (+0x3a4) = 1 × `C_Battle_Manager.BossWave_HitDamageRatio` (статик +0x1fc, у боссовой волны отличается от 1) × `(100 + EnemyUnitVar_HitDamageRatioU)/100` × `(100 − …D)/100` [П по структуре].
- **Особые/элитные юниты** (строка-тег `UnitTag`, ветки 0x1098398…0x10985f0): для одной ветки `HpMax = (2·HpMax + константа) × ApplyEliteMonsterHpRatio` (статик героя +0xcc) и `UnitApply_HpMax_Bonus` += константам по `AreaChangeNum ≥ 0..6` (0x1098424..0x1098568) [П].
- В конце метода: `BUnitApply_Attack = UnitApply_Attack`, `BUnitApply_UnitSize = UnitApply_UnitSize`, `Unit_Hp = min(UnitApply_HpMax, константа)` (0x10992f8..0x1099318).
Поля героя `AllyUnitVar_EnemyHpRatio` (0xac4), `EnemyMoveSpeedRatio` (0xac8), `EnemySizeRatio` (0xa70), `LargeEnemyHpRatio` (0xb38), `EnemyHpAndAttackRatio` (0xb3c), `EnemyCreateDelayRatio` (0xc00), `EnemyTimeDamageRatio` (0xbb8) — «эффекты героя», воздействующие на врагов (ID эффектов — EFFECTS.md; 84 уже проверен как работающий через подстановку, CONDITIONAL.md).

## 4. Рост с волнами (`EnemyLevelUp`, MoveNext 0x10d11b0) [П]

Журнал `g.txt` (зона 4): при старте боя статики выставляются в `EnemyUnitVar_AttackRatioU = 4`, `HpMaxRatioU = 10`, `MoveSpeedRatioU = 2`, через секунду `HpMaxRatio_Bonus: 1 → 0.2` (стартовый множитель HP) — зона-зависимые значения кладёт `C_Battle_Manager.Start` (0x10b00f0, 0x10b0128, 0x10b015c).
Дальше `EnemyLevelUp` проходит ≈77 шагов (по 66 КБ кода; шаги привязаны к `List_ApplyWaveTime` +0x918). На каждом шаге:
- `EnemyUnitVar_HpMaxRatioU += List_ApplyUnitEnemyStatRatio0K[i] × List_ApplyWaveStat_HpMax[j]` (0x10d22f4..0x10d2334), `MoveSpeedRatioU += Ratio[1] × 10 или 20` (0x10d2364, 0x10d24f8), `AttackRatioU += Ratio[2] × …` — **приращения аддитивные**, значения — константы зоны из `Start` (`List_UnitEnemyStatRatio` 0x8a8 и `List_ApplyUnitEnemyStatRatio01..06` 0x8b0..0x8d8, в `Start` их строят для каждой зоны);
- `EnemyCreateDelay (+0x93c) = clamp(Ratio01[3] × константа, EnemyCreateDelay_Min (+0x944), 100)` (0x10d210c) — 78 записей на весь забег;
- `Z_Database.List_UnitEnemyDistribution.set_Item(j, Dictionary_AreaEnemyID[AreaChangeID][k])` — новые типы врагов по ходу (AREAS.md §7);
- на последних шагах: `EnemyCreateNum` (+0x930) выставляется константами 80/100/150 (0x10d5618, 0x10ddf8c, 0x10dec5c, 0x10df52c, 0x10dfdc4, 0x10e0470, 0x10e0494, 0x10e0c5c), `Create_UnitEnemyBoss` (0x10e044c, 0x10e05cc), `EnemyUnitVar_HpMaxRatio_Bonus` (0x10e01fc) и `ApplyEnemyCreateDelay` (0x10e04f4);
- `Create_UnitEnemy_StartLoop` перезапускает спавн (для зон 12–14 и «зачистки»).
**Вывод:** сложность набирается тремя рычагами: (1) приращения статиков `EnemyUnitVar_*U` по шагам, (2) пачка `EnemyCreateNum` и пауза `ApplyEnemyCreateDelay`, (3) состав типов врагов (`Dictionary_AreaEnemyID`).

## 5. Спавн: число и скорость [П]

`Create_UnitEnemy` (`<Create_UnitEnemy>d__534`, MoveNext 0x10cf79c): `for i in 0..EnemyCreateNum` — по одному `Start_Unit` из пула `List_BattleUnitEnemy_Empty` (статик +0x68), внутри пачки короткие паузы (≈`ApplyEnemyCreateDelay/EnemyCreateNum`, динамически подгоняются под число свободных объектов пула: сравнение с 0xc9 = 201 и 0x1e = 30, 0x10cfa18, 0x10cfaf4); **между пачками** `WaitForSeconds(ApplyEnemyCreateDelay)` (0x10cff58). Состав врага — случайный элемент `List_UnitEnemyDistribution` (по `Object.name` → `Int32.TryParse` пула подбирается `UnitID`). Потолки: размер пула (число одновременных врагов ограничено), `AllEnemyNum` (+0x950).
Что читают: `EnemyCreateNum` (+0x930, `int`, читается как `ldr s0` и `scvtf` — `float` на лету), `ApplyEnemyCreateDelay` (+0x940, `float`). `EnemyCreateDelay` (+0x93c) пишется 78 раз `EnemyLevelUp` и копируется в `Apply…` в `Start` (0x10a8404) и в конце `EnemyLevelUp`; другого читателя не найдено [Г]. Поэтому для темпа спавна менять надо **оба** (0x93c и 0x940) или подменять `ApplyEnemyCreateDelay` на время `MoveNext`.
Поле героя `AllyUnitVar_EnemyCreateDelayRatio` (+0xc00) по имени влияет на темп спавна [Г, читатель не разбирался; эффект в EFFECTS.md не найден].
Другие спавнеры: `Create_UnitEnemySpecial` (`<Create_UnitEnemySpecial>d__535`; читает `EnemySpecialCreateDelay` +0x94c, `SuperEnemy*` +0x964..0x970), `Create_UnitEnemyEpic` (поля `CreateEpicEnemyID/Num/Delay/FirstDelay` +0x98c..0x9a8) — «особые»/«эпические» враги; по ним — те же приёмы (числа и паузы полями).

## 6. Боссы [П]

- `Create_UnitEnemyBoss(int PUnitEnemyID, int PUnitEnemyNum)` (0x10b171c): берёт объект из пула, `set_tag`, позицию вокруг героя, читает `Game_AreaID` (ветки `==1`, `>=0xb` для положения), `Array_AreaBestScoreNum`, ведёт счётчик `ApplyBossNum` (статик +0x28c) и вызывает `Start_Unit` (0x10b1bd0).
- Вызывается из `EnemyLevelUp` (0x10e044c, 0x10e05cc) — т.е. боссы приходят **в конце таймлайна** зоны; события ядра `bossSpawn` (`unitId`, `count`) подходят для замеров и для замены ID (ID можно менять в `Before`-хуке: массив аргументов хука `a[0]`).
- Волна босса: `Play_BossWaveEffect_Start/End` (0x10b0740/0x10b07a8), `Play_BossWave_Enforce` (0x10b0878), `BossWaveBar_*`/`BossWaveGround_*` (цвета) и статики `BossWave_VisionRatio` (+0x1f8), `BossWave_HitDamageRatio` (+0x1fc), `BossWave_SpeedRatio` (+0x200), `BossWave_SizeRatio` (+0x204), `BossWave_EnforceVar` (+0x208) — обнуляются в `Start` (1.0) и меняются «усилением босса» в течение волны (читает `Start_Unit`, 0x1098c3c).
- ID боссов 41–46, 10–24 тыс. HP; рядовые формулы §3 к ним применяются так же (поэтому `Start_Unit`-хук масштабирует и босса; для отдельного множителя босса — по `UnitID >= 41`).

## 7. Рычаги: что и на каком хуке менять

Хуки — имена как принимает ядро (`ctx.After/Before(cls, method, paramCount, …)`); для итераторов формат вложенных классов `C_Battle_Manager+_Create_UnitEnemy_d__534` / `…EnemyLevelUp…` нужно проверить в `Hooks.FindType` (подтверждён только `C_Battle_AllyUnit+_Play_Fireball_d__1134`).

| # | Рычаг | Поле / метод | Хук и время | Безопасность | Замечания |
|---|---|---|---|---|---|
| 1 | HP врагов ×k | `UnitApply_HpMax` (+0x39c) и `Unit_Hp` (+0x3bc) | `After C_Battle_EnemyUnit.Start_Unit(3)` | да: `float`, нет сохранения, нет Obscured | пересчитывать `Unit_Hp = UnitApply_HpMax`; ограничить сверху (ниже 1e9) |
| 2 | Урон врагов ×k | `UnitApply_Attack` (+0x398), `BUnitApply_Attack` (+0x48c), `ApplyBulletAttack` (+0x3f0) | `After Start_Unit` | да | `BUnitApply_Attack` — копия для баффов; контактный урон — `UnitApply_Attack`; дальний — `ApplyBulletAttack` |
| 3 | Скорость ×k | `UnitApply_MoveSpeed` (+0x3c4), `BUnitApply_MoveSpeed` (+0x498) | `After Start_Unit` | да | при больших k враги обгоняют героя — ограничить 3× |
| 4 | Размер ×k | `UnitApply_UnitSize` (+0x3d4), `BUnitApply_UnitSize` (+0x4a0), `Transform` масштаб | `After Start_Unit` | осторожно | размер влияет на хитбокс/столкновения; использовать эффект 33 вместо прямой записи |
| 5 | Отдельный множитель боссов | то же по `UnitID >= 41` (41–46) | `After Start_Unit` | да | `Unit_Hp` боссов огромны; потолок 1e9 |
| 6 | Количество врагов ×k | `EnemyCreateNum` (+0x930) | `Before/After <Create_UnitEnemy>.MoveNext` (подмена на время вызова) | да, но **горячий** хук (несколько раз в секунду) — только поле и откат | ограничивает пул (~200 живых); при k>2 — исчерпание пула, рост лагов |
| 7 | Темп спавна ×k | `ApplyEnemyCreateDelay` (+0x940), `EnemyCreateDelay` (+0x93c) | `Before/After <Create_UnitEnemy>.MoveNext` или единожды в `battleStart` + после `EnemyLevelUp` | да | см. §5 про копирование |
| 8 | Шаг роста с волнами | статики `EnemyUnitVar_HpMaxRatioU/AttackRatioU/MoveSpeedRatioU` | **не менять**; либо множить приращения в списках `List_ApplyUnitEnemyStatRatio01..06` (+0x8b0..0x8d8) один раз в `battleStart` | риск искажения | аддитивное накопление (§4) — проще и надёжнее рычаги 1–3 |
| 9 | Состав врагов зоны | `Dictionary_AreaEnemyID[AreaChangeID]` | в `Define`/меню (AREAS.md §9) | да | ID только существующие в `UnitStat` |
| 10 | Базовые характеристики | таблица `UnitStat` (колонки HP/атака/скорость/размер/мана) | меню/до боя через `ModKit.Table`; спавн читает при каждом создании | да (откат при выключении) | один из самых чистых способов «×k на тип врага» (по ID), не нужен хук на горячем пути |
| 11 | Время зоны | `Explain[k]` условие журнала (AREAS.md §2) | меню | да | удлинение = больше шагов роста |
| 12 | Награды | `BounsPointRate` (+0x9cc, `int`), `Kill_RewardGold*` (Obscured) | не писать Obscured; `BounsPointRate` — только в `Start` | **риск ACTk** | множить награду за сложность — через эффект/множитель золота зоны (`Explain`), не прямой записью |
| 13 | Эффекты героя «против врагов» | эффекты 54 (HP), 55 (скорость), 33 (размер), 84 | `SetGlobalEffects`/`OnHeroPass` | да (путь, проверенный в «Проклятиях и дарах») | единственный метод, обновляемый и при `AbilityStatApply`; значения в % и ограничены игрой |
| 14 | Хард-режим | `Game_AreaLevel` = 1 | меню до `Button_AreaStart` | зависит | игровая сложность «Хард»: другое название зоны, декор, награды; формулы не разобраны |

**Что НЕ делать:** не писать в `Array_AbilityClassPractice`, `Array_AreaBestScoreNum`, `Array_JournalProgress`, золото/самоцветы/исследования (сохраняемые массивы и ObscuredInt с контрольной суммой, MELONLOADER.md/ROADMAP §2); не хукать `Unit_Damage`/`Update` врагов (сотни экземпляров × кадр); не менять `UnitApply_*` у живых врагов в `Update`.

## 8. Anti-cheat и сохранение [П]

- ACTk (`CodeStage.AntiCheat.ObscuredTypes`) используется для денег, времени выживания и исследований: **ObscuredFloat/Int читаются/пишутся через `op_Implicit`** (в `End_Battle` видны вызовы 0xfea174/0xfeb30c/0xfeb3f4). Прямая запись «сырых» байтов в такие поля ломает шифр/ключ и, вероятно, при включённом детекторе вызывает «cheat detected»/сброс [Г: общее поведение ACTk, на устройстве не проверялось]. Мод должен читать их только свойствами Il2Cpp (`ObscuredFloat` → неявное приведение) и **не** писать.
- Поля врагов (`C_Battle_EnemyUnit`), статики `Z_Database.EnemyUnitVar_*` и `AllyUnitVar_*`, поля `C_Battle_Manager` (кроме списка Obscured из §1) — обычные типы; контрольной суммы у них нет; в сохранение не попадают (в сейв идут только массивы практики/журнала/очков и списки `List_Book_*`, `Array_Int_AutoSaveGame` во время боя).
- Автосохранение боя (`StartAutoSave`, 0x10bd05c) пишет в `Array_Int_AutoSaveGame/Float_AutoSaveGame` текущие значения (в том числе `EnemyCreateDelay`, `EnemyCreateNum`, `WeatherNum`, список ультимейтов); при возобновлении боя (First_Fade_IN) значения восстанавливаются — поэтому множители, применённые **только** в `Start_Unit`, переживают восстановление без особых правок, а изменённые на лету поля менеджера восстановятся к сохранённым значениям.

## 9. Предлагаемый набор настроек и API

Идея: библиотека `difficulty` (мод-библиотека, `"library": true`) + мод-интерфейс, но **ядро не менять**; всё делается хуками из §7.

```csharp
public sealed class DifficultySettings
{
    // множители (1 = как в игре); диапазон ограничивается в Apply()
    public float EnemyHp = 1f;          // рычаг 1
    public float EnemyAttack = 1f;      // рычаг 2 (контактный + снарядный)
    public float EnemySpeed = 1f;       // рычаг 3 (0.5…2)
    public float EnemySize = 1f;        // рычаг 4 (0.7…1.5), по умолчанию 1 и не трогаем
    public float BossHp = 1f;           // рычаг 5
    public float BossAttack = 1f;
    public float SpawnCount = 1f;       // рычаг 6 (0.5…2)
    public float SpawnInterval = 1f;    // рычаг 7 (>1 — реже)
    public float ZoneTime = 1f;         // рычаг 11 (множитель времени журнала)
    public int[] ZoneEnemies = null;    // рычаг 9: 8 ID для текущей зоны (null — не трогать)
}
public interface IDifficulty
{
    DifficultySettings Current { get; }
    void Apply(DifficultySettings s);   // сразу пересоздаёт хуки/значения таблиц
    void Reset();                       // вернуть исходные ячейки UnitStat/Explain и списки зон
    event Action<int, float> EnemySpawned;   // unitId, финальный HP — для журнала
}
```

Реализация по хукам:
1. `Define`: найти `Il2Cpp.Z_Database.Dictionary_UnitStat`, запомнить исходные ячейки (откат при `Reset`).
2. `After C_Battle_EnemyUnit.Start_Unit(3)`: прочитать `UnitID` (+0x2f8), `boss = UnitID >= 41`; применить `EnemyHp`/`BossHp` к `UnitApply_HpMax`, `Unit_Hp = UnitApply_HpMax`; `EnemyAttack` — к `UnitApply_Attack`, `BUnitApply_Attack`, `ApplyBulletAttack`; `EnemySpeed` — к `UnitApply_MoveSpeed`/`BUnitApply_MoveSpeed`. Лёгкий код, без аллокаций, потолки (HP < 1e9).
3. `Before/After <Create_UnitEnemy>.MoveNext`: `EnemyCreateNum` и `ApplyEnemyCreateDelay` подменять и возвращать (как лимит снарядов в ACTIVE_SPELLS.md §6).
4. `After C_Battle_Manager.Start` (событие `battleStart`): применить `ZoneTime` к `Battle_ClearTime` (+0x928, `int`), записать `ZoneEnemies` в `Dictionary_AreaEnemyID[Game_AreaID]`; залогировать применённые значения.
5. Настройки в UI (`UiKit.OpenNumberForm`/`OpenChecklist`; пресеты «Лёгкий 0.7/0.7», «Обычный 1/1», «Хардкор 1.5/1.5/спавн 1.3», «Кошмар 2/2/спавн 1.5»), в журнал — одна строка с применёнными множителями.
6. Для боссов — отдельные множители, но только через `Start_Unit` и `UnitID`, без трогания `BossWave_*` (они ведут волну усиления).

## 10. Проверка без экрана (gamelog/autotest)

- `gamelog` уже печатает `Z_Database.EnemyUnitVar_*` и `AllyUnitVar_*`; для врагов нужен новый снимок: после `Start_Unit` записывать `UnitID`, `UnitBase_HpMax`, `UnitApply_HpMax`, `Unit_Hp`, `UnitApply_Attack`, `ApplyBulletAttack`, `UnitApply_MoveSpeed` для первых 20 врагов и для каждого босса — сверка с формулой §3.
- Тест «модификатор работает»: `python tools/autotest.py --mods difficulty --duration 180 --expect "…"` — условие по средней разнице `Unit_Hp/UnitApply_HpMax` (=1) и множителю относительно таблицы `UnitStat`.
- Критерии: (а) в логе строка `[difficulty] hp×… atk×… spd×… count×…`; (б) у первого рядового врага `UnitApply_HpMax ≈ round(UnitBase_HpMax × формула × k)`; (в) число живых врагов после 60 с ≈ ×k (счётчик по `enemySpawn − enemyDeath`); (г) нет вылетов и нет изменений `Array_*` сохранений.

## 11. Не подтверждено / что проверить

1. Точная формула скорости и размера (§3) — только структура; сверить `UnitBase_MoveSpeed` → `UnitApply_MoveSpeed` на устройстве.
2. Кто читает `EnemyCreateDelay` (+0x93c) кроме копирования в `ApplyEnemyCreateDelay`; расчёт внутрипачечных пауз (динамика по размеру пула).
3. Влияние `Game_AreaLevel == 1` («Хард») на формулы и награды; `AllyUnitVar_EnemyCreateDelayRatio` (+0xc00) — читатель.
4. Что именно возвращает `Unit_Attack` в урон героя (`UnitApply_Attack` × защита героя) — `Unit_Attack` 0x109b614 разбирался только по чтению полей.
5. Таблица `Dictionary_BattleUnitEnemy` (`Z_Database` +0x658): в `Start_Unit` она **не** используется (читается `Dictionary_UnitStat` +0x630); читатели не найдены (смещение 0x658 неоднозначно в скане) — в журнале пустая; по README заполняется в бою — проверить дампом в бою.
6. Формат имён вложенных классов для Harmony (`<Create_UnitEnemy>d__534`, `<EnemyLevelUp>d__548`).
7. Влияние масштабирования на статистику результата боя и достижения (`Game_KillMonsterNum`, `Game_SurvivalTime`): множители не должны менять эти счётчики, но «Хардкор» даёт больше убийств/мин — нужна проверка наград.
