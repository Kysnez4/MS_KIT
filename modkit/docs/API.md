# Справочник ядра ModKit (`ModKit.dll`)

Ядро — «мод-менеджер»: контракт мода, хуки, события, таймеры, хранилище, окна, зависимости. Всё, что знает устройство игры (враги, заклинания, эффекты, таблицы), живёт в библиотеках: [LIBRARIES.md](LIBRARIES.md). Все типы лежат в `namespace ModKit`. Пометка ✅ — проверено на устройстве, ◻ — собрано, в игре не проверялось.

## 1. `ModContext` (параметр `ctx`)

| Член | Что делает |
|---|---|
| `Id` | id мода |
| `Settings`, `Setting(key)`, `SetSetting(key, v)` | настройки мода (см. [MOD_FORMAT.md](MOD_FORMAT.md) §3) |
| `Log(msg)`, `Warn(msg)`, `Error(msg)` | журнал (`[id] …`), предупреждение в карточке, ошибка с подсчётом |
| `Guard(what, fn)` | выполнить под защитой, исключение → журнал, серия ошибок отключает мод |
| `Mark(what)` | метка для журнала вылетов (дёшево, можно в горячем коде) |
| `Events` | подписка и рассылка событий, §3 |
| `Expose(api)`, `Mod<T>(id)` | отдать свой API другим модам / взять чужой (`null`, если мода нет или он выключен) |
| `Use<T>(libraryId)` | API библиотеки для этого мода (расширение): `ctx.Use<ICombat>("modkit_combat")` |
| `CleanupList()` | список `Action`, которые выполнятся при выключении мода (расширение, для библиотек) |
| `Before/After/Replace(класс, метод, число_параметров, fn)` | хуки на методы игры, §2 |
| `Every(ms, fn)` | периодический вызов на главном потоке (реальное время) |
| `OnCleanup(fn)` | выполнить при выключении мода |
| `Registry<T>(name)` | общий реестр между модами, §5 |
| `Timer` | таймеры по игровому времени, §4 |
| `Store` | постоянное хранилище мода (JSON), §4 |
| `Tags` | общие теги заклинаний, артефактов, врагов, §4 |
| `Screen` | где игра и что на экране, §7 |
| `Toast(текст, сек)` | подпись на экране по игровому времени |
| `ReadAsset(путь)` | байты файла из zip мода или `null` |
| `Lang` | код языка игры: `ko`, `en`, `ja`, `zh_cn`, `zh_tw`, `es`, `de`, `pt`, `ru` (`Z_Database.Game_SystemLanguage` 1..9) |
| `T(ключ, запасной)` | перевод из `lang/<язык>.json` в zip мода: язык игры → `en` → `ru` → запасной текст (или сам ключ). См. MOD_FORMAT.md §9 |

Всё, что регистрируется через `ctx`, снимается при выключении мода.

## 2. Хуки на методы игры
```csharp
ctx.After("C_Battle_Manager", "Start", 0, (self, args) => { /* после */ });
ctx.Before("C_Battle_AllyUnit", "Unit_Damage", 3, (self, args) => { /* до */ });
ctx.Replace("Класс", "Метод", 1, (self, args) => результат);   // оригинал не вызывается; для void результат игнорируется
```
- Класс — имя из `Assembly-CSharp` или `"Сборка:Класс"`; третий аргумент — число параметров (выбирает перегрузку). `self` — Il2Cpp-обёртка `this`, `args` — аргументы (приводите через `Convert.ToDouble`, `(int)`…).
- Ставится один Harmony-патч на метод; хуки разных модов работают вместе. `Replace` может занять метод только один мод (иначе `ModConflictException`).
- Недоступно: хуки *внутри* функции (по инструкциям) и IL-трансформеры: методы игры — нативный код.
- Не вешайте тяжёлые хуки на `Update` и `Unit_Damage` врагов: сотни экземпляров за кадр. Хук дольше 8 мс пишется в журнал как «медленный».
- Имена классов и методов ищите в `dump.cs` или декомпиляции: [DECOMP.md](DECOMP.md).

## 3. События

Подписка: `ctx.Events.On("имя", o => …)` (`o` — `EventData`, словарь с `Get<T>(ключ)`) или типизированная `ctx.Events.On<T>(t => …)`. Рассылка: `ctx.Events.Emit("мой_мод:событие", данные)` / `Emit<T>(объект)`. Подписки снимаются при выключении мода. Хуки событий ставятся при первой подписке и снимаются, когда подписчиков не осталось.

| Событие | Когда | Данные |
|---|---|---|
| `mainMenu` ✅ | построено главное меню | `menu` |
| `battleStart` ✅ | после `C_Battle_Manager.Start` | `manager` |
| `battleEnd` | до `End_Battle` | `manager` |
| `battleResult` / тип `BattleResult` ◻ | после `End_Battle` (нужна `modkit_game`) | `Time`, `Kills`, `AreaId`, `AreaLevel`, `ClassId`, `PracticeGained[24]` |
| `areaChange` | `Start_AreaChange` (путь «Обелиск»; обычная смена подзон — нет) | `manager` |
| `enemySpawn` | `Start_Unit` | `enemy` |
| `enemyDeath` ✅ | `Unit_Death` | `enemy`, `unitId` |
| `bossSpawn` | `Create_UnitEnemyBoss` | `manager`, `unitId`, `count` |
| `heroDamaged` ✅ | `Unit_Damage` героя | `amount` |
| `manaPickup` ✅ | `ManaGain` | `amount` |
| `itemPickup` ✅ | `Obtain_Item` | `type` (`ObtainItemType`), `objType`: 1 — шарики, 3 — руны и бонусы, 4 — сундук (вероятно) |
| `modEnabled` / `modDisabled` | мод включён / выключен | `id` |
| `RegistryChanged` (тип) | изменение общего реестра | `Registry`, `Id`, `Owner`, `Added` |

Свои события называйте с префиксом мода (`my_mod:событие`). Типы для событий между модами объявляйте в библиотеке.

## 4. Время, хранилище, теги

**`ctx.Timer` (`IGameTimer`)**: `Now` (игровое время с запуска мода), `Delta`, `After(сек, fn)`, `Every(сек, fn)`. Время идёт только в бою и стоит на паузе; в отличие от `ctx.Every` (реальное время).

**`ctx.Store` (`IStore`)**: `Get<T>(key, def)`, `Set<T>(key, value)`, `Has`, `Remove`. JSON в `UserData/ModKit/store/<id>.json`; значения — числа, строки, bool, списки и объекты, понятные `System.Text.Json`. Пишется сразу при `Set`.

**`ctx.Tags` (`ITags`)**: `Set(kind, id, теги…)`, `Of(kind, id)`, `With(kind, тег)`; `kind` — `"spell"`, `"artifact"`, `"enemy"` или свой. Общие для всех модов, не сохраняются.

**`BattleState`** (статический): `InBattle`, `Paused`, `Hero`, `HeroKnown` — идёт ли бой сейчас, нет ли паузы, герой текущего боя.

## 5. Реестры и API между модами
- `ctx.Registry<T>("имя")` → `IRegistry<T>`: `Register(id, value)`, `Unregister(id)`, `TryGet`, `All`. Реестр общий для всех модов; записи мода убираются при его выключении; `T` — общий тип (из библиотеки или ядра). Пример: `class_menu` собирает свои классы из реестра `classes` (`CustomClass`).
- `ctx.Expose(объект)` / `ctx.Mod<T>("id")` — разовый API мода без модели библиотек. Для API «на мод» используйте `ILibrary<T>` и `ctx.Use` ([MOD_FORMAT.md](MOD_FORMAT.md) §5).

## 6. Окна и интерфейс (`UiKit`)

Экран логический: ширина `UiKit.LogicalW` = 720, высота `UiKit.ScreenH`, координаты от левого верхнего угла. Всё строится на Unity uGUI.

| Метод | Что |
|---|---|
| `OpenPage(name, title, subtitle[, onClose])` → `(go, rt, top)` | полноэкранное окно с заголовком и кнопкой закрытия; повторное открытие с тем же `name` заменяет окно |
| `Label(parent, text, x, y, w, h, size, align)`, `Tint(text, цвет)` | надпись |
| `AddButton(parent, text, x, y, w, h, size, цвет[r,g,b,a], onClick)` | кнопка |
| `AddNumberField(parent, значение, x, y, w, h, size, десятичное, onCommit)` | поле ввода числа |
| `AddScroll(parent, x, y, w, h)`, `SetContentHeight` | прокручиваемая область |
| `OpenChecklist(ChecklistOptions)`, `OpenNumberForm(NumberFormOptions)` | готовые окна: список флажков и форма чисел (пример: `auto_artifact`, `spell_tuner`) |
| `AddPanView(...)`, `OpenPanWindow(...)`, `PanOptions`, `PanNode` | карта с прокруткой и узлами (дерево достижений, `achievements`) ◻ |
| `AddPager(...)`, `PagerOptions` | листатель страниц ◻ |
| `AddHudLabel`, `AddHudBar`, `AddHudButton(name, text, x, y, w, h, size, цвет, onClick)` | подпись, полоса и кнопка поверх экрана боя, без перехвата касаний |
| `Picture(parent, png, x, y, w, h)`, `SetImageOverlay(image, png)` | своя картинка |
| `GameSprite(поле Z_Database, индекс)`, `GetSprite/SetSprite` | спрайты игры через указатели (обёртка `Sprite` роняет Mono) |
| `LoadSpriteBundle(key, bytes, onDone)` | свои спрайты из PNG ✅: папка `sprites/` мода → `sprites.bundle` (собирает `pack_mod.py`), в моде `UiKit.LoadSpriteBundle("id", ctx.ReadAsset("sprites.bundle"), d => …)`; `d[имя файла без .png]` — указатель на спрайт для `SetSprite`, `SpriteRenderer.set_sprite`, `SpriteList_*`. Опорная точка — центр, `spritePpu` в `mod.json` (по умолчанию 100), до 256 спрайтов на мод |
| `CreateSprite(…)`, `CanCreateSprite` | `Sprite.Create` — в движке игры вырезан, возвращает `IntPtr.Zero` (оставлено на случай другой `libunity`, см. `MELONLOADER.md`) |

Окно настроек мода: задайте `OpenSettings` в `ModDef`. Свои элементы в главное меню: `OnMainMenu`.

## 7. Экран и боты (`ctx.Screen`) ✅

`Name` (`mainMenu`, `battle`, `paused`, `spellSelect`, `artifactSelect`, `dead`, `loading`), `Size`, `Buttons()`, `Texts()` (с координатами экрана), `FindButton(текст)`, `Click(кнопка или текст)`. Нужен для автотестов без скриншотов ([DEBUGGING.md](DEBUGGING.md)).

## 8. Низкий уровень
- `Raw` — прямые вызовы IL2CPP по указателям (`Class`, `Method`, `Invoke`, `UnboxInt`…) там, где обёртка Il2CppInterop роняет Mono: `UnityEngine.Texture`, `Sprite`.
- `Hooks` — те же хуки без `ctx` (`AddBefore`, `AddAfter`, `AddReplace`, `FindType`, `FindMethod`); для библиотек.
- Поля и методы игры доступны как обычные C#-члены `Il2Cpp.*` из `Assembly-CSharp` (`Z_Database`, `C_Battle_AllyUnit` …).

## 9. Ограничения
- `GameObject.Find` недоступен: ищите через `FindObjectsOfType(Il2CppType.Of<T>())`.
- `ObscuredInt/Float` читать через `GetDecrypted()`; писать только через обёртку игры. Сохраняемые массивы с контрольной суммой не править (`Array_JournalProgress`, `Array_AreaBestScoreNum`, практика классов).
- Класс игры задаёт `Start_Ability` один раз; подмена `Game_BattleClassID` позже не работает. Свои классы: мод `class_menu` + `CustomClass`.
- Слоты эффектов класса 1..7 (слот 7 — мастерство), 8–9 игра у школ не читает.
- Покупки в клонах игры не работают.
