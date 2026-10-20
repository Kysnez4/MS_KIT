# Локации (зоны): как игра хранит и меняет зоны — разведка D1

Статус: только чтение дампа (`dump.cs`, `tables/*.json`, журнал `g.txt`) и дизассемблера `libil2cpp.so` (Python + capstone, RVA из `dump.cs`; адрес в Ghidra = RVA + 0x100000). Ghidra-MCP не использовалась. Ничего не запускалось на телефоне, код ядра и модов не менялся.
Пометки: **[П]** подтверждено по дампу/дизассемблеру/таблице/журналу, **[Т]** взято из таблицы `tables/Explain.json` (данные игры), **[Г]** догадка, нужна проверка на устройстве.
Версия игры 612. RVA и смещения полей привязаны к ней.

## 1. Кратко (что нужно знать для следующих модов)

1. Зон в игре **14** (`Game_AreaID` 1..14). Три «большие» из вопроса: 12 «Виртуальная экосистема», 13 «Глубинная зона», 14 «Терра» [Т].
2. Названия, множитель золота, камни и **условия открытия лежат в таблице `Explain`** (`Z_Database.Dictionary_Explain`, статик +0x660), строки 1..14 [П][Т].
3. **Открытие зоны k+1 = выживание в зоне k нужное время** (в таблице «условие журнала», 720…1800 с), а не убийство босса. Игра пишет `Array_JournalProgress[k] = 1`, а `Game_AreaMaxNum` пересчитывается из этого массива при загрузке главного меню [П].
4. Список типов врагов зоны — `C_Battle_Manager.Dictionary_AreaEnemyID` (`Dictionary<int, List<int>>`, статик +0x28): **14 ключей × 8 ID**, заполняется в `.cctor` кодом, не из таблицы; в бою читается как `[AreaChangeID][1..7]` [П].
5. Фон (`SpriteList_Land[LandType]`), погода (`Play_Weather_AreaNN` по `LandType-1`), музыка (`BGMType` 1..3) выбираются в `Play_AreaChange` и в `C_Battle_Manager.Start` **по `LandType = AreaChangeID`**; в `SpriteList_Land` индекс берётся без проверки, кроме границы массива (выход за границу = исключение IL2CPP) [П].
6. Зоны 12–14 «составные»: во время боя `AreaChangeID` переключается по расписанию между подзонами 1..11, 12, 13, 14 (фон, враги, музыка меняются), расписание — в `Create_UnitEnemy_StartLoop` (§6) [П].
7. Событие мода `areaChange` (`Start_AreaChange`) **не срабатывает при обычном переходе подзон**: этот метод вызывается только из `V_Artifact_Manager.Create_Artifact` (путь «Обелиск», §6.1). Для логирования смены подзон нужны хуки на `Create_UnitEnemy_StartLoop` и `Play_AreaChange.MoveNext` [П].
8. Безопасно менять из мода: списки `Dictionary_AreaEnemyID` (до боя), ячейки `Explain` (время прохождения, множитель золота), `SpriteList_*` (через указатели), `WeatherName`/`LandType` после `Play_AreaChange`. Не трогать: `Array_JournalProgress`, `Array_AreaBestScoreNum`, золото/самоцветы (сохраняемые массивы с контрольной суммой, ObscuredInt) (§8).

## 2. Данные зон в таблице Explain [П][Т]

Строки 1..14 `Dictionary_Explain` (колонки `구역 이름`, `구역 골드 보상 배율`, `구역 보석 …`, `일지 구역ID`, `일지 조건 값A`, `일지 해금 구역`, `일지 보상 보석 개수`); строка 0 — заголовок со счётчиком `일지개수` = 14.

| Зона | Название | Золото, % | Время выживания для «журнала» (с) | Открывает | Журнал, самоцветы |
|---|---|---|---|---|---|
| 1 | Изолированная зона | 120 | 720 | 2 | 200 |
| 2 | Лаборатория | 150 | 720 | 3 | 300 |
| 3 | Канализация | 180 | 900 | 4 | 300 |
| 4 | Окраины | 210 | 900 | 5 | 300 |
| 5 | Дендрарий | 240 | 900 | 6 | 300 |
| 6 | Академия (эффект «Начальный уровень +10») | 270 | 900 | 7 | 300 |
| 7 | Электростанция | 300 | 1080 | 8 | 400 |
| 8 | Инкубатор | 330 | 1080 | 9 | 400 |
| 9 | Водохранилище | 360 | 1080 | 10 | 400 |
| 10 | Лес | 380 | 1080 | 11 | 400 |
| 11 | Город | 400 | 1080 | 12 | 400 |
| 12 | Виртуальная экосистема (в журнале условие «Граница») | 500 | 1800 | 13 | 500 |
| 13 | Глубинная зона | 400 | 1500 | 14 | 500 |
| 14 | Терра | 400 | 1500 | 15 (нет такой зоны) | 500 |

Названия в таблице русские (язык игры у пользователя); колонка языков такая же, как у других таблиц (`Game_SystemLanguage` +0x8bc).
Подтверждение по журналу `g.txt`: `Array_AreaBestScoreNum = [0,720,720,900,263,…]` (лучшие времена в секундах по зонам 1..4), `Array_JournalProgress = [0,1,1,1,0,…]`, `Game_AreaMaxNum = 4`, `Game_AreaID = 4`, `Game_LastAreaMaxNum = 1` [П].

## 3. Поля Z_Database и C_Battle_Manager, связанные с зонами

`Z_Database` (статики; смещения из `dump.cs`) [П]:

| Поле | Смещение | Смысл |
|---|---|---|
| `Game_AreaID` | 0x8c8 | выбранная зона 1..14 (меняют `Button_AreaMoveL/R`, читают `Start`, `End_Battle`, `Create_UnitEnemy_StartLoop`) |
| `Game_AreaLevel` | 0x8cc | сложность зоны: 0 обычная, 1 «Хард» (читают `Start`, `Create_UnitEnemy_StartLoop`, `End_Battle`, `Play_AreaChange`; в `Start` при 1 меняется `AreaLevelName` и фон/декор) |
| `Game_AreaMaxNum` | 0x8d0 | число открытых зон (1..14), **вычисляется**, см. §4 |
| `Game_LastAreaMaxNum` | 0x89c | значение при прошлом открытии меню (для плашки «Новая зона») |
| `Array_AreaBestScoreNum` | 0x80 | `int[30]`: лучшее время по зонам (индекс = зона), [29] — контрольная сумма; хард, вероятно, на индексах +14 [Г] |
| `Array_JournalProgress` | 0xa0 | `int[80]`: флаги журналов (индекс = номер журнала = зона) |
| `Dictionary_Explain` | 0x660 | таблица зон/журналов (§2) |
| `SpriteList_AreaPortrait` | 0x1a0 | картинки зон в меню |
| `SpriteList_Land` | 0x590 | основные фоны боя, индекс `LandType` |
| `SpriteList_LandDecoA..G` | 0x598..0x5c8 | декор фона (семь массивов) |
| `SoundList_BGM` / `LSoundList_BGM` | 0x158 / 0x118 | клипы музыки / громкости |
| `Array_EnemyBattleLocation` | 0x548 | массив положений врагов (смысл не выяснен [Г]) |
| `List_UnitEnemyDistribution` | 0x690 | **живой** список ID врагов для спавна (в журнале `[] → [1]` при старте) |

`C_Battle_Manager` [П]: статики `Dictionary_AreaEnemyID` (+0x28), `AreaChangeID` (+0x33c), `LastAreaChangeID` (+0x340), `AreaChangeNum` (+0x344), `BGMType` (+0x338), `AreaEnemyUnitVar` (+0x348), `Battle_ClearON` (+0x1d4), `ApplyMonsterClearCondition` (+0x2a4); поля экземпляра `LandType` (0xa50), `WeatherType` (0xa54, нигде не записывается кодом боя — мёртвое поле [Г]), `WeatherNum` (0x974; **не погода**, а номер события артефактов, §6.1), `WeatherName` (0x978, строка имени корутины погоды), `AreaLevelName` (0xa58), `AreaChangeFirstON` (0xa60), `Area_SpecialVal01..10` (0xa64..0xa88), `IsRushON` (0xa8c), `Battle_ClearTime` (0x928, секунды до «журнала»), `List_UnitEnemyStatRatio` (0x8a8) и `List_ApplyUnitEnemyStatRatio01..06` (0x8b0..0x8d8), `SpriteRenderer_Weather/_Plus/_Back` (0x818/0x820/0x828).
Методы: `Start` 0x109e444, `Start_AreaChange` 0x10b4008, `Play_AreaChange` 0x10b4054 (MoveNext 0x10f3760), `Play_AreaOpen_NameEffect` 0x10b40bc (MoveNext 0x10f4f6c), `Create_UnitEnemy_StartLoop` 0x10b0fe0, `Play_Weather_Area01..14` 0x10b08e0..0x10b0da8, `Play_WeatherObj_RainA/B` 0x10b0e00/0x10b0e68, `EnemyLevelUp` (MoveNext 0x10d11b0), `End_Battle` 0x10b4124, `.cctor` 0x10bef04.

## 4. Как открываются зоны [П]

1. **В бою** `C_Battle_Manager.Start` (0x109e444) читает `Battle_ClearTime = Dictionary_Explain[Game_AreaID][колонка условия]` (запись в поле +0x928 на 0x10a123c; какая именно колонка — по значениям 720/900/… это `일지 조건 값A` [Г]).
2. **Конец боя**, `End_Battle` (0x10b4124, блок 0x10b8d90..0x10b9120): если `Battle_ClearON == 1` (иначе сбрасывается в 0, когда `Game_SurvivalTime` (ObscuredFloat, статик +0x918) меньше порога), игра идёт по журналам `j = 1..14`; если `Explain[j].«зона журнала» == Game_AreaID` и `Array_JournalProgress[j] == 0` — пишет `Array_JournalProgress[j] = 1` и добавляет `j` в `List_ApplyJournalID` (показ экрана журнала `Fade_Journal`). То есть **приз — журнал за выживание**; связь с боссом косвенная: `Create_UnitEnemyBoss` вызывается из поздних шагов `EnemyLevelUp` (0x10e044c и 0x10e05cc) [П], но флаг журнала от убийства босса не зависит.
3. **Главное меню** `A_MainMenuInformation.MainMenuInformation` (0x1011b30, блок 0x1015a90..0x1015f14): `Game_AreaMaxNum = 1` (и `Game_AreaID = 1`, если был 0), затем 13 сравнений: `Array_JournalProgress[k] >= 1` → `Game_AreaMaxNum = k+1`, `k = 1..13`. Это **не накопительное**: итог = (наибольший `k` с прогрессом) + 1 [П].
4. Стрелки меню: `Button_AreaMoveL/R` (0x1023728/0x1023820) просто делают `Game_AreaID ∓ 1` и вызывают `Area_Information(id)` (0x101a7f4), где **жёстко зашит максимум 14** (`cmp …, #0xe`) и границы по `Game_AreaMaxNum` (чтение 0x8d0 на 0x101b420). Кнопки старта: `Button_AreaStart` 0x1021268, `Button_AreaStart_Loop` 0x1023190, `Button_AreaSpecialSheet_Start` 0x1023444 — все пишут `Game_BattleClassID` и вызывают `V_Ability_Manager.Start_Ability` [П].
5. Прочее: `Game_AreaMaxNum >= 12` включает **несколько ультимейтов** (`Button_MagicCom_Decision`, см. ULTIMATES.md) — открытие зоны 11→12 влияет не только на карту.

**Что это значит для мода:** чтобы «открыть все зоны» без прав на сохранение, достаточно в главном меню после `MainMenuInformation` записать `Z_Database.Game_AreaMaxNum = 14` (обычный `int`, не Obscured, не сохраняется — пересчитывается при следующем входе в меню). `Array_JournalProgress` писать нельзя (сохраняемый массив: сейв не трогаем). Побочный эффект такого трюка: плашка «Новая зона» (`Game_LastAreaMaxNum`) и условие ультимейтов (≥ 12) сработают как при реальном открытии [Г по плашке].

## 5. Старт боя: что `C_Battle_Manager.Start` выбирает по зоне [П]

`Start` (≈70 КБ кода) делает **два больших `switch` по `Game_AreaID` (1..14; значения вне диапазона пропускают настройку)**:
1. Первый (jump table 0x653740, переход на 0x10a534c…): `WeatherNum = 0`, читает строку `Explain[AreaID]` (множитель золота +100 → `BounsPointRate` 0x9cc), `Game_BattleLevel = 0`, `ApplyMonsterClearCondition = 0x320` (800), `EnemyCreateDelay` (0x93c) = стартовая константа зоны (например 0.75 с), заводит `List<float>` и набивает его константами — **списки множителей характеристик врагов по шагам времени** (первый элемент у зоны 1 — 0.3 / 1.0…); часть зон сразу пишет `EnemyCreateNum_Min` (0x934).
2. Второй (≈0x10a8000..0x10a8400): создаёт `List_UnitEnemyStatRatio` (0x8a8) своими константами для каждой зоны, `AreaChangeFirstON = 1`, **`LandType = AreaID`** (зоны 1..11; в зонах 12–14 `LandType` = 11 или значение из автосохранения). Затем `EnemyCreateDelay → ApplyEnemyCreateDelay` (0x93c → 0x940).
3. Музыка: `BGMType` ∈ {1,2,3} по таблице от `AreaChangeID` (0x10a8438; jump table 0x65375c): подзоны 1,4,5,10,11,12,13,14 → 1; 2,6,8 → 2; 3,7,9 → 3 [П]. Затем `V_SoundManager.PlaySound(…, BGMType)` (0x10aa898).
4. Фон: `SpriteRenderer.set_sprite(SpriteList_Land[LandType])` (0x10a95d8; индекс с проверкой границы, при выходе `IndexOutOfRange` — нативное исключение, игра виснет/падает). Декор — `SpriteList_LandDeco*[LandType + смещение]` в `Play_AreaChange` (0x10f4afc).
5. `AreaChangeID = LastAreaChangeID = Game_AreaID`, `AreaChangeNum = 0`, `IsRushON = false`, `AreaEnemyUnitVar = 0` (0x10a1150..0x10a1184) [П].
6. Для зон 12–14 (и режима автосохранения, флаг `Z_Database +0x83c`) `Area_SpecialVal01..10` либо восстанавливаются из `Array_Int_AutoSaveGame[69..78]`, либо **заново рандомизируются**: строится список подзон `[1,2,3,4,…]`, из него `Random.Range`+`RemoveAt` выбирают перестановку; `Area_SpecialVal01` = 1 (или константа) [П по структуре, значения не расшифрованы].

Для всех зон в гейм-логе при старте виден набор «множителей врагов по зоне»: `EnemyUnitVar_AttackRatioU 0→4`, `HpMaxRatioU 0→10`, `MoveSpeedRatioU 0→2` (зона 4) — подробнее в DIFFICULTY.md.

## 6. Смена зоны/подзоны в бою

### 6.1. `Start_AreaChange` и событие `areaChange` [П]
`Start_AreaChange` (0x10b4008) = `StartCoroutine("Play_AreaChange")`. Единственные вызовы — три места в `V_Artifact_Manager.Create_Artifact` (0x1126594, 0x11266a0, 0x11267a0), условие `WeatherNum == 9/10/11` (там же `AreaChangeID = Area_SpecialVal02`, затем текст из `Dictionary_Name`). По именам методов рядом (`Button_Obelisk_Change_Decision`) и названиям артефактов («Обелиск», «Сломанный обелиск») — это **артефакт «Обелиск»: игрок меняет локацию посреди боя** [Г по смыслу, П по коду]. Значит событие ядра `areaChange` (`Hooks`: `Start_AreaChange`, `Leave`) срабатывает **только** в этом сценарии. Первая настройка зоны при старте боя идёт через `StartCoroutine("Play_AreaChange")` строкой изнутри `Start` (0x10af728) — отдельного события нет.

### 6.2. `Play_AreaChange` (MoveNext 0x10f3760) [П]
1. Состояние 0: обнуляет статики менеджера `StartRest` (+0x32c) и `ApplyBossGround_Size` (+0x2b0), останавливает одну корутину по имени, скрывает `SpriteRenderer_BossLimitGround` (+0x810) и объект `[+0x148]`; если `AreaChangeFirstON == 1` — перезапускает по имени две корутины (предположительно `Play_WeatherObj_RainA/B`, имена — литералы 0x250e058/0x250e060 [Г]).
2. Выбор **`LandType`**: если `static+0x330 == 1 && LastAreaChangeID >= 1` → `LandType = LastAreaChangeID`, иначе `LandType = AreaChangeID` (0x10f3d88..0x10f3df4). Для `LandType == 1` берётся `Area_SpecialVal01`, `BGMType = 1` и играется музыка (0x10f3e58).
3. **Погода:** `WeatherName = ""`; если `LandType-1 <= 13` — `WeatherName = таблица[LandType-1]` (указатели на строки по 0x23ef768 — имена `Play_Weather_Area01..14`); затем **`StopCoroutine` по именам для всех 14 корутин погоды** и `StartCoroutine(WeatherName)`, если строка не пустая (0x10f3e64..0x10f3ff8). Значит: погода = f(`LandType`), состав `Play_Weather_AreaNN` (RVA §3) жёстко завязан на 14 значений.
4. Фон: `SpriteRenderer.set_sprite` из декор-массивов (индексы `LandType + [sp+0x18]`), повороты `Transform.set_rotation` на 4 объектах, `Random.Range` для положения декора, затем `StartAutoSave` (0x10f3d5c), `Play_AreaOpen_NameEffect` (название зоны на экране: `Dictionary_Name` строка + `Text.set`) — запускает сама `Play_AreaChange` (0x10f4ce8).

### 6.3. Составные зоны 12–14: `Create_UnitEnemy_StartLoop` (0x10b0fe0) [П]
Метод вызывается из `Play_EnenmyClearActive` (корутина героя, 0x1069eb4; «зачистка врагов»; перед этим вызывает `Create_UnitEnemy_Stop`) и из `Play_Unit_Death` (0x10c58f0, остановка). Делает: `AreaChangeNum++`, `LastAreaChangeID = AreaChangeID`, затем **новый `AreaChangeID`** по расписанию (индекс = `AreaChangeNum`, после 24 — `AreaChangeNum % 5`) и перезапускает спавн `StartCoroutine("Create_UnitEnemy")`. Расписание (расшифровано из jump-таблиц; `SpNN` = `Area_SpecialValNN`, т.е. случайная подзона 1..11 для этого забега):

| `Game_AreaID` | AreaChangeNum 0..24 → `AreaChangeID` | после 24: `%5` |
|---|---|---|
| 12 «Виртуальная экосистема» | `[AreaChangeNum % 5]`: 1, Sp02, Sp03, Sp04, Sp05 | то же |
| 13 «Глубинная зона» | 1, Sp02, Sp03, Sp04, 12, Sp05, Sp06, Sp07, 12, 13, Sp08, Sp09, Sp10, 12, 13, Sp02, Sp03, Sp04, 12, 13, Sp05, Sp06, Sp07, 12, 13 | Sp08, Sp09, Sp10, 12, 13 |
| 14 «Терра» | Sp02, Sp03, Sp04, (не расшифр.), (не расшифр.), Sp05, Sp06, Sp07, 13, 14, Sp08, Sp09, Sp10, 13, 14, Sp02, Sp03, Sp04, 13, 14, Sp05, Sp06, Sp07, 13, 14 | Sp08, Sp09, Sp10, 13, 14 |

Смысл: в зонах 12–14 ключ `AreaChangeID` вместо номера самой зоны бежит по подзонам (1..11 — враги/фон/музыка обычных зон, 12/13/14 — «финальные» наборы врагов). Именно поэтому `Dictionary_AreaEnemyID` нужен с ключами до 14, а `Play_Weather_Area14`, `SpriteList_Land[14]` и т. п. существуют. У зон 1..11 `AreaChangeID` остаётся равным номеру зоны. На 0x10b13c4..0x10b14a0 видны чтения `Area_SpecialVal` по смещениям 0xa64..0xa88 [П].

## 7. Dictionary_AreaEnemyID [П]

`.cctor` `C_Battle_Manager` (0x10bef04, запись в статик на 0x10c1e4c) строит `Dictionary<int,List<int>>`. Восстановлено разбором инструкций (ID врагов — строки `UnitStat`, см. DIFFICULTY.md §2):

| Ключ (AreaChangeID) | [0] | [1] | [2] | [3] | [4] | [5] | [6] | [7] |
|---|---|---|---|---|---|---|---|---|
| 1 | 1 | 6 | 11 | 13 | 16 | 31 | 21 | 22 |
| 2 | 1 | 7 | 11 | 13 | 18 | 31 | 21 | 27 |
| 3 | 1 | 7 | 32 | 16 | 18 | 32 | 14 | 19 |
| 4 | 1 | 6 | 11 | 17 | 16 | 8 | 19 | 24 |
| 5 | 1 | 7 | 12 | 13 | 17 | 14 | 23 | 27 |
| 6 | 1 | 13 | 18 | 17 | 19 | 31 | 26 | 24 |
| 7 | 1 | 7 | 12 | 16 | 7 | 21 | 22 | 28 |
| 8 | 1 | 6 | 11 | 16 | 21 | 18 | 23 | 29 |
| 9 | 1 | 7 | 12 | 16 | 31 | 21 | 22 | 27 |
| 10 | 1 | 7 | 17 | 13 | 18 | 17 | 19 | 22 |
| 11 | 1 | 6 | 11 | 17 | 8 | 19 | 23 | 24 |
| 12 | 1 | 31 | 21 | 22 | 28 | 27 | 34 | 33 |
| 13 | 1 | 8 | 34 | 23 | 27 | 34 | 29 | 35 |
| 14 | 1 | 35 | 36 | 37 | 38 | 0 | 39 | 40 |

(Значения `0` — «нет юнита на этом шаге».) Роль [П по `EnemyLevelUp`]: на шагах волн (77 записей `EnemyCreateDelay`) `EnemyLevelUp` берёт `Dictionary_AreaEnemyID[AreaChangeID]` (0x10d2170: `bl 0x14707f0` с ключом `static+0x33c`) и копирует элементы `[1..7]` по возрастанию в `Z_Database.List_UnitEnemyDistribution` (`List<int>.set_Item`); `[0]` = 1 всегда («Рой (малый)»). Индексы 1..7 встречаются в блоках `EnemyLevelUp` по шагам; точные пороги времени по каждому шагу не извлечены [Г]. Сам список спавна `Create_UnitEnemy` выбирает случайным элементом (`Random.Range` + `List.get_Item`, 0x10cfb10..0x10cfb20) и вызывает `C_Battle_EnemyUnit.Start_Unit` (0x1094d4c).

## 8. Жёстко заданные «под число зон» места [П]

| Место | Что зашито |
|---|---|
| `Dictionary_AreaEnemyID` | ключи 1..14 (код `.cctor`) |
| `Start` | оба `switch` по `Game_AreaID` — 14 значений (`sub 1; cmp #0xd`); вне диапазона настройка пропускается (старт без `List_UnitEnemyStatRatio` → `EnemyLevelUp` читает пустой список → исключение) |
| `Create_UnitEnemy_StartLoop` | особая логика только для `AreaID` 12, 13, 14 |
| `Play_AreaChange` | таблица погоды на 14 имён, индекс `LandType-1`; вне — без погоды (безопасно) |
| `Start`, `Play_AreaChange` | `SpriteList_Land[LandType]`, `SpriteList_LandDeco*[…]` — прямой индекс с проверкой границы массива |
| `BGMType` | таблица `AreaChangeID → {1,2,3}` на 14 значений; клип по `SoundList_BGM[BGMType]` |
| Главное меню | `Area_Information`: максимум 14 (`cmp #0xe`); `MainMenuInformation`: пересчёт `Game_AreaMaxNum` по `JournalProgress[1..13]`; 14 плиток/картинок `SpriteList_AreaPortrait` |
| Таблица `Explain` | 14 строк зон и 14 журналов (+ пустая строка 15) |
| Сохранение | `Array_AreaBestScoreNum` — `int[30]` (индексы 1..14 и, вероятно, хард 15..28; [29] — контрольная сумма), `Array_JournalProgress` — `int[80]` |
| Платформа | `GPGSIds`: 13 таблиц лидеров (reservoir, power_plant, outskirt, woodland, array_zone, virtual_ecosystem, depths, arboretum, sewerage, academy, city, laboratory, incubation_room) — порядок не совпадает с номером зоны, соответствие — [Г] |

Следствие: **добавить 15-ю зону** — это правка десятка независимых мест (в том числе нативных `switch`, которые из C# не изменить). Реалистичны только: подмена содержимого существующих зон и «ремиксы» (комбинации фона/врагов/погоды/музыки зон 1..14).

## 9. Что безопасно менять из мода

| Что | Как (поле/метод) | Когда | Риск / замечания |
|---|---|---|---|
| Список врагов зоны | `C_Battle_Manager.Dictionary_AreaEnemyID[ключ]` — `List<int>`: `list[i] = id` (для i 1..7); только ID, существующие в `UnitStat` (1,6,7,8,11–14,16–19,21–24,26–29,31–40; боссы 41–46 как рядовых — огромное HP) | один раз при включении мода и при выходе в меню; не во время боя | статическое, не сохраняется; чужой ID без картинки/движения может быть `null`-спрайтом [Г] — сначала использовать ID из соседних зон. Откат: вернуть исходный список |
| Время «журнала» зоны | ячейка `Dictionary_Explain[k]` колонка условия (`ModKit.Table`): уменьшить 720 → 120 для теста | в меню до боя | меняет только порог прохождения; `Array_JournalProgress` игра пишет сама. Откат при выключении мода |
| Множитель золота зоны | ячейка `구역 골드 보상 배율` в `Explain` | в меню | влияет на награду; `Game_Money_Gold` (ObscuredInt) мод напрямую не пишет |
| Открытые зоны | `Z_Database.Game_AreaMaxNum` (int) | после `MainMenuInformation` (хук «после») | см. §4; не сохраняется |
| Погода | после `Play_AreaChange`: `WeatherName`, остановить все `Play_Weather_AreaNN`, `StartCoroutine("Play_Weather_AreaMM")` | сразу после `battleStart` или после смены подзоны (подождать кадр, пока `Play_AreaChange` стартует погоду) | погоду рисуют `SpriteRenderer_Weather*` — сменить имя корутины достаточно; проверить, что старая остановлена |
| Фон | `SpriteList_Land[LandType]` (подменить спрайт в массиве; либо вызвать `SpriteRenderer.set_sprite` у объекта `[manager+0x4f0]`), рост массива — через `Sprites.Ensure` (как `AddArtifact`) | в меню / до боя | **не** писать `LandType` вне 0..Length-1; ссылка на спрайты — через указатели (`UiKit.GameSprite`/`Raw`), обёртка `Sprite` из Il2CppInterop опасна под Mono (MELONLOADER.md «Грабли») |
| Музыка | `C_Battle_Manager.BGMType` (1..3, статик) до вызова `PlaySound`; либо `Audio_BGM` | после `Start`/`Play_AreaChange` | индексы 1..3 в `SoundList_BGM`; новые клипы — отдельная работа (OpenSL, см. matador_boss) |
| Подзона | `AreaChangeID` (статик) перед `Play_AreaChange`/`Create_UnitEnemy_StartLoop` | в бою | значение 1..14, иначе выход за массивы |
| Режим «Хард» | `Game_AreaLevel` (0/1) до `Start` | в меню | влияет на `End_Battle` (награды), список/названия; полная семантика не расшифрована |

**Нельзя/опасно:** `Array_AreaBestScoreNum` и `Array_JournalProgress` (сохраняемые), `Game_Money_*`, `Game_SurvivalTime`, `Game_ResearchPoint` (ObscuredInt/Float и контрольные суммы); значения `AreaID`/`LandType` вне 1..14; рост `Dictionary_AreaEnemyID` ключом 15 без правки кода `Start`.

## 10. Предлагаемые хуки

Имена классов — как принимает `Hooks.FindMethod` (вложенные итераторы `C_Battle_Manager+_Play_AreaChange_d__550`.`MoveNext` — формат подтверждён только для `C_Battle_AllyUnit+_Play_Fireball_d__1134` в MELONLOADER.md §16, **для остальных класс надо проверить**; возможен вариант `Assembly-CSharp:<Play_AreaChange>d__550`).

| Хук | Зачем | Тип |
|---|---|---|
| `A_MainMenuInformation.MainMenuInformation` (после, 0 пар.) | прочитать/переписать `Game_AreaMaxNum`; залогировать `Game_AreaID`, `Array_JournalProgress[1..14]`, `Array_AreaBestScoreNum[1..14]` | лёгкий |
| `A_MainMenuInformation.Area_Information(int)` (до/после) | отследить выбор зоны | лёгкий |
| `C_Battle_Manager.Start` (после; уже есть событие `battleStart`) | снимок: `Game_AreaID/Level`, `AreaChangeID`, `LandType`, `BGMType`, `Battle_ClearTime`, длины `SpriteList_Land/LandDeco*/SoundList_BGM/AreaPortrait`, содержимое `Dictionary_AreaEnemyID` | лёгкий |
| `C_Battle_Manager.Create_UnitEnemy_StartLoop` (до и после) | смена подзоны: `AreaChangeNum`, `LastAreaChangeID → AreaChangeID` | редкий |
| `C_Battle_Manager+_Play_AreaChange_d__550.MoveNext` (после) | факт запуска (вызывается каждый шаг итератора — ограничить числом/состоянием `<>1__state`) | **горячий** — только ограниченное логирование |
| `C_Battle_Manager.Start_AreaChange` (событие `areaChange`) | сценарий «Обелиск» | редкий |
| `C_Battle_Manager.Create_UnitEnemyBoss(int,int)` (событие `bossSpawn`) | момент и ID босса | редкий |
| `C_Battle_Manager.End_Battle` (до; событие `battleEnd`) | значения `Battle_ClearON`, `Game_SurvivalTime` (читать только через `Obscured`-оператор/свойство), `Array_JournalProgress` до/после (в `battleEnd`-постфиксе разности нет — снимать после `End_Battle`) | редкий |
| `C_Battle_EnemyUnit.Start_Unit(int,float,float)` (событие `enemySpawn`) | подсчёт `UnitID` по зонам (проверка `Dictionary_AreaEnemyID`) | **горячий** (сотни вызовов) — только счётчик |

## 11. Пробный мод `area_probe` (описание, без сборки)

Цель: на устройстве подтвердить §4–§7 и узнать недостающие размеры массивов без риска (только чтение + лог в `modkit.log`).
Структура: `modkit/melon/examples/area_probe` (zip-мод `IKitMod`, без зависимостей, ничего не пишет в игру).
- **Меню:** `ctx.After("A_MainMenuInformation", "MainMenuInformation", 0, …)` → строки `[area_probe] menu: Game_AreaID=… AreaMaxNum=… LastAreaMaxNum=… Level=…`, `JournalProgress[1..14]`, `AreaBestScore[1..28]`, `Explain` строки 1..14 (название, время журнала, зона, куда ведёт) через `Table("Explain")`.
- **Бой:** по событию `battleStart`: `AreaChangeID/LastAreaChangeID/AreaChangeNum`, `LandType`, `BGMType`, `Battle_ClearTime`, `WeatherName`, `Area_SpecialVal01..10`, `Game_AreaLevel`; длины `SpriteList_Land`, `SpriteList_LandDeco{A..G}`, `SoundList_BGM`, `SpriteList_AreaPortrait`, `Array_EnemyBattleLocation`; **дамп `Dictionary_AreaEnemyID`** (14 строк, сверить с §7); `List_UnitEnemyDistribution` каждые 30 с (первые 10 минут) — увидеть, как список растёт по `[1..7]`.
- **Смена подзон:** `Before/After` на `Create_UnitEnemy_StartLoop`: строка `[area_probe] StartLoop: num=… id X→Y land=… t=…` (время — `Stopwatch`, не `Time`). `After` на `Start_AreaChange` — отдельная строка «Обелиск». На `Create_UnitEnemyBoss` — `unitId`, `count`, время.
- **Конец боя:** `After End_Battle`: `Battle_ClearON`, `JournalProgress[1..14]` (чтобы поймать момент записи журнала), `AreaMaxNum` (останется прежним до меню — ожидаемо).
- **Первый пробный тест:** играть зону 1 (`Battle_ClearTime = 720` — 12 мин); ожидается одна смена только если бить «Обелиск»; для зоны 12 — серия `StartLoop` с чередованием `1, Sp02…`.
- **Ограничения безопасности:** не хукать `MoveNext` итераторов без крайней нужды (Scudo-сбои массовых хуков, MELONLOADER.md §13); лимит строк лога на хук (например 200); `try/catch` вокруг чтения статиков (`Obscured*` читать через свойства-операторы, не через `ToString()`).
- **Критерий успеха:** в логе есть таблица §7 (14 списков) и значения `Game_AreaMaxNum`, совпадающие с `JournalProgress` (`k+1`), плюс длины `SpriteList_Land` и `SoundList_BGM`.
- **Следующий мод `area_tweaks` (D2):** настройки «врагов зоны», «погода» (список имён корутин), «время выживания», «открыть все зоны (до 14)».

## 12. Не подтверждено / что проверить

1. Какая именно колонка `Explain` кладётся в `Battle_ClearTime` (0x10a1234, ключ — литерал 0x250d458) — [Г] `일지 조건 값A`.
2. Что вызывает сброс `Battle_ClearON`/установку в 1 в бою (`Game_ClearON` у героя, 0x1044…) и порог по `Game_SurvivalTime`.
3. Назначение `WeatherNum` (события 9/10/11 артефактов), `Game_AreaLevel == 1` (хард) во всех ветках, `Array_AreaBestScoreNum[15..28]`.
4. Точные пороги времени в `EnemyLevelUp` (77 шагов) и привязка `[1..7]` к шагам.
5. Длины `SpriteList_Land`, `SpriteList_LandDeco*`, `SoundList_BGM` — читаются `area_probe`.
6. Две ячейки расписания зоны 14 (`AreaChangeNum` 3 и 4) не расшифрованы.
7. Формат имени вложенного класса для Harmony-хуков на `<Play_AreaChange>d__550` — проверить в `Hooks.FindType`.
