# Ультимейты (ультимативные умения) — разведка B2

Статус: чтение `dump.cs`, `tables/MagicCom.json` и дизассемблера `libil2cpp.so` (Python + capstone; RVA из `dump.cs`; адрес в Ghidra = RVA + 0x100000). Ghidra-MCP не использовалась, ничего не запускалось на устройстве, код ядра/модов не менялся.
Пометки: **[П]** подтверждено по дампу/ассемблеру/таблице, **[Г]** догадка, нужна проверка на устройстве. Версия игры 612.

## 1. Краткий вывод

1. **Ультимейт — не активное умение.** У него нет кнопки, перезарядки и «запуска»: это **постоянный режим усиленной комбинации заклинаний**, который включается тем, что ID комбинации лежит в списке `Z_Database.List_ApplyUltimateON` (5 слотов). Дальше «код ультимейта» — проверки `List_ApplyUltimateON.Contains(ID)` внутри `Play_*`-корутин заклинаний, `StartInformation_B` и снарядов [П].
2. **ID ультимейта = ID строки таблицы `MagicCom`** (комбинация заклинаний, 1..63): колонки `궁극기 이름` (название ультимейта), `궁극기 조건`, `궁극기 설명줄01` [П][таблица].
3. Выбор ультимейта: `V_Ability_Manager.Button_Ultimate_Decision` (RVA 0x1116ae0) → кладёт `Ultimate_ApplyID` в первый свободный слот `List_ApplyUltimateON`, добавляет ID в `List_Book_Ultimate` (энциклопедия — **сохраняется в сейв**), вызывает `AbilityStatApply(2)`, который в конце запускает `StartInformation_A → StartInformation_B` героя, а тот поднимает нужные корутины [П].
4. **Запустить «по кнопке из мода»** нельзя в смысле «разовый каст». Можно: (А) **включить/выключить** ультимейт записью в `List_ApplyUltimateON` + `AbilityStatApply(2)`; (Б) для 6 ультимейтов с собственной корутиной (`Play_Ultimate03/07/37/38/43/55`) — вызвать итератор вручную, как «ручной залп» заклинаний (ACTIVE_SPELLS.md §4); (В) вызвать `Button_Ultimate_Decision` — не рекомендуется (побочные эффекты, §4).
5. Узнать выбранный ультимейт: читать `Z_Database.List_ApplyUltimateON` (статик +0x790, `List<int>`, 5 элементов, 0 = пусто); в бою его же сохраняет автосохранение (`StartAutoSave`) [П].

## 2. Таблица ультимейтов (MagicCom) [П][таблица]

Колонка «условие» — значение `궁극기 조건`; по коду (§3) оно сравнивается и с `Game_BattleClassID`, и с `Game_ApplyExperimentID` — то есть это ID школы/подопытного (1..24, у «Галлюцинации» 25) [П по коду, Г по смыслу колонки]. «Код» = ID встречается в проверках `List_ApplyUltimateON.Contains(…)` (§5): да/нет.

| ID | Комбинация | Ультимейт | Усл. (школа) | Код |
|---|---|---|---|---|
| 1 | Гнев небес | Штормбрингер | 4 Шаман | да |
| 2 | Парящие мечи | Аура Блейд | 7 Призыватель | нет (ID не найден — проверить на устройстве) |
| 3 | Световая стрельба | Темпестер | 13 Алхимик | да (+ `Play_Ultimate03`) |
| 4 | Поток Теры | 아칸 (корейский заглушка) | 0 | нет |
| 5 | Военный климат | Граунд Зеро | 5 Чернокнижник | да |
| 6 | Уравнение демона | Немезис | 11 Пиромант | да |
| 7 | Безумие | Берсерк | 22 Боевой маг | да (+ `Play_Ultimate07`) |
| 9 | Противодействие | 오버차지 (заглушка) | 0 | нет |
| 10 | Вихрь | Тьма | 10 Друид | да |
| 12 | Поджог эфира | 아마테라스 (заглушка) | 0 | нет |
| 13 | Орбитальная бомбардировка | 익스터미네이션 (заглушка) | 0 | нет |
| 14 | Генерация нейтронов | Омега | 10 Друид | да |
| 16 | Плазменный луч | Квантовый лазер | 9 Мистик | да |
| 17 | Взрыв молний | Дес Шторм | 4 Шаман | да |
| 19 | Инферно | Инквизитор | 23 Полководец | да |
| 20 | Отладка молний | 템페스터 (заглушка) | 0 | нет |
| 23 | Потоп | Мега-цунами | 17 Заклинатель | да |
| 24 | Снежное цветение | Нулевая нова | 3 Криомант | да |
| 29 | Отправной взрыв | Блиц | 13 Алхимик | да |
| 32 | Гиперион | Абаддон | 2 Астроном | да (спутники, `Start_Satellite_Ultimate32`) |
| 33 | Аватар | Эквилибриум | 1 Чародей | да |
| 34 | Сердце грозы | Арка | 12 Колдун | да |
| 35 | Великое столкновение | Гигантский удар | 5 Чернокнижник | да |
| 37 | Эффект Мейснера | Локдаун | 16 Электромант | да (+ `Play_Ultimate37`, `Start_Ultimate37`) |
| 38 | Энергетическая насыщенность | Бастер Бим | 9 Мистик | да (+ `Play_Ultimate38`) |
| 40 | Телепорт | Галлюцинация | 25 (нет такой школы) | да |
| 41 | Пламенный шторм | 볼케이노 (заглушка) | 0 | нет |
| 43 | Магическое копье | Валькирия | 7 Призыватель | да (+ `Play_Ultimate43`) |
| 44 | Ворота | Асенсия | 22 Боевой маг | да (`Play_FlashShock_Com02`) |
| 45 | Генерация магнитного поля | Ядро | 2 Астроном | да |
| 50 | Чёрная дыра | 이벤트 호라이즌 (заглушка) | 0 | нет |
| 51 | Бытие | Космос | 21 Маг | да |
| 55 | Адское пламя | Бримстоун | 23 Полководец | да (+ `Play_Ultimate55`) |
| 56 | Эра Солнца | Оверлорд | 15 Ведьма | да |
| 57 | Разрушитель | Терминатор | 16 Электромант | да |

Остальные 29 строк `MagicCom` — комбинации без ультимейта (`궁극기 이름` = «`»). 7 ультимейтов имеют корейское имя-заглушку и условие 0 (их, видимо, нельзя получить) [Г]. Для названий школ см. CLASSES.md (ID школ 1..24 → имена).
Условие по таблице: игрок выбирает **ту школу и ту «подопытную» версию**, что названы в условии [Г по комбинации двух сравнений].

## 3. Как ультимейт становится доступным [П]

`V_Ability_Manager.Button_MagicCom_Decision` (RVA 0x1115834, ≈2,5 КБ; блок 0x1115a08..0x1115e68):
1. Пересоздаёт статический `V_Ability_Manager.List_ApplyUltimatePossible` (`List<int>`, статик +0x0): `Count = 0`.
2. Проверка слотов: **`Game_AreaMaxNum >= 12` ИЛИ `List_ApplyUltimateON[0] == 0`** (0x1115a54..0x1115a90). То есть первый ультимейт можно взять всегда, **остальные (до 5 слотов) — только когда открыта зона 12** («Виртуальная экосистема», см. AREAS.md). Если условие не выполнено, список возможных остаётся пустым.
3. Цикл по строкам `Dictionary_MagicCom` (`k = 1..Count`, число комбинаций читается из ячейки `기술 개수` нулевой строки = 63): если `cond(k) >= 1` и (`List_AllyAbilityLearn` содержит `10001 + (k-1)` — комбинация изучена — **или** `k == MagicCom_ApplyID` (+0x2d8, только что выбранная комбинация)) и `Game_BattleClassID == cond(k)` и `Game_ApplyExperimentID == cond(k)` → в `List_ApplyUltimatePossible` добавляется `k` (0x1115d4c..0x1115e68).
4. Затем показывается окно выбора (`Button_Ultimate_Inf` 0x1116494, `Button_Ultimate_MoveL/MoveR` 0x1116948/0x1116a14, `Button_Ultimate_IN` 0x1116f98, корутины `Play_UltimateStartA/B` 0x1117034/0x111709c).
В тот же момент `Z_Database.AllyUnitGlobal_Level` (+0x9d8) увеличивается на 1 и, при наборе порога, сбрасывается шкала маны (`AllyUnitGlobal_ManaCost/ManaStock` +0x9e4/+0x9e8): выбор комбинации/ультимейта игра засчитывает как «уровень» [П по инструкциям, Г по смыслу].

## 4. Выбор ультимейта — `Button_Ultimate_Decision` (0x1116ae0) [П]

Последовательность:
1. `Z_Database.AllyUnitGlobal_Level` (+0x9d8) += 1; при `Game_BattleClassID == 19` (Археолог) на кратных уровнях `CreateItem_Character(5, 1)` (0x1116c58); подсветка шкалы маны (`Img_ManaBar_Front`, спрайты Normal/Special/Ultimate/Max) и, при полной, сброс `AllyUnitGlobal_ManaCost/Stock` (0x1116d9c).
2. **Запись слота:** цикл `i = 0..List_ApplyUltimateON.Count-1`: берёт первый элемент, равный 0, и пишет `List_ApplyUltimateON[i] = this.Ultimate_ApplyID` (+0x2e0) (0x1116de0..0x1116e60).
3. **Энциклопедия:** если `List_Book_Ultimate` (статик +0x780) не содержит ID — `Add` (0x1116e7c..0x1116f08). **Этот список читается/пишется `Z_GPGSManager.SaveLocalData/LoadLocalData` — то есть попадает в сохранение (облачный/локальный файл).**
4. Сбрасывает `Ultimate_ApplyID` и `Ultimate_ApplyID_Location` (+0x2e0/+0x2e4) в 0, `static+0x18 = 0` (`List_ApplyUltimatePossible.Count`), `StartCoroutine` анимации `Play_…` на кнопке, `Z_Database +0x9c8`.
5. Хвостовой переход `V_Ability_Manager.AbilityStatApply(2)` (0x1103210, параметр 2 = «пересчёт в бою»; при старте боя `Start_Ability` вызывает `AbilityStatApply(1)`, 0x1102ef4).
`AbilityStatApply` в конце вызывает `C_Battle_AllyUnit.StartInformation_A` (0x1043fa0, вызов на 0x110fed8), та — тейл-коллом `StartInformation_B` (0x11c0684): **именно там по `List_ApplyUltimateON` включаются ультимейты** [П].

## 5. Где игра читает List_ApplyUltimateON и какие ID проверяет [П]

Метод: скан статик-чтений поля +0x790 `Z_Database` и ближайшие `mov w1,#ID` перед `List<int>.Contains` (0x16ac588).

| Читатель | RVA чтения (пример) | Проверяемые ID |
|---|---|---|
| `C_Battle_AllyUnit.StartInformation_B` (26 мест) | 0x11d8ad8… | 1, 3, 5, 6, 7, 10, 14, 16, 17, 19, 23, 24, 29, 32, 33, 34, 35, 37, 38, 40, 43, 45, 51, 55, 57 |
| `<Play_ThunderC>d__1166.MoveNext` | 0x1084d1c | 1 |
| `<Play_Thunder_Com03>d__1168.MoveNext` | 0x10854bc | 17 |
| `<Play_Meteor>d__1098.MoveNext` / `<Play_Meteor_Com01>d__1099.MoveNext` | 0x1077554 / 0x1078a70 | 35 / 5 |
| `<Play_Pandora>d__1067.MoveNext` | 0x107aabc, 0x107b290 | 5, 35 |
| `<Play_Electric>d__1138.MoveNext` | 0x106793c | 34 |
| `<Play_EnergyBullet_Com02>d__1185.MoveNext` | 0x106aab0 | 29 |
| `<Play_FlashShock_Com02>d__1198.MoveNext` | 0x106ea28, 0x106eb38 | 44 |
| `<Play_Incinerate_Com02>d__1178.MoveNext` | 0x1074b40 | 19 |
| `<Play_LavaField_Com02>d__1157.MoveNext`, `C_Battle_AllyMeteorLand.Start_MeteorLand` | 0x1076008, 0x11b541c | 56 |
| `<Play_ElectricZone_Com04_Active>d__49.MoveNext` | 0x118faec | 57 |
| `<Play_Blizzard_Com03>d__1191.MoveNext` | 0x106576c | 51 |
| `<Play_Void>d__1171.MoveNext`, `C_Battle_AllyTornado.TornadoAttack` | 0x108c0b4, 0x11bc104 | 10 (+14 у вихря) |
| `C_Battle_AllyArcaneRay.BulletAttack` | 0x10e3b3c | 16 |
| `C_Battle_AllyExplosion.ExplosionAttack` | 0x1193e74 | 40 |
| `C_Battle_AllyUnit.Display_AbilityDetail` | 0x1056848, 0x1058778 | 3, 37 (текст окна) |
| `V_AbilityShow.Show_AbilityInformation`, `V_Ability_Manager.Start_Ability`, `Button_MagicCom_Decision`, `Button_Ultimate_Decision` | — | чтение по индексу слотов (отрисовка/сброс) |
| `C_Battle_Manager.StartAutoSave` | 0x10be864, 0x10be898, 0x10be8cc, 0x10be900 | слоты 1..4 копируются в `Array_Int_AutoSaveGame` (+ слот 0 отдельным кодом [Г]) |
| `Z_Database.ResetInformation` / `.cctor` | 0x114d8ac / 0x115b64c | создание списка: **5 нулевых элементов** (`List<int>` из пяти нулей) |

Вывод: у ультимейтов нет единого обработчика — каждый реализован своим `if (список содержит ID)` в коде конкретного заклинания. Остальные заклинания, чьи ультимейты без собственной корутины (например 3, 5, 6, 14…), правят поведение внутри `StartInformation_B` (флаги/статы) и в корутинах `Play_*_ComNN`.

## 6. Корутины `Play_UltimateNN` героя (`C_Battle_AllyUnit`) [П]

| Метод (RVA) | MoveNext (RVA), класс итератора | Поля итератора | Что делает (по чтению полей героя) |
|---|---|---|---|
| `Play_Ultimate03` 0x104f4ac | 0x1088fd8, `<Play_Ultimate03>d__1184` | `ApplyDamageRatio` 0x28, `ApplyFireNum` 0x2c, `ApplyCooltime` 0x30, `ApplyCooltimeEditCastNum` 0x34, `Transform_ObjPGUnit` 0x38, `x` 0x40 | залпы, 3 вызова `Bullet_Create`; читает `BulletID_EnergyBullet` (+0xfc8), `SoundID_EnergyBullet` (+0x108c), `BulletID_FlashShock` (+0xfd8) |
| `Play_Ultimate07` 0x104f9f4 | 0x10892d4, `<Play_Ultimate07>d__1197` | `ApplyDamageRatio` 0x28, `ApplySpeedRatio` 0x2c, `ApplyCastNum` 0x30, `ApplyCastMaxNum` 0x34, `ApplyCooltimeEditCastNum` 0x38, `Transform_ObjPGUnit` 0x40 | серия выстрелов (2 `Bullet_Create`), `C_Battle_AllyElectricZone.Start_Ultimate37`; читает `BulletID_FlashShock` (+0xfd8), `ApplyFlashShockID` (+0xfec) |
| `Play_Ultimate37` 0x104fcd8 | 0x1089668, `<Play_Ultimate37>d__1204` | только `<>4__this` | зона `Start_Ultimate37` (0x118d418; корутины `Play_Ultimate37_Duration/AnimationA/AnimationB` 0x118d85c/0x118d8e4/0x118d96c), 1 `Bullet_Create`; читает `BulletID_ArcaneRay` (+0xfd0) |
| `Play_Ultimate38` 0x104e7ac | 0x1089910, `<Play_Ultimate38>d__1152` | `ApplyDamageRatio` 0x28, `ApplyArcaneRayResetNum` 0x2c, `ApplyArcaneRayAttackNum` 0x30, `ApplyCooltimeEditCastNum` 0x34, `Transform_ObjPGUnit` 0x38 | лучи, 1 `Bullet_Create`; `BulletID_ArcaneRay` (+0xfd0), `SoundID_ArcaneRay` (+0x1094) |
| `Play_Ultimate38_Effect` 0x104e814 | 0x1089cac, `<Play_Ultimate38_Effect>d__1153` | `i` 0x28 | визуальный эффект Бастер Бима |
| `Play_Ultimate43` 0x104ec8c | 0x1089ec0, `<Play_Ultimate43>d__1164` | `ApplyDamageRatio` 0x28, `SpiritFirePowerUp_Count` 0x2c, `SpiritDamagePower` 0x30, `SpiritBulletSizePower` 0x34, `ApplyCooltimeEditCastNum` 0x38, `SpiritCulNum` 0x44, `SpiritBulletAngle` 0x48, `Transform_ObjSpirit(_Plus)` 0x60/0x68, `List_EGUnit_Distance` 0x70 | «Валькирия»: цикл выстрелов духов по ближайшим врагам, много `set_sprite`; читает `Apply_SpiritFireNum` (+0xff8) и `SoundID_Spirit` (+0x1018) |
| `Play_Ultimate55` 0x104f30c | 0x108b094, `<Play_Ultimate55>d__1180` | `ApplyDamageRatio` 0x28, `ApplyFireNum` 0x2c, `ApplyAccuracyAimPlus` 0x30, `ApplySizeVar` 0x34, `Transform_ObjPGUnit` 0x38, `x` 0x40 | залпы испепеления (читает `BulletID_Incinerate` +0xfc4, `Apply_IncinerateNum` +0xffc) **плюс** вызовы `Unit_RevivalON`, `Game_EndON`, `Time.set_timeScale`, `StartAutoSave`, `Unit_AriseDivine`, `Application.internetReachability` — похоже на «воскрешение/конец игры» (Бримстоун) [Г]: осторожно |
| `Start_Satellite_Ultimate32` 0x108dce4; `Play_SatelliteA_Motion_Ultimate32` 0x108de98; `Play_Satellite_AttackDelay_Ultimate32` 0x108df20 | — | — | спутники Абаддона (комбо спутника `SatelliteComID` +0xf98) |

Сигнатуры: все `Play_UltimateNN()` без параметров, возвращают `IEnumerator`; запуск игры — `StartCoroutine("Play_UltimateNN")` строкой из `StartInformation_B` (поэтому прямых `bl` нет, как у заклинаний) [Г по аналогии с ACTIVE_SPELLS.md §2: подтверждено только для `Play_Fireball`/`Play_Meteor`].

Данные для «ручного вызова» (аналог `ISpells.Cast`): первый `MoveNext` инициализирует поля (`ApplyDamageRatio`, `ApplyFireNum`, `Transform_ObjPGUnit`…), второй делает залп без ожиданий [Г по аналогии]. Перезарядки у ультимейта нет — только `WaitForSeconds` внутри цикла итератора, читающие `UnitApply_*PSpeed` соответствующего заклинания.

## 7. Можно ли запустить по кнопке из мода

| Способ | Что делает | Сигнатуры/параметры | Побочные эффекты | Вывод |
|---|---|---|---|---|
| **А. Выдать/снять ультимейт** | `Z_Database.List_ApplyUltimateON[i] = ID` (или 0), затем `V_Ability_Manager.AbilityStatApply(2)` (инстанс менеджера: `FindObjectOfType<V_Ability_Manager>()` или запомнить в хуке `Start_Ability`/`Button_*`) | `AbilityStatApply(int ApplyType)` 0x1103210; `ApplyType` 2 в бою | `AbilityStatApply` — самая тяжёлая функция (Ghidra не декомпилирует); полный пересчёт характеристик героя и перезапуск `StartInformation_B` (сбрасывает флаги `Is*_P`, мод `ISpells.SetAuto` глушит корутины заново — см. MELONLOADER.md §16); не трогает `List_Book_Ultimate` (в энциклопедию не пишет) | **Рекомендуется** как «тумблер ультимейта по кнопке» (включил на 10 с, выключил). Нужна проверка: гаснут ли у ультимейтов с собственной корутиной сами корутины (в `StartInformation_B` для ID 32 есть явная ветка выключения: `SetActive(false)` спутников при `!Contains(32)`, для 03/07/37/38/43/55 не проверено) |
| **Б. Ручной залп** | `hero.Play_Ultimate38()`/`03`/`07`/`37`/`43`/`55` + два `MoveNext` | см. §6; без параметров | как у заклинаний: звук, счётчики каста; для 55 — воскрешение/`timeScale` | годится только для 38, 03, 07 (боевые циклы); **55 не вызывать** без проверки; 43 — визуал+цикл духов |
| **В. Имитировать выбор** | вызвать `Button_Ultimate_Decision` после записи `Ultimate_ApplyID` | без параметров | `AllyUnitGlobal_Level++`, сброс маны, **запись в `List_Book_Ultimate` (сохраняемый)**, анимации UI | не использовать, кроме тестов на копии сохранения |
| Г. Принудительно «открыть выбор» | записать ID в `List_ApplyUltimatePossible` и вызвать `Button_Ultimate_IN` | — | показывает окно выбора; нужен активный `Canvas` боя | экспериментально |

Что **нельзя** разумно сделать: «разовый каст ультимейта по кнопке» — такого действия в игре нет; любые «ульты по кнопке» придётся собирать из способов А/Б.

## 8. Как узнать выбранный ультимейт и событие выбора

- **Чтение:** `Z_Database.List_ApplyUltimateON` (`List<int>` из 5, 0 = свободно) — поле статическое, читается как `Il2Cpp.Z_Database.List_ApplyUltimateON` (по образцу `auto_artifact`: `Z_Database.List_ArtifactObtain`). Название — из `Table("MagicCom")` колонка `궁극기 이름` по ID.
- **Событие «выбран ультимейт»:** хук `After` на `V_Ability_Manager.Button_Ultimate_Decision` (0 параметров). Внимание: метод завершается хвостовым переходом `AbilityStatApply`, но Harmony-постфикс на входной метод всё равно выполнится после возврата [Г — проверить; безопасная альтернатива — `After` на `V_Ability_Manager.AbilityStatApply` (параметр 2) и сравнение слепка `List_ApplyUltimateON` с прошлым].
- **Доступные кандидаты:** `V_Ability_Manager.List_ApplyUltimatePossible` (статик +0x0) после `Button_MagicCom_Decision`.
- **Связанные поля героя:** `C_Battle_AllyUnit.*ComID` (какая комбинация активна у каждого заклинания; `MagicBulletComID` 0xf54, `FireballComID` 0xf58, `ThunderComID` 0xf5c, `TornadoComID` 0xf60, `MeteorComID` 0xf64, `EnergyBulletComID` 0xf68, `SpiritComID` 0xf6c, `ElectricComID` 0xf70, `ArcaneRayComID` 0xf74, `FlashShockComID` 0xf78, `IncinerateComID` 0xf7c, `IceNovaComID` 0xf80, `TsunamiComID` 0xf84, `BlizzardComID` 0xf88, `ShieldComID` 0xf8c, `ElectricZoneComID` 0xf90, `LavaFieldComID` 0xf94, `SatelliteComID` 0xf98, `GhostComID` 0xf9c, `MagicCircleComID` 0xfa0) [П].
- **Сохранение:** `List_ApplyUltimateON` в бою сохраняется в автосохранение боя (`StartAutoSave`), в долговременное сохранение не пишется; `List_Book_Ultimate` — пишется.

## 9. Предлагаемый пробный мод `ultimate_probe` (описание, без сборки)

Назначение: подтвердить §3–§5 и выяснить, возможен ли «тумблер» (способ А) без падений.
1. **Только чтение (шаг 1):** по `battleStart` и по `After` на `V_Ability_Manager.AbilityStatApply` (ApplyType 2) логировать `[ultimate_probe] ON=[a,b,c,d,e] possible=[…] AreaMaxNum=… ClassID=… ExperimentID=…`; на `After` `Button_MagicCom_Decision` — содержимое `List_ApplyUltimatePossible`. Ничего не писать.
2. **Тумблер (шаг 2), включается настройкой «опасно»:** кнопка боя (`UiKit.AddHudButton`) «Ульт»: нажатие — найти свободный слот и записать ID выбранного в настройке ультимейта (например 38 «Бастер Бим» — нужны уровни и комбинация), вызвать `AbilityStatApply(2)`; повторное нажатие — вернуть слот в 0 и снова `AbilityStatApply(2)`. В логе — `ON` до/после, число `StartCoroutine`/`StopCoroutine` в течение 2 с (хук на `MonoBehaviour.StartCoroutine(string)` не ставить — горячий; вместо этого сверять `IsEnergyBullet_P`/`IsArcaneRay_P` героя).
3. **Ручной залп (шаг 3):** для 38 — `hero.Play_Ultimate38()` + два `MoveNext` в защищённом `ctx.Guard`; проверка по `gamelog`/`modkit.log`: появилось ли `Bullet_Create` (через `List_BulletArcaneRay_Empty` убывает), нет ли исключений. 55 в пробе исключить.
4. **Критерии:** (а) записи слотов и списка `Possible` совпадают с §3 (в том числе правило «≥ зоны 12»); (б) после способа А ультимейт включается/выключается и не ломает характеристики героя (сравнить `UnitApply_*` до/после через `gamelog`); (в) нет вылетов `Scudo`.
5. **Не включать на живом сохранении**, пока не подтверждено, что `AbilityStatApply(2)` из мода безопасен: лучше на тестовой копии прогресса (`Download/ModKit/progress` / «Импорт прогресса»).

## 10. Не подтверждено / что проверить

1. Значение колонки `궁극기 조건` для ID 40 (25) и ID 2 (нет проверки ID 2 в коде) — запись в бою и логи.
2. Что именно запускает окно выбора ультимейта после `Button_MagicCom_Decision` (порог уровня?), смысл `AllyUnitGlobal_Level` (+0x9d8) и сброса маны.
3. Выключаются ли корутины ультимейтов 03/07/37/38/43/55 при удалении ID из `List_ApplyUltimateON` (способ А «выключить»).
4. Точный набор полей `Play_UltimateNN`, которые читают `UnitApply_*PSpeed` (для «перезарядки»).
5. Формат имени вложенных итераторов для Harmony (`<Play_Ultimate38>d__1152`) — проверить в `Hooks.FindType`.
6. Поведение `Play_Ultimate55` (воскрешение/`timeScale`/`Game_EndON`) — назначение подтвердить на устройстве, не вызывать вручную.
