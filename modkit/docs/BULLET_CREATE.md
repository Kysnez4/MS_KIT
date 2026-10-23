# C_Battle_AllyUnit.Bullet_Create — карта метода

Разведка перед заменой метода своей реализацией на C# (`ctx.Replace`). Источник: локальный декомпилят `modkit/decomp/0x1037eb0_full.c` (в git не входит), `dump.cs`, `tools/ghidra/names.tsv`. Код игры здесь не приводится — только имена, офсеты и смысл. Ничего из описанного **не проверено в бою** ◻.

Карта составлена по полному декомпиляту `0x1037eb0_full.c` (6593 строки; выгружен headless-скриптом `D:\tools\ghidra\scripts\DecompToFile.java` через `analyzeHeadless`, т.к. MCP-сервер обрезает вывод на 200000 символов, см. [DECOMP.md](DECOMP.md)). Старый `0x1037eb0.c` обрезан на `case 75` — не использовать.

## 1. Сигнатура

`void C_Battle_AllyUnit.Bullet_Create(Transform Transform_CreateUnit, int BulletID, float EGUnit_X, float EGUnit_Y, float EGUnit_Z, float AccuracyAimPlus, float PowerVar01, float PowerVar02, float SpecialVar01, float SpecialVar02, float SpecialVar03, float SpecialVar04, float DamageRatio)` — RVA **0x1037eb0** (в Ghidra 0x1137eb0), экземплярный, 13 параметров (`paramCount = 13` для `Replace`).

| Параметр | Смысл |
|---|---|
| `Transform_CreateUnit` | откуда вылетает снаряд: обычно `Transform_ObjUnit` героя, у союзников — их Transform. `null` → ветка ничего не делает (`break`). Для 62/110 (Лавовое поле) не используется |
| `BulletID` | вариант снаряда, берётся из полей героя `BulletID_*` (+0xfa4…+0xfd8). Один вид заклинания имеет несколько ID — варианты/усиления |
| `EGUnit_X/Y/Z` | мировая точка цели (ближайший враг или случайная точка); передаётся в снаряд как `EUnit_Vector*` |
| `AccuracyAimPlus` | добавка к разбросу (веер: `i * const` в цикле залпа); часть веток её обнуляет |
| `PowerVar01/02` | множители «усиленного» снаряда (Огненный шар 1.0/1.5; Энергетическая пуля: оба =2 при срабатывании `UnitApply_EnergyBulletPowerRan`); 01 обычно множит урон (и область), 02 — размер |
| `SpecialVar01..04` | параметры ветки: режим (Вспышка 69: 1…9; Цунами 49: 1…5 — сторона появления; Лонгинус 106: `SpecialVar02 == 12`, `SpecialVar03 == 20`), передаются в `EUnitApply_SpecialVar*` снаряда; многие ветки обнуляют |
| `DamageRatio` | итоговый множитель урона: урон = `UnitApply_XDamage × DamageRatio` (часто ещё × `PowerVar01`) |

### Кто вызывает
Из сохранённых декомпилятов найдено (Ghidra xrefs не снимались — список неполный):
- корутины `Play_<Заклинание>` героя (`MoveNext` итераторов): Огненный шар, Метеор (2 вызова: основной и доп. каст), Молния (в цикле) — см. [ACTIVE_SPELLS.md](ACTIVE_SPELLS.md);
- `<Play_Doppelganger_Attack>d__1203.MoveNext` (decomp `0x1066764`) — союзник; BulletID берётся у героя: `BulletID_MagicBullet/Fireball/Spirit/Electric/EnergyBullet`;
- ультимейты `Play_Ultimate03/07/37/38` — `BulletID_EnergyBullet`, `BulletID_FlashShock`, `BulletID_ArcaneRay` ([ULTIMATES.md](ULTIMATES.md));
- моды ModKit: компаньоны (`modkit_companions`) — [COMPANIONS.md](COMPANIONS.md).

Не выяснено: где присваиваются `BulletID_*` и кто вызывает метод с «фиксированными» ID (18, 53, 68, 70, 71, 83, 96, 100, 106…). Нужен xref-поиск по 0x1137eb0 в Ghidra.

## 2. Структура

### Пролог и `default`
1. Однократная инициализация метаданных IL2CPP (указатели типов/методов, флаг `DAT_027b0fea`).
2. `switch (BulletID)` по диапазону **1…129 (0x81)**, **111 веток**. Отсутствуют (идут в `default`): 5, 11, 24, 42, 44, 45, 46, 55, 61, 72, 73, 74, 77, 78, 103, 112, 113, 118, а также ≤0 и >129.
3. `default` (`caseD_5`) — сразу `return`: ничего не создаётся, исключения нет. Это значит, что свои ID ≥1000 безопасно уходят в `default` у оригинала.
4. Внутри ветки `break` (пустой пул, `null` Transform/компонент) выходит из switch и тоже на `return`; в самом конце функции стоит недостижимый `FUN_01015b08` (обработчик исключения IL2CPP).

### Типовая ветка
1. Проверка `Transform_CreateUnit != null`, позиция источника (x, y; z = константа слоя, иногда `+ i × const` для порядка отрисовки).
2. Пул: статический `List<GameObject>` в `C_Battle_Manager` (офсеты ниже): `get_Item(0)`, затем `Remove(obj)`. **Проверки `Count` нет** почти везде — пустой пул даст исключение в `get_Item`, выстрел пропадёт. Проверяют `Count < 1` только ветки 53 и 83.
3. `transform.position = …`, `SetActive(true)`.
4. Компонент: `GameObject.GetComponent<T>()` на самом объекте или `transform.GetChild(0).GetComponent<T>()` (зависит от префаба, см. таблицу).
5. Сбор чисел: урон/размер/скорость/пробивание/область из полей `this.UnitApply_<Spell>*`, `CriMultiplier` из `this.UnitApply_CriMultiplier`, шанс крита — из **статического** `C_Battle_AllyUnit.UnitApply_<Spell>_CriRan` (у Призрака, Щита, HP-шара, Уровня — из поля экземпляра `this.UnitApply_CriRan`).
6. Прыжок в общий хвост с вызовом старта (§2.1).

### 2.1. Общие хвосты (адреса Ghidra)

Все `goto`-цепочки сходятся в **пять** мест вызова. Промежуточные метки только обнуляют/домножают аргументы и ставят константы (MagicID, пробивание, разброс).

**Хвост B — `C_Battle_AllyBullet.BulletAttack`** (RVA 0x102ca34, 24 параметра), метка `LAB_01142f40` (вход `LAB_01142f38` дополнительно обнуляет FreezeTime). Аргументы по порядку сигнатуры:

| Параметр | Откуда |
|---|---|
| `PApply_BulletID` | `iVar29` (обычно = case) |
| `PApply_ExplosionID` | `iVar30` (0, у Огненного шара/маг. пули 8, 98 — = case: 6, 8, 26, 27, 52, 75, 98, 121) |
| `PApply_MagicID` | `PApply_MagicID` (константа в промежуточной метке) |
| `PUnit_VectorX/Y/Z` | позиция источника (z — слой) |
| `EUnit_VectorX/Y/Z` | `EGUnit_X/Y/Z` (у части веток со смещением или обнулены) |
| `EUnitApply_Attack` | `UnitApply_XDamage × DamageRatio [× PowerVar01]` |
| `EUnitApply_AccuracyAim` | поле `…AccuracyAim` или константа: 10 (Дух), 80 (Испепеление 47/64), 900 (Электро, Энергопуля, Дух 22/56), 0 (Цунами); у 109/116/129 = static `C_Battle_Manager.TestAim` (0x250) + 30/35 |
| `EUnitApply_AccuracyAimPlus` | аргумент `AccuracyAimPlus` (иногда 0) |
| `EUnitApply_CriRan` | `iVar12` = static `UnitApply_<Spell>_CriRan` |
| `EUnitApply_CriMultiplier` | `this.UnitApply_CriMultiplier` |
| `EUnitApply_PierceNum` | поле `…PierceNum` или 999 |
| `EUnitApply_ExplosionArea` | 0 (у Огненного шара — `FireballSize × PowerVar01`) |
| `EUnitApply_BulletSize` | поле `…Size [× PowerVar02] [× Random]` |
| `EUnitApply_BulletSpeed` | поле `…MSpeed/BSpeed [× Random]` или 0 |
| `SpecialVar01` | обычно 0; Испепеление 109/116/129 — `PowerVar01`; Электро 92/114 — −1 |
| `SpecialVar02` | 0 |
| `SpecialVar03` | всегда 0 |
| `SpecialVar04` | обычно 0; **10015** у Испепеления (47, 64, 65, 109, 116, 129) |
| `StunTime` | 0 |
| `FreezeTime` | 0 |

**Хвост E — `C_Battle_AllyExplosion.ExplosionAttack`** (RVA 0x1192b20, 13 параметров), метка `LAB_01143330`; входы `LAB_01143328` (обнуляет SV02, Stun, SV03), `LAB_01143324` (MagicID 7, Молния), `LAB_011400c4` (MagicID 10, Метеор), `LAB_01141ba0` (MagicID 5, Ледяная нова), `LAB_0113e9b4`/`LAB_01141d38` (MagicID 17, Буря), `LAB_0113ff3c` (11, Призрак), `LAB_0113ec64` (6, Щит), `LAB_011436d8` (MagicID 0). Аргументы:

| Параметр | Откуда |
|---|---|
| `PApply_ExplosionID` | `iVar12` (= case) |
| `PApply_MagicID` | **`iVar29`** (в E-ветках это MagicID, а не BulletID) |
| `EUnitApply_Damage` | `XDamage × DamageRatio [× PowerVar01]` |
| `EUnitApply_CriRan` | `iVar30` = static `…_CriRan` или `this.UnitApply_CriRan` |
| `EUnitApply_CriMultiplier` | `this.UnitApply_CriMultiplier` |
| `EUnitApply_ExplosionArea` | поле `…Area [× PowerVar01]` |
| `SpecialVar01/02` | обычно 0 (106: SV02 = 12) |
| `SpecialVar03` | обычно 0 |
| `SpecialVar04` | всегда 0 |
| `StunTime` | обычно 0 (48 — static `ApplyStunTime`) |
| `FreezeTime` | `IceNovaFreezeTime`; у Бури — static `ApplyFreezeTime`; иначе 0 |
| `EUnitApply_ExplosionHitNum` | 0 (35 — `this.IceNovaAreaNum`, после вызова +1) |

**Хвост A — `C_Battle_AllyArcaneRay.BulletAttack`** (RVA 0x10e3544, 25 параметров — как у B плюс `ArcaneRayTime` перед SV01), метка `LAB_01142bbc` (входы `LAB_0113dd90`, `LAB_01142b9c` обнуляют `AccuracyAimPlus` и SV01). BulletID = `iVar12` (= case), ExplosionID 0, **MagicID 18**, z источника и цели = 0, AccuracyAim 0, CriRan = static 0x230, Pierce 0, Area 0, Size = `ArcaneRaySize`, Speed 0, `ArcaneRayTime` = поле, SV01 — из аргумента/0, остальное 0. Объект перед этим `SetParent(Obj_UnitArcaneRayMid)`, `localPosition = 0`.

**Хвост T — `C_Battle_AllyTornado.TornadoAttack`** (RVA 0x11bbb60, 22 параметра), метка `LAB_01140a70`; вход `LAB_01140a68` обнуляет позицию источника и `EGUnit_X/Y` (торнадо привязан к `Obj_UnitTornadoMid` — 39, 95, 104), `LAB_0113e810`/`LAB_0113f2cc` — SV01 = 0, AccuracyAim 180, размер × PowerVar02, урон × DamageRatio × PowerVar01. TornadoID = `iVar12` (= case), **MagicID 12**, Attack, AccuracyAim, CriRan = static 0x21c, CriMult, `TornadoSize` = `ApplyTornadoSize_Cal`, SizeExpand, Speed = `TornadoMoveSpeed`, Time, SV01, StunTime — из локала (обычно 0), Freeze 0.

**Хвост M — `C_Battle_AllyMeteorLand.Start_MeteorLand`** (RVA 0x11b4c80, 11 параметров), метка `LAB_01141374` внутри 62; туда же прыгает 110. `MeteorLandID = this.ApplyID_LavaField`, **MagicID 20**, урон `LavaFieldDamage × DamageRatio`, CriRan static 0x234, CriMult, Size, Time, SV01 = `LavaField_SpecialVar01`, SV02 = `LavaFieldHitDamageRatioNum`, SV03/04 = 0 (◻ точный порядок SV не сверен). Перед вызовом — `Script_SoundManager.PlaySound(…)`.

**Inline-вызовы** (без общего хвоста): 35 (E, Ледяная нова), 51 и 90 (A, цикл копий; 90 — по `ArcaneRayNum`), 65 (B, цикл по static `ApplyID_Incinerate`), 69 (B, Вспышка, цикл 1/3 копии), 86 (B, Цунами, цикл с позициями из полей героя по SV01), 62 (M).

### Пулы `C_Battle_Manager` (static, офсет в статике)
`List_BulletAlly_Empty` 0xc0, `List_BulletFireball_Empty` 0xc8, `List_BulletConstellation_Empty` 0xd0, `List_ExplosionAlly_Empty` 0xf0, `List_ExplosionAlly_Thunder_Empty` 0xf8, `_Meteor_` 0x100, `_Blizzard_` 0x108, `_Ghost_` 0x110, `_GravityOrb_` 0x118, `_HpBallBomb_` 0x120, `_LevelUp_` 0x128, `List_BulletElectric_Empty` 0x140, `List_BulletArcaneRay_Empty` 0x150, `List_BulletTsunami_Empty` 0x158, `List_IceNova_Empty` 0x160, `List_Tornado_Empty` 0x168, `List_MeteorLand_Empty` 0x170, `List_Incinerate_Empty` 0x178, `List_EnergyBullet_Empty` 0x180, `List_SpiritBullet_Empty` 0x188, `List_DestroyField_Empty` 0x190, `List_FlashShock_Empty` 0x198. Снаряды сами возвращают себя в эти списки при деактивации.

Static `C_Battle_AllyUnit.UnitApply_<Spell>_CriRan`: MagicBullet 0x1f8, Fireball 0x1fc, Thunder 0x200, Tsunami 0x204, Spirit 0x208, Meteor 0x214, IceNova 0x218, Tornado 0x21c, Electric 0x220, Incinerate 0x224, EnergyBullet 0x228, Blizzard 0x22c, ArcaneRay 0x230, LavaField 0x234, FlashShock 0x238.

### Таблица BulletID → заклинание
Класс/хвост: B, A, E, T, M (см. §2.1). «Компонент»: obj — на самом объекте, ch0 — на `GetChild(0)`. MagicID: 1 Маг. пуля, 2 Огненный шар, 3 Дух, 5 Ледяная нова, 6 Щит, 7 Молния, 9 Цунами, 10 Метеор, 11 Призрак, 12 Торнадо, 13 Электро, 15 Испепеление, 16 Энергопуля, 17 Буря, 18 Луч, 20 Лавовое поле, 21 Вспышка, 0 — без магии (Сфера гравитации, HP-шар, Уровень, Лонгинус-удар).

| Заклинание | BulletID (dec) | Хвост / пул / компонент | MagicID | ExplosionID | Особенности |
|---|---|---|---|---|---|
| Магическая пуля | 1, 3, 8, 10, 98, 108 | B / BulletAlly 0xc0 / obj | 1 | 0; 8 → 8, 98 → 98 | 3 и 108 — пробивание 999 (без `PierceNum`); 8/98 — со взрывом |
| Огненный шар | 6, 26, 52, 121 | B / Fireball 0xc8 / obj | 2 | = ID | ExplosionArea = `FireballSize × PowerVar01`, урон × PowerVar01 |
| Созвездие | 27, 75 | B / Constellation 0xd0 / obj | 2 | = ID | `Obj_UnitConstellationMid`; 75 — `Random(1,5)` выбирает сторону (±2.625), default — цель в 0 |
| Взрыв огненного шара | 100 | E / ExplosionAlly 0xf0 / ch0 | 2 | 100 | `FireballDamage/Size` |
| Электро | 2, 57, 59, 85, 92, 101, 114, 124, 125 | B / Electric 0x140 / ch0 | 13 | 0 | размер × `Random`; AccuracyAim 900, пробивание 999; 92/114 — SV01 = −1; 101 — aim из `TestAim` |
| Чародейский луч | 4, 23, 40, 51, 67, 90, 94, 111, 127 | A / ArcaneRay 0x150 / ch0 | 18 | 0 | `SetParent(Obj_UnitArcaneRayMid)`; 51 и 90 — inline-циклы |
| Молния | 7, 21, 38, 41, 58, 79, 82 | E / Thunder 0xf8 / ch0 | 7 | = ID | |
| Лонгинус (молния) | 106 | E / Thunder 0xf8 / ch0 | 7 или 0 | 106 | `LonginusDamage`; режимы по `SpecialVar02 == 12` и `SpecialVar03 == 20` (разный случайный разброс); в одном режиме MagicID 0, SV02 = 12 |
| Метеор | 9, 25, 32, 54, 93, 97 | E / Meteor 0x100 / ch0 | 10 | = ID | 25/54/93 — случайное смещение; урон и область × PowerVar01 |
| Ледяная нова | 12, 30, 35, 48, 87, 91, 117 | E / IceNova 0x160 / obj | 5 | = ID | 35 — inline, HitNum = `IceNovaAreaNum` (+1 после); 48 — Stun = static `ApplyStunTime`; 91 — позиция героя (`Transform_ObjUnit`) |
| Торнадо | 13, 39, 60, 76, 80, 88, 95, 104 | T / Tornado 0x168 / obj | 12 | = ID (TornadoID) | 39/95/104 — нулевая позиция (привязка к `Obj_UnitTornadoMid`); 88 — `TornadoNum` |
| Цунами | 14, 37, 49, 63, 86, 102, 115, 123 | B / Tsunami 0x158 / obj | 9 | 0 | пробивание 999, AccuracyAim 0; 49 — сторона по SV01; 86 — inline-цикл; 102/115 — `Tsunami_PosX/Y` |
| Испепеление | 15, 47, 64, 65, 84, 109, 116, 129 | B / Incinerate 0x178 / obj | 15 | 0 | SV04 = 10015; 65 — inline-цикл; 84/109/116/129 — z по `Apply_IncinerateNum`, 109/116/129 — SV01 = PowerVar01 |
| Энергетическая пуля | 16, 20, 28, 33, 36, 66, 89, 120, 126 | B / EnergyBullet 0x180 / ch0 | 16 | 0 | static `UnitApply_EnergyBulletPowerRan` (0x1dc) → PowerVar01/02 = 2; размер/скорость × `Random`; 28/33/89/120 — static `UnitApply_EnergyBulletExplosionArea` (0x1e0); AccuracyAim 900 |
| Снежная буря | 17, 29, 34, 50, 81, 105, 119 | E / Blizzard 0x108 / ch0 | 17 | = ID | точка = цель + `Random`; Freeze = static `ApplyFreezeTime` |
| Призрак | 18, 96 | E / Ghost 0x110 / ch0 | 11 | = ID | CriRan из `this.UnitApply_CriRan` |
| Дух | 19, 22, 31, 43, 56, 99, 107, 122, 128 | B / SpiritBullet 0x188 / ch0 | 3 | 0 | AccuracyAim 10 (22/56 — 900), пробивание 999; 22/122 — `SpiritAttackPlus`, 122 — `SpiritPowerRan`; 43 — цель (0,0) |
| Щит / поле разрушения | 53, 83 | E / 53: DestroyField 0x190, 83: ExplosionAlly 0xf0 / ch0 | 6 | = ID | `ShieldDamage/Area`, CriRan из `this`; **есть проверка `Count`**; 53 — `SetParent(Obj_UnitDestroyFieldMid)` |
| Лавовое поле | 62, 110 | M / MeteorLand 0x170 | 20 | `ApplyID_LavaField` | звук, точка = цель + `Random(-2,2) × LavaField_RandomPosValue` (110 — без смещения); `Transform_CreateUnit` не нужен |
| Сфера гравитации | 68 | E / GravityOrb 0x118 / ch0 | 0 | 68 | позиция героя, `Obj_UnitGravityOrbMid` |
| Вспышка | 69 | B / FlashShock 0x198 / ch0 | 21 | 0 | inline-цикл: `SpecialVar01` 1…9 — число копий (1 или 3) и разброс |
| HP-шар (взрыв) | 70 | E / HpBallBomb 0x120 / ch0 | 0 | 70 | `ApplyHpBallBomb_Damage`, CriRan из `this` |
| Взрыв при уровне | 71 | E / LevelUp 0x128 / ch0 | 0 | 71 | `ApplyLevelUpExplosion_Damage/Area`, CriRan из `this` |

Чем различаются варианты одного заклинания (например, 7 и 21/38/41/58/79/82): ExplosionID/BulletID = case, поэтому вся разница — в поведении снаряда по своему ID (внутри `BulletAttack`/`ExplosionAttack`/`Unit_Damage` снаряда) плюс разброс/обнуления в ветке. Какой ID какому улучшению соответствует — смотреть по присваиванию `BulletID_*` (не разобрано).

## 3. Общий скелет для C#

Почти все ветки сводятся к одной функции с таблицей данных:

```
Spawn(spec, src, target, args):
  pool = C_Battle_Manager.<spec.Pool>; if pool пуст → return (у игры — исключение)
  go = pool[0]; pool.Remove(go)
  go.transform.position = spec.Pos(src, target, args)    // z: слой / фикс.
  go.SetActive(true)
  comp = spec.OnChild ? go.transform.GetChild(0).GetComponent<T>() : go.GetComponent<T>()
  stats = spec.Stats(hero)                                // Damage, Size, Speed, Pierce, CriRan, CriMult
  comp.<Start>(… по таблицам §2.1 …)
```

**Данные** (таблица на BulletID): пул, obj/ch0, хвост, BulletID/ExplosionID/MagicID, набор полей героя, константы (AccuracyAim 10/80/900, пробивание 999, SV04 10015 и т.п.), какие `SpecialVar`/`AccuracyAimPlus` обнуляются.

**Логика** (отдельный код): разброс/случайности (`Random.Range` — Цунами, Буря, Энергопуля, Метеор, Созвездие, Лонгинус), привязка к `Obj_Unit*Mid` (луч, торнадо, гравитация, созвездие, щит), циклы (51, 65, 69, 86, 90), шанс усиления энергопули, звук и случайная точка у Лавового поля, позиция героя у Сферы гравитации и Новы 91.

## 4. Заметки для реестра своих снарядов

Цель: обработчик мода для своего `BulletID ≥ 1000` (оригинал такие ID отправляет в `default` → `return`, см. §2) запускает снаряд так же, как игра. Всё ◻ — не проверено в бою.

**Общее для обоих типов**
1. Источник: `Transform_CreateUnit.position` (x, y); z — слой (в ветках константа порядка −0.1…−1 или фикс.; можно взять z существующей ветки того же пула).
2. Пул: `C_Battle_Manager.List_<X>_Empty` (static, офсеты §2). Проверить `Count > 0` (у игры проверки нет), взять `[0]`, `Remove(obj)` из списка, `transform.position = …`, `SetActive(true)`. Не `Instantiate` — снаряд сам вернётся в свой список.
3. Числа героя (`this` — `C_Battle_AllyUnit`, на котором вызван метод): `UnitApply_<Spell>Damage/Size/MSpeed/PierceNum/Area`, `UnitApply_CriMultiplier`; шанс крита — static `C_Battle_AllyUnit.UnitApply_<Spell>_CriRan` (или `this.UnitApply_CriRan`).
4. Урон: `Damage × DamageRatio [× PowerVar01]`. MagicID выбирать из существующих (синергии, статистика урона и звуки снаряда идут по нему); ExplosionID/BulletID снаряда — ID существующей ветки, поведение которой нужно (снаряд ветвится по нему внутри себя). Свой ID ≥ 1000 в снаряд **не передавать** — его код не знает таких ID ◻.

**Тип B (`C_Battle_AllyBullet`)** — шаблон: Магическая пуля (1).
- Пул `List_BulletAlly_Empty` (0xc0), компонент на объекте: `GameObject.GetComponent<C_Battle_AllyBullet>()` (для Электро/Энергопули/Духа/Вспышки — на `GetChild(0)`).
- `BulletAttack(BulletID = 1, ExplosionID = 0, MagicID = 1, src.x, src.y, z, EGUnit_X, EGUnit_Y, EGUnit_Z, MagicBulletDamage × DamageRatio, MagicBulletAccuracyAim, AccuracyAimPlus, static 0x1f8, UnitApply_CriMultiplier, MagicBulletPierceNum, 0, MagicBulletSize, MagicBulletMSpeed, 0, 0, 0, 0, 0, 0)`.
- Со взрывом при попадании: ExplosionID = 6/8 (как Огненный шар/пуля 8) и ExplosionArea > 0 ◻.

**Тип E (`C_Battle_AllyExplosion`)** — шаблон: Молния (7).
- Пул `List_ExplosionAlly_Thunder_Empty` (0xf8) или общий `List_ExplosionAlly_Empty` (0xf0, им пользуются 83 и 100); позиция = точка цели (`EGUnit_X/Y`); компонент на `GetChild(0)` (у Ледяной новы — на объекте).
- `ExplosionAttack(ExplosionID = 7, MagicID = 7, ThunderDamage × DamageRatio, static 0x200, UnitApply_CriMultiplier, ThunderArea, 0, 0, 0, 0, StunTime 0, FreezeTime 0, HitNum 0)`.
- Взрыв без магии (как HP-шар/Уровень): MagicID 0, CriRan = `this.UnitApply_CriRan`.

Вызвать из обработчика можно и сам `Bullet_Create` с существующим ID и своим `DamageRatio`/`PowerVar` — это проще, если нужен лишь «другой урон» у готового снаряда (при `Replace` — осторожно с рекурсией, §5.1).

## 5. Привязка к единственному герою

- **Статические** поля `C_Battle_AllyUnit` (общие для всех экземпляров): `UnitApply_<Spell>_CriRan` (0x1f8…0x238), `UnitApply_EnergyBulletPowerRan` (0x1dc), `UnitApply_EnergyBulletExplosionArea` (0x1e0), `ApplyFreezeTime` (0x70), `ApplyStunTime` (0x74), `ApplyID_Incinerate` (0x26c); у `C_Battle_Manager` — `TestAim` (0x250). Второй герой с другим набором улучшений получит чужие значения.
- Статические пулы `C_Battle_Manager` — общие, это нормально.
- Остальное читается из `this` (урон, размер, скорость, `CriMultiplier`, `Obj_Unit*Mid`, `Transform_ObjUnit`, `Script_SoundManager`, `Tsunami_PosX/Y`). Ветки луча, торнадо 39/95/104, 27/75, 53, 68, 91 привязывают снаряд к объектам самого `this` — для союзника, вызывающего метод на герое (компаньоны, Doppelganger), луч/сфера окажутся у героя.
- Снаряды сами (в `BulletAttack`/`Unit_Damage` снаряда) могут читать статику героя — вне этого метода, не разбиралось.

## 6. Риски замены через `Replace`

1. **Нет «отката на оригинал» в API.** `Hooks.PrefixV` при наличии `Replace` всегда возвращает `false` — оригинал не вызывается; если замена бросила исключение, выстрел просто теряется (ошибка в `Crash`). Для переноса «по одному виду» нужно расширение ядра: например, `Replace` возвращает признак «не обработано → выполнить оригинал» (префикс вернёт `true`), либо отдельный `ReplaceIf(predicate)`. Вызвать `Bullet_Create` изнутри замены нельзя — рекурсия в тот же префикс.
2. **Производительность.** Метод вызывается на каждый снаряд (десятки в секунду на поздней игре). Harmony-префикс с `object[] __args` упаковывает 13 аргументов, `Slots` — поиск по словарю; плюс IL2CPP-обёртки на `GameObject/Transform/List` и вызовы `GetComponent<T>` через интероп. Желательно: кэш `Il2CppSystem.Type`/делегатов, без LINQ и аллокаций.
3. **Пулы.** Брать `[0]` и `Remove` (как игра); снаряды сами возвращаются в пул — свой код не должен держать ссылки и не должен создавать объекты через `Instantiate`. Пустой пул — проверять `Count`.
4. **Аргументы стартов** теперь известны (§2.1), но точный порядок `SpecialVar` у `Start_MeteorLand` и значения констант `DAT_*` (z-слои, границы `Random`) по декомпиляту не видны — их надо снять из памяти/дизассемблера. Ошибка в `MagicID` влияет на синергии и статистику урона в журнале.
5. **Побочные эффекты внутри ветки:** звук (62, 110), `Random` (порядок вызовов меняет поток случайных чисел — не критично), `SetParent` к объектам героя, `IceNovaAreaNum += 1` в 35.
6. **Поля, которые ставятся здесь:** кроме `IceNovaAreaNum` (35) метод полей `this` не пишет; все поля снаряда заполняются внутри стартов — их и надо вызывать, а не писать поля снаряда вручную.
7. **Компаньоны и ультимейты** вызывают метод с чужим `Transform_CreateUnit` — замена должна учитывать, что источник не герой, а статы — героя (`this`).
8. **Конфликт модов:** `Replace` эксклюзивен (`ModConflictException`), заменить сможет только одна библиотека (логично — `modkit_spells`/`modkit_combat`).

## 7. План переноса (оценка)

1. ~~Перевыгрузить декомпилят целиком, разобрать хвосты~~ — сделано (§2.1).
2. Расширить ядро: замена с возвратом «не обработано» (½ дня + проверка). Для своих ID ≥ 1000 достаточно постфикса/префикса, ловящего только эти ID: оригинал их и так игнорирует.
3. Скелет + таблица данных для «простых» видов: Магическая пуля, Огненный шар, Дух, Электро, Молния, Метеор, Ледяная нова, Буря, Призрак, HP-шар, Уровень — 1–2 дня с проверкой `autotest.py` по каждому.
4. Особые: луч (привязка), Энергопуля, Цунами, Испепеление-цикл, Вспышка, Лавовое поле, Торнадо, Гравитация, Созвездие, Щит, Лонгинус — 2–3 дня.

Основная оставшаяся неизвестность — кто присваивает `BulletID_*` (соответствие ID улучшениям) и константы `DAT_*`.
