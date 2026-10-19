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
Ссылку на `Il2CppACTk.Runtime` моду добавлять не нужно, пока он не читает `ObscuredInt/Float` сам (для этого есть `ctx.Game`).

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

## 3. `ctx.Game` — состояние игры (новое, [не проверено])

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
| Герой | `ctx.OnHeroPass(view => …)`, `ctx.DefineEffect`, `ctx.SetGlobalEffects` (эффекты подставляются при пересчёте, `docs/EFFECTS.md`) |
| Заклинания | `ctx.Spells`: `Level`, `CanCast`, `SetAuto`, `Cast`, `Cooldown`, `AddActive` (кнопка активного каста) |
| Данные игры | `new Table("Class")`: `Get/Set/CloneRow/IndexOf`; библиотека `modkit_data` (`Edit/Add/AddToList`, откат при выключении) |
| Реестры | `ctx.Registry<T>("classes")` — общие данные между модами (классы `CustomClass`) |
| Интерфейс | `UiKit` (окна, кнопки, `PanView` — карта с прокруткой, `Pager`), `OnMainMenu`, `OpenSettings` |
| Диагностика | `ctx.Log/Warn/Error/Guard/Mark`; журнал `UserData/ModKit/modkit.log`, `gamelog.txt` |

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
