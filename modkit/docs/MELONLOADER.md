# Моддинг Magic Survival: возможности, ограничения и конвейер MelonLoader

Справочник по тому, что можно и нельзя сделать с игрой, как собирается APK с загрузчиком MelonLoader (LemonLoader) и какие проблемы при этом найдены. Нужен автору мода при выборе способа (Frida или C#) и тому, кто пересобирает APK или переносит ModKit на новую версию игры. API модов описано в `API.md`, `LIBRARIES.md`, `COOKBOOK.md`.

Версия игры 612, Unity 2022.3.62f3, IL2CPP (игра скомпилирована в нативный код `libil2cpp.so`, IL-кода нет), arm64, телефон Android 12. Два способа запуска кода в игре: **Frida** (старый Zombix на Frida, JS/TypeScript, `modkit/src`, см. `FRIDA.md`) и **MelonLoader/LemonLoader** (C#, `modkit/melon`, сборки Zombim и Zombix — см. раздел 3). Основная платформа — C#: ядро ModKit 2.0 + 16 библиотек `modkit_*`; Frida-версия старая.

✅ проверено на устройстве, ◻ не проверено.

## Коротко

- C# (MelonLoader; пакеты `com.vkslrzm.Zombix` — основная сборка, `com.vkslrzm.Zombim` — сборка для разработки, по умолчанию в скриптах) — основной путь: ядро-мод-менеджер и 16 библиотек `modkit_*` (`ctx.Use<T>`), Harmony-хуки на вход метода, правка таблиц, свои эффекты, интерфейс, библиотеки `modkit_*`. Frida-версия (`modkit/src`) остаётся рабочей, но с лагом на хуках; удалять её нельзя, пока не перенесены `spirit_more`, `satellite_more` и другие встроенные TS-моды.
- Harmony ставится только **на вход** метода; править IL и ставить хук внутрь функции нельзя. Срез числа снарядов до раскладки возможен только Frida-хуками по адресам `libil2cpp.so` (56 мест, версия 612) или патчем байтов.
- На Mono-рантайме телефона нельзя использовать обёртки `Texture`/`Sprite` из Il2CppInterop и лямбды с замыканием для делегатов Unity: нужны прямые вызовы по указателям (`Raw.cs`) и методы открытого класса (`UiKit.Callback`).
- Сохранения правятся только набором целиком (26 файлов), иначе игра сбрасывает прогресс. Покупки в клонах не работают.
- Нативные сбои (`SIGSEGV`, `Scudo`) из C# не поймать: для них журнал вылетов `Crash.cs`, бот `autotest` и бисекция модов.
- Проверять игру нужно по журналам (`modkit.log`, `gamelog`) и `ctx.Screen`, а не по скриншотам.

## Содержание

1. [Возможности и ограничения](#1-возможности-и-ограничения)
2. [Как выбрать способ](#2-как-выбрать-способ)
3. [Как устроен конвейер сборки APK и загрузчика](#3-как-устроен-конвейер-сборки-apk-и-загрузчика)
4. [Найденные проблемы и обходы](#4-найденные-проблемы-и-обходы)
5. [Перенесённые моды](#5-перенесённые-моды)
6. [Подсистемы ModKit](#6-подсистемы-modkit)
7. [Прогресс в Zombim](#7-прогресс-в-zombim)
8. [Ограничения и грабли](#8-ограничения-и-грабли)
9. [История и тупики](#9-история-и-тупики)

## 1. Возможности и ограничения

### Можно (работает)

| Что | Frida (JS) | MelonLoader (C#) |
|---|---|---|
| Читать и писать поля героя и любых классов (`UnitApply_*`, `Z_Database.*`), вызывать методы игры | ✅ | ✅ (`Unit_StatBuff`: `UnitApply_SatelliteNum` прочитан) |
| Выполнять свой код до/после метода игры | ✅ `ctx.before/after` | ✅ Harmony Prefix/Postfix, `ctx.After/Before` |
| Заменить метод целиком или подменить его результат | ✅ `ctx.replace` | ✅ Harmony Prefix + `__result` (`ctx.Replace`) |
| Править таблицы данных игры (Ability, MagicCom, Synergy…) | ✅ | ✅ `modkit_data` |
| Добавлять артефакты, комбинации, синергии | ✅ | ✅ (артефакт 561, синергия 101, комбинация 66) |
| Свои эффекты (ID ≥ 10000) и подстановка настоящих эффектов игры при пересчёте | ✅ `effects.ts` | ✅ `modkit_effects` (`IEffects`), `OnHeroPass` |
| Лимит снарядов: срез после пересчёта + бонус урона/стата | ✅ | ✅ (пуля 8→3 + бонус, по gamelog) |
| Срез числа до раскладки (хуки сразу после записи поля) | ✅ 56 мест по адресам `libil2cpp.so` | ❌ (Harmony ставится только на вход метода) |
| Свои окна, кнопки, настройки, иконки, скриншоты в карточке мода | ✅ | ✅ (иконки и скриншоты — через `Raw.cs`) |
| Клонировать объекты (`Instantiate`), менять Transform | ✅ `satellite_more` | ◻ |
| Играть свой звук | ✅ matador_boss (OpenSL ES) | ◻ |
| Zip-моды: `mod.json` + скрипт/DLL, зависимости, события, реестры | ✅ JS | ✅ DLL + `IKitMod` |
| Читать и менять сохранения (формат и ключ известны) | ✅ | ✅ (`magic-survival-save-format`, импорт прогресса) |
| Читать код игры: декомпилятор Ghidra, дамп `dump.cs`, поиск «кто пишет/читает поле» по инструкциям | ✅ | ✅ |
| Регистрировать свои C#-классы (`MonoBehaviour`) через Il2CppInterop `ClassInjector` | — | ◻ (так регистрируются служебные типы MelonLoader) |

### Нельзя (принципиально)

- **Править IL игры** (Harmony `Transpiler`) и **ставить Harmony внутрь функции**: методы игры — нативный код, IL нет; хук только на вход.
- **Загрузить скомпилированный C# DLL в Frida-версию** и **`Reflection.Emit`/`Assembly.Load` для кода игры**: у IL2CPP нет JIT.
- **Нативные `CModule`-хуки Frida в бою**: установка проходит, при срабатывании игра падает (`SIGSEGV`, `pc` в сгенерированном коде). Причина не найдена (подробно — раздел 9).
- **Покупки в клонах** (Zombix/Zombim): не работают, принято.
- **Правки сохранений без контрольных сумм**: набор из 26 файлов копируется целиком, иначе игра сбрасывает прогресс.

### Очень трудно (возможно, но дорого и хрупко)

| Задача | Почему трудно | Как делали / чем рискуем |
|---|---|---|
| Срезать число снарядов **до** раскладки спутников/духов | число пишется внутри огромных `AbilityStatApply` и `StartInformation_B`, дальше в той же функции читается | Frida: хуки по 56 адресам (держатся на версии 612, любое обновление игры ломает таблицу `STORES`). C#: только после пересчёта, круг может слипнуться. Радикально — патч байтов `libil2cpp.so` по результатам Ghidra |
| Править инструкции игры (бинарный патч `libil2cpp.so`) | привязка к одной версии, ошибка в байтах = крэш при запуске, нужна пересборка APK | конвейер `melon_build.sh` умеет подменять библиотеки, но патч байтов ещё не делали |
| Читать огромные функции | Ghidra не декомпилирует `AbilityStatApply` (таймаут) | дизассемблер кусками, сканирование `str/ldr` по смещению поля |
| Переживать обновление игры | смещения полей, RVA, адреса записи, `libunity`/`melon_data` под конкретную версию Unity | при обновлении заново: `dump.cs`, скан инструкций, сборка APK |
| Запуск MelonLoader на Unity 2022.3.62f3 | у LemonLoader (0.6.5.1, март 2025) нет `libunity` для f3; пришлось: родной `libunity`, правка `Il2CppInterop` (`Single`→`First`), явная версия Unity; часть icall (`Scene::GetBuildIndexInternal`) не разрешается | 2–3 минуты генерации сборок при первом запуске; новые версии Unity могут сломать |
| Картинки и спрайты из C# | обёртка `UnityEngine.Texture`/`Sprite` из Il2CppInterop роняет игру под Mono (`SIGABRT`) | прямые вызовы IL2CPP по указателям (`Raw.cs`, `UiTextures.cs`) |
| Делегаты Unity из лямбд с замыканием | падает `DelegateSupport.ConvertDelegate` | методы открытого класса (`UiKit.Callback`) |
| Отладка нативных крахов (`Scudo`, `SIGSEGV`) | из C# и JS не поймать | `logcat` + журнал меток `Crash.cs` (`lastrun.txt`, `running.lock`), бот `autotest`, бисекция модов |
| Массовые Harmony-хуки (~300 методов `gamelog`) | установка на лету роняла игру (Scudo) | трассировка выключена по умолчанию |
| Показать настоящее число в окне характеристик (`14 (270)`) | нужны индексы `Array_BattleStatus_Value` | пробный лог индексов, пока не делали |
| Бонусы за количество («Волшебная палочка», «Термоядерная реакция» и др.) после обрезки числа | неизвестно, считает ли игра их внутри пересчёта или каждый раз по текущему числу | нужен замер в игре |
| Совместимость модов между собой | конфликты `Replace`, общие поля, `satellite_more` против лимита снарядов | зависимости и конфликты в `mod.json`, предупреждения в карточке |
| Эмулятор вместо телефона | AVD Pixel_5 на `x86_64` с 16K-страницами, трансляция arm | пока проверка только на реальном устройстве |

## 2. Как выбрать способ

| Задача | Лучше |
|---|---|
| Поменять значение поля, таблицу, добавить артефакт, свой эффект | любой, удобнее C# |
| Обернуть метод игры (до/после, подмена результата) | C# (Harmony) |
| Влиять на что-то **посреди** функции, до чтения значения | Frida по адресу или патч `libil2cpp.so` |
| Нужен новый компонент/класс в сцене | C# (`ClassInjector`) ◻ |
| Быстрый прототип без пересборки APK | Frida (JS-zip, правка `loader.js`) |
| Долгоживущий мод со своим интерфейсом | C# `ModKit` |

## 3. Как устроен конвейер сборки APK и загрузчика

LemonLoader 0.6.5.1 — форк MelonLoader для Android. Результат: C#-мод загружается и Harmony-патч на метод игры срабатывает (`[ProjectileCap] Unit_StatBuff: UnitApply_SatelliteNum = 2`) ✅. Игра ставится отдельным пакетом рядом с оригиналом (свои сохранения): `com.vkslrzm.Zombix` (основная сборка, ModKit загружает 28 zip без ошибок ✅) или `com.vkslrzm.Zombim` (по умолчанию в `melon_build.sh` и `modkit_deploy.sh`). Ниже `<пакет>` — любой из двух; в примерах путей стоит Zombim.

### Сборка APK

`bash modkit/tools/melon_build.sh` делает всё:

1. `modkit/tools/build_melon_base.py` — чистый APK (оригинальные base + split arm64, **без Frida**), пакет `com.vkslrzm.Zombim` (то же переименование, что у Zombix).
2. Патч библиотекой установщика LemonLoader без GUI: `modkit/tools/lemon_patch` (консоль над `MelonLoader.Installer.Core.dll`).
3. Установка на телефон, выдача прав на файлы, запуск.

Выбор пакета: `PKG=com.vkslrzm.Zombix bash modkit/tools/melon_build.sh` (`BUILD_ONLY=1` — без установки на телефон). Ядро для Zombix — архив `ModKit-core.zip` в релизе `modkit-core` (папка `Mods` — в `/sdcard/MelonLoader/com.vkslrzm.Zombix/`), библиотеки и моды — релизы `modkit-libs` и `modkit-mods`.

Внешние файлы в `D:\tools\lemon`: `installer` (lemoninstaller-win-x64.zip), `deps\libunity.so.arm64-v8a` (LavaGang/MelonLoader.UnityDependencies 2022.3.62), `deps\melon_data_patched.zip`. Патчер **удаляет переданные ему файлы** — скрипт каждый раз делает свежие копии.

Первый запуск генерирует прокси-сборки Il2CppInterop (~2–3 минуты, чёрный экран).

### Загрузчик ModKit на C#

Проект `modkit/melon/ModKit` (net8.0) — MelonMod `ModKit.dll`, заменяет `core.ts`, `events.ts`, `ext.ts`, `ui.ts`, `uikit.ts`. После версии 2.0 это только мод-менеджер (`Api.cs`, `Core.cs`, `Hooks.cs`, `Events.cs`, `External.cs`, `Registries.cs`, `BattleState.cs`, `MenuUi.cs`, `Ui*.cs`, `Extras*.cs`, `Screen.cs`, `Crash.cs`); игровая логика вынесена в библиотеки. Данные лежат в `/sdcard/MelonLoader/com.vkslrzm.Zombim/UserData/ModKit` (`config.json`, `modkit.log`, `mods/*.zip`); архивы ищутся в `Download/ModKit` и `Download`.

| Было (JS) | Стало (C#) |
|---|---|
| `ModKit.define({...})` в `main.js` | класс с `IKitMod.Define()` в DLL; в `mod.json` поле `main` = `имя.dll` |
| `ctx.replace/after/before` | `ctx.Replace/After/Before` (Harmony, `Hooks.cs`); класс — имя из Assembly-CSharp или `Сборка:Класс` |
| `ctx.events.on` | `ctx.Events.On(имя, e => ...)`, данные — `EventData` |
| `ctx.every` | `ctx.Every` (главный поток, `OnUpdate`) |

Типы между модами не делятся (каждый DLL грузится из zip): API через `Expose` — словарь или интерфейс из `ModKit.dll`. Формат zip — `MOD_FORMAT.md`.

Проверено на устройстве ✅: окно «Моды» и иконка на главной, «+» (поиск, установка), зависимости (автовключение и выключение зависимых), `Expose`/`Mod<T>`, настройки (кнопки, перезапуск мода), `Every`, событие `battleStart` и `After`-хук, долгое нажатие и удаление, сохранение `config.json`, диалог «Моды изменились» после перезапуска.

Не проверено ◻: `Replace` (в т. ч. с результатом), конфликты замен, обновление/откат версии, плашка «Выйти и применить», перестройка окна после возврата из боя, `OnMainMenu` у модов.

Примеры: `modkit/melon/examples/hello`, `hello_addon`; сборка в zip — `python modkit/melon/pack_mod.py hello hello_addon`; заливка ModKit и перезапуск — `bash modkit/tools/modkit_deploy.sh`.

### Первый C#-мод (`ProjectileCap`, исторический)

- Проект `modkit/melon/ProjectileCap` (net8.0, самостоятельный MelonMod до появления ModKit; сейчас лимит снарядов — zip-мод `melon/mods/projectile_cap`); ссылки на сборки берутся из копии с телефона: `adb pull /sdcard/MelonLoader/com.vkslrzm.Zombim/MelonLoader/net8` и `.../Il2CppAssemblies` в `D:\tools\lemon\ref\net8` и `\il2cpp`.
- Типы игры — `Il2Cpp.C_Battle_AllyUnit`, `Il2Cpp.V_Ability_Manager` и т. д.; поля героя — свойства.
- Сборка: `dotnet build -c Release`; заливка: `adb push ...ProjectileCap.dll /sdcard/MelonLoader/com.vkslrzm.Zombim/Mods/`; лог: `/sdcard/MelonLoader/com.vkslrzm.Zombim/MelonLoader/Latest.log`.

### Ghidra и GhidraMCP (вне репозитория, `D:\tools`)

- Ghidra 12.1.4, JDK 21 (переносимый Temurin; winget-установщик падал с кодом 1603), GhidraMCP 2.0 (LaurieWired), окружение Python 3.12 `D:\tools\ghidra\venv`.
- `libil2cpp.so` игры извлечён в `D:\tools\ghidra\targets`; проект `D:\tools\ghidra\project` (`zombie612`).
- Запуск: `powershell -File modkit\tools\start_ghidra.ps1` (headless, `http://127.0.0.1:8081/mcp`, подключён через `.mcp.json`). Сервер живёт, пока открыт его процесс: после перезапуска ПК или сессии его нужно запустить заново (иначе `ECONNREFUSED`), проект при этом открывается быстро.
- Адрес в Ghidra = RVA из `dump.cs` + `0x100000`.
- `AbilityStatApply` (огромная функция) Ghidra не декомпилирует (таймаут) — читать дизассемблером.
- Найдено: `ApplySatelliteGroup` берёт `round(UnitApply_SatelliteNum)` и ставит центр `i` на угол `360 / n × i`; вызывается только из `StartInformation_B`.

## 4. Найденные проблемы и обходы

### Запуск MelonLoader на Unity 2022.3.62f3

| Проблема | Причина | Решение |
|---|---|---|
| «Unable to initialize the Unity Engine», `Invalid serialized file version` | LemonLoader подставляет `libunity.so` сборки 2022.3.62**f1**, игра на **f3** | ставить **родной** `libunity.so` игры (`UNITY_LIB=orig`, по умолчанию); правка строки версии в f1 запускалась, но падала (Scudo `invalid chunk state`; причина — не версия, а модули полного движка без C#-части в игре, см. ниже) |
| `InjectorHelpers..cctor`: «Sequence contains more than one matching element» | `libil2cpp.so` отображён несколькими кусками (сегменты по 16 КБ с зазорами), .NET считает каждый отдельным модулем, а Il2CppInterop делает `Single` | `modkit/tools/il_patch` (Mono.Cecil): `Single` → `First` в трёх копиях `Il2CppInterop.Runtime.dll` внутри `melon_data.zip` |
| Автоопределение версии Unity падает | поток записи в zip без перемотки | версия задаётся явно (`UNITY_VERSION`, по умолчанию `2022.3.62f3`) |
| `Sprite.Create` и часть icall отсутствуют | игра собрана с вырезанием движка: в родной `libunity` 1048 icall, в полной 7557 | **Полная `libunity` не подходит** ✅ (проверено 2026-10-23): Release f3 из Android Build Support (`UNITY_LIB=full`) падает и с MelonLoader, и в чистом APK. Движок вызывает в цикле кадра C#-методы `NativeInputSystem` (`NotifyBeforeUpdate` и др.), вырезанные из метаданных игры: «Unable to find method …», затем SIGSEGV в `libil2cpp` (адрес 0x52) из `libunity`. Та же причина у падения f1 (Scudo `invalid chunk state` под MelonLoader), а не разница f1/f3. Development-вариант f3 (`UNITY_LIB=dev`) зависает при старте. `UiKit.CreateSprite` с родной `libunity` возвращает `IntPtr.Zero` |
| Остаточные ошибки | у LemonLoader нет `libunity` для f3; часть внутренних вызовов Unity не разрешается (`Scene::GetBuildIndexInternal`, `SetAsLastSibling`) | пока не мешают, игра идёт |

### Рантайм Mono и интерфейс ✅

- Рантайм на телефоне — **Mono**. Обёртка `UnityEngine.Texture`/`Texture2D` из Il2CppInterop роняет игру (`mono_class_from_mono_type_internal: implement me` → SIGABRT) даже при обращении к `Il2CppClassPointerStore<Texture>`. Обход — прямые вызовы IL2CPP по указателям (`Raw.cs`, `UiTextures.cs`): так работают иконки и скриншоты модов (PNG/JPG из zip) и скруглённые кнопки.
- Делегаты Unity (`UnityAction`) из лямбд с замыканием роняли игру при `DelegateSupport.ConvertDelegate` — вместо них методы открытого класса `UiKit.Callback`.
- `Time.realtimeSinceStartupAsDouble` не разрешается (icall) — таймеры на `Stopwatch`.
- `Harmony` внутри пространства имён `Il2Cpp` писать как `HarmonyLib.Harmony`.
- `ToString()` боксированных чисел через обёртку возвращает мусор — значения читаются через `Unbox<T>()`.
- Спрайты `SpriteList_*` растут и копируются через указатели (`Sprites` в `melon/mods/modkit_data/Commands.cs`): обёртка Sprite опасна так же, как Texture.

### Хуки Harmony и данные игры ✅

- **Хуки Harmony с `__result` обнуляли характеристики героя** (все `UnitApply_*` = 0, ампла −100 %, перезарядка −100 %) при включённой трассировке `gamelog`. Теперь префикс с `__result` ставится только на методы с `Replace`; на обычных «до/после» — префикс без `__result` (`Hooks.cs`). Без трассировки (`trace=0`) характеристики нормальные (Attack 137, перезарядка 0,845).
- **Трассировка вызовов `gamelog` выключена по умолчанию** (`trace`, ~300 хуков): установка на лету дважды роняла игру (SIGSEGV/Scudo). Включается в «Настройки» → «Опасно».
- **Новая синергия ломала вход в бой** (`IndexOutOfRangeException` в `V_Ability_Manager.Start_Ability`): `Array_SynergyObtain` индексируется по ID синергии, а ID 101 вне массива. `AddSynergy` теперь растит массив (`Sprites.GrowInts`).
- Артефакт 561 (без синергии и комбинации) в бою выдаётся `auto_artifact` без исключений.

### Ограничение Harmony для эффектов

Хуки внутри функций (по адресам `libil2cpp`) недоступны, поэтому «Лимит снарядов» на C# режет число после пересчёта, а не в его середине (подробнее — раздел 6, «Эффекты»).

## 5. Перенесённые моды

C#, `modkit/melon/mods`. Сборка: `python modkit/melon/pack_mod.py auto_artifact gamelog`. Класс `Table` (чтение, запись, клонирование строк) теперь в `modkit_data` (`Data.cs`).

| Мод | Версия | Что проверено | Не проверено / замечания |
|---|---|---|---|
| `auto_artifact` | 2.0 | ✅ выдача артефакта 210 на старте боя | не перенесена диагностика `Dictionary_ApplySynergy` |
| `gamelog` | 2.0 | ✅ INIT/SET/CALLS в меню и в бою. Журнал игры: снимки полей по смещениям IL2CPP, трассировка вызовов (Harmony, ~300 методов, установка ~12 с), STATUS и ART | ◻ STATUS, ART/OFFER; редкий вылет при включении (Scudo) наблюдался дважды, не воспроизведён |
| `projectile_cap` («Лимит снарядов») | — | ✅ через `OnHeroPass`: пуля 8 → 3 + бонус урона, +30 % урона от эффекта 10001, гром +5 (по gamelog) | режет после пересчёта; таблица лимитов — раздел 6 |

Что было во Frida: `ctx.data` (таблицы, артефакты, комбинации, синергии, эффекты: `data.ts`, `artifacts.ts`, `commands.ts`, `effects.ts`) перенесено в библиотеки (раздел 6). Остальные моды ядра 2.0 (`achievements`, `active_pack`, `api_probe`, `area_mechanics`, `art_swap`, `artifact_pack`, `autotest`, `class_menu`, `class_practice`, `combat_probe`, `kill_streak`, `spell_tuner`) — на новом API (`CHANGELOG.md`). Встроенные TS-моды (`spirit_more`, `satellite_more` и др.) и JS-моды из zip остаются во Frida, `modkit/src` пока удалять нельзя.

## 6. Подсистемы ModKit

Краткий обзор; актуальный API — `API.md`, список библиотек — `LIBRARIES.md`, рецепты — `COOKBOOK.md`, отладка — `DEBUGGING.md`.

### Библиотеки, события, реестры

**Библиотека** — обычный zip-мод с `"library": true` в `mod.json`. Не показывается в списке, включается сама, когда включён зависимый мод (`depends`), и выключается, когда зависимых не осталось (выключение ◻). Библиотеки загружаются раньше остальных; их DLL регистрируются по имени сборки (`AssemblyResolve`), поэтому мод, собранный со ссылкой на `lib.dll`, получает **те же типы**. Дополнительные DLL в zip — поле `"assemblies": ["x.dll"]`. Если библиотеки нет, мод не ставится с сообщением «сначала установите её». Подключение: `ctx.Use<IX>("modkit_x")`.

- **Типизированные события:** `ctx.Events.On<T>(e => ...)`, `ctx.Events.Emit(new T{...})`; ключ — полное имя типа.
- **Реестры:** `ctx.Registry<T>("имя")` → `Register(id, значение)`, `TryGet`, `All`; записи мода убираются при выключении, изменения — событие `RegistryChanged`.
- `ctx.Warn(текст)` — предупреждение в карточке мода.
- Примеры: `examples/demo_lib` + `demo_user` (общие типы, события, реестр ✅), `examples/data_demo`.

### Данные игры (`modkit_data`, `modkit_artifacts`, `modkit_combos`, `modkit_synergies`)

Раньше доступ шёл через `ModKitData.Data.For(ctx)`; теперь `ctx.Use<IData>("modkit_data")`: `WhenReady`, `Table`, `Edit`, `Add` (клон строки), `AddToList`; всё откатывается при выключении мода (откат ◻). `ModKit.Table` умеет читать, писать, клонировать строки. Проверено ✅: чтение ID, правка ячейки, добавление строки.

Артефакты, комбинации, синергии (раньше `modkit_data` 1.1; теперь рецепты в `modkit_artifacts`, `modkit_combos`, `modkit_synergies`). `IData`: `AddArtifact(s)`, `EditArtifact`, `RemoveArtifact`/`RestoreArtifact`, `AddCombo(s)`, `EditCombo`, `AddSynergy/Synergies`, `RemoveSynergy`, `SynergyAddArtifact`/`SynergyRemoveArtifact`; описания — `ArtifactDef`, `ComboDef`, `SynergyDef`, `Effect(id, значение)` (`Defs.cs` в `modkit_data` и `modkit_artifacts`). Вызывать из `WhenReady`; всё откатывается при выключении мода.

- Новый артефакт: строка в `Ability`, пулы выпадения, запись `Dictionary_ApplySynergy`, плитка и окно описания в энциклопедии (вкладка с кубком), своя иконка `IconFile` (только в энциклопедии, через `UiKit.SetImageOverlay`).
- ✅ На `examples/data_demo`: артефакт (ID 561), синергия (101), комбинация (66) добавлены; массив иконок вырос с 401 без сбоя; плитка и окно описания в энциклопедии показываются.
- ◻ Откат при выключении, `IconFile`, редкость/пулы в бою, `EditArtifact`/`RemoveArtifact`, правка комбинаций и синергий, `FitBookHeight` при нескольких новых артефактах.

Бой для проверки: Играть → Выбрать → Войти в зону; значения героя и списки читаются из `gamelog.txt` (`INIT/SET hero.*`, `List_ArtifactObtain`).

### Эффекты (`modkit_effects`, `IEffects`)

Движок эффектов (перенос `effects.ts`) раньше жил в ядре (`melon/ModKit/Effects.cs`, методы `ModContext`: `DefineEffect`, `SetGlobalEffects`, `UseBuiltinEffects`, `OnHeroPass`, `RecalcStats`); теперь это `IEffects` (код — `melon/mods/modkit_effects/Effects.cs`):

- `DefineEffect(EffectDef)` — свой эффект (ID ≥ 10000) с `Apply(view, value, ctx)` и/или `Expand(value)` (подстановка настоящих эффектов игры на время `AbilityStatApply`);
- `SetGlobalEffects(pairs)` — эффекты без артефакта; `UseBuiltinEffects()` — встроенные 10001–10004;
- `OnHeroPass(view => ...)` — проход по герою после каждого пересчёта и раз в ~0,7 с. `view.Field(name).Value` отдаёт БАЗУ игры, запись задаёт цель; движок сам пишет в игру и возвращает поля к базе, когда проход их не трогает (правки не копятся и откатываются при выключении мода).

Пример: `melon/examples/effects_demo` (артефакт «Руна ModKit»: эффекты 10003 и 10001). ✅ Пуля 8 → 3 + бонус урона, +30 % урона от эффекта 10001, гром +5 (по gamelog). Список эффектов — `EFFECTS.md`.

### Заклинания: данные (`modkit_spells_data`)

Заклинания — строки `Ability` с ID 1..21 (`유형 = 마법`). Слот 1 эффектов — метка заклинания (`-ID`), слоты 2..9 — улучшения по уровням: **слот N действует на уровне N+1**. Текст каждого уровня лежит в `효과 설명줄06` через `^`, его API пока не правит. Раньше эти методы были в `modkit_data` 1.2; код — `melon/mods/modkit_spells_data/Spells.cs`.

- `data.GetSpell(id)` → `SpellInfo` (имя, описание, `MaxLevel`, `Slots`).
- `data.EditSpell(id, new SpellChanges { Name, Description, MaxLevel, Slots = {[n] = Effect}, Scale = {[effectId] = k}, Add = {[effectId] = d} })` — всё откатывается при выключении мода. `Effect(1, 0)` очищает слот.
- `data.ScaleSpell(id, effectId, k)` — короткая форма. ID эффектов заклинаний — `EFFECTS.md` (151 урон пули, 153 число пуль, 158 КД пули и т. д.).

Пример: `examples/spell_demo` (урон пули ×N, +2 пули на уровне 2). Собрано, ◻ на устройстве не проверено; проверять по `modkit.log` («слот N: эффект …») и в бою по урону пули.

### Заклинания в бою (`ISpells`, `modkit_spells`)

`ISpells`: `Level(id)`, `CanCast(id)`, `SetAuto(id, bool)`, `Cast(id, steps = 2)` (ID заклинаний — `Ability` 1..21, таблица имён в `melon/mods/modkit_spells/Spells.cs`: флаг героя `Is{Имя}_P`, корутина `Play_{Имя}`).

- **Как работает.** Игра запускает `Play_X` один раз как бесконечную корутину по строке (`StartCoroutine("Play_X")` в `StartInformation_B`). `SetAuto(id, false)` останавливает её (`StopCoroutine`) и поднимает флаг `IsX_P`, чтобы игра не запускала снова; игра при каждом `StartInformation_B` сбрасывает флаги при уровне 0, поэтому ядро заново глушит авто-каст после каждого такого вызова (хук «после»). Первая версия ставила флаг один раз — игра его затирала, и авто-каст шёл вместе с ручным. `Cast` создаёт новую корутину `Play_X()` и делает `steps` вызовов `MoveNext` — получается один залп без ожиданий. При выключении мода авто-каст возвращается.
- ✅ `cast_probe` на Огненном шаре: ручной залп раз в 2 с, авто-каст остановлен, ограничение `fireball_cap` (число шаров 1) на ручной залп действует; Harmony на `MoveNext` итератора (`C_Battle_AllyUnit+_Play_Fireball_d__1134`) работает без вылетов.
- ◻ Остальные заклинания (у части нет `Play_X` — `CanCast` вернёт false), возвращение авто-каста при выключении мода, поведение в новом бою, звук и счётчики комбинаций при ручном касте. Комбинированные версии корутин (`Play_Fireball_Com01/03`) при активных комбинациях запускаются отдельно и `SetAuto` их не гасит.

**Активные заклинания:** `ISpells.AddActive` и `UiKit.AddHudButton`. `AddActive(new ActiveSpellDef { SpellId, Label, Cooldown, Burst, Slot, Color, OnCast })` отключает авто-каст, рисует кнопку на экране боя (`UiKit.AddHudButton`, справа внизу, `Slot` сдвигает влево на 170 px) и запускает `Burst` залпов по нажатию. Перезарядка кнопки идёт по игровому времени (на паузе стоит), кнопка скрывается на паузе, в главном меню (`InBattle`: хук `A_MainMenuInformation.Update`) и когда герой мёртв (`C_Battle_AllyUnit.Unit_Hp <= 0`). Мод `examples/active_demo` 2.0 — три кнопки (✅ Огненный шар; ◻ Молния и Метеорит). Также `ISpells.InBattle`, `Paused`, `Cooldown(id)`. Подробнее — `ACTIVE_SPELLS.md`.

### PanView и Pager (`melon/ModKit/UiPan.cs`) ◻

Два виджета uGUI в стиле окна «Моды». Ввод и обрезка сделаны на тех же компонентах, что `AddScroll` (проверен ✅): `ScrollRect` по двум осям + `RectMask2D`, поэтому перетаскивание и границы даёт сам Unity.

- **PanView:** `UiKit.AddPanView(parent, x, y, w, h, PanOptions)` или окно `UiKit.OpenPanWindow(name, title, subtitle, PanOptions)`.

| Поле `PanOptions` | Назначение |
|---|---|
| `ContentW/ContentH` | размер карты, определяет границы |
| `NodeSize` | размер узла |
| `Nodes` | `PanNode(id, x, y, icon png, label, state)`, x/y — центр в координатах содержимого |
| `Edges` | `PanEdge(from, to)`, линия — тонкий повёрнутый `Image` под узлами |
| `OnTap(node)` | кнопка узла; при перетаскивании клик не срабатывает |

  Состояния `Locked/Available/Done` — цвет рамки; линия золотая, если оба конца не `Locked`. У результата: `SetState(id, state)`, `Refresh()`, `CenterOn(id)`, `Find(id)`, `View`, `Content`, `Scroll`.
- **Pager:** `UiKit.AddPager(parent, x, y, w, h, PagerOptions)`: кнопки `<` `>` (символы ◀ ▶ в шрифте игры могут отсутствовать), подпись «заголовок N / M», под ней контейнер страницы. `PagerOptions`: `Pages` (заголовки), `Start`, `Loop`, `OnPage(index, title, container)` — вызывается при смене, контейнер очищен. Методы `Next/Prev/Go(i)`, свойство `Page`.
- Демо: `melon/examples/pan_demo` (кнопка настроек мода: карта из 8 узлов и Pager). Сборка: `python modkit/melon/pack_mod.py pan_demo`.
- Риски: рабочая гипотеза, что `ScrollRect` с `horizontal = vertical = true` и `localEulerAngles` линий работают под Mono (Vector3 — структура, должно быть безопасно); `CenterOn` считает от размеров `rect` сразу после создания (раскладка могла не обновиться); узлы-кнопки внутри `ScrollRect` могут «съедать» начало перетаскивания.

### Журнал вылетов и защита от ошибок (`Crash.cs`)

Нативный сбой игры (SIGSEGV, `Scudo ERROR`) из C# не поймать. Поэтому ядро пишет в `UserData/ModKit/lastrun.txt` последние «метки» (какой мод в каком хуке) и держит `running.lock`; при штатном выходе замок снимается. Если на старте замок найден, в `modkit.log` пишется отчёт («последним работал мод X: хук Y»), а мод отключается, если сбой пришёлся прямо на его код. Метка модов: `ctx.Mark("что делаю")`.

Ошибки C#: `ctx.Guard(что, действие)`, `ctx.Error(текст)`; все хуки, события, таймеры и эффекты ядра обёрнуты сами. 5 ошибок за 10 с отключают мод, игра продолжает работать.

### Бот-тестер (`autotest`)

Мод `melon/mods/autotest` (v2.0 на новом API) + скрипт `tools/autotest.py`. Бот ходит штатным джойстиком (`V_JoyStickController._moveVector` каждый кадр) к шарикам (`C_Battle_Item`) и к сундуку (`C_Battle_Item_TreasureBox`, по пути подбирая шарики), выбирает заклинания (`Button_AbilityCh` + `Decision`) и артефакты (`Button_Artifact_Select` + `Button_ArtifactObtain_Select`), пишет `autotest.txt` (HB, STAT, CHECK, RESULT, DONE).

Скрипт: `python modkit/tools/autotest.py --mods effects_demo,projectile_cap --duration 240 --picks 1 [--expect "UnitApply_MagicBulletNum<=3"] [--bisect]`. При вылете печатает отчёт ядра; `--bisect` перезапускает бой по разу без каждого мода и называет виновника.

`tools/dumpm`: `dotnet run -- <Класс> "<regex>" p` — методы и свойства классов игры.

## 7. Прогресс в Zombim

26 файлов `*.json` из `Download/ModKit/progress` копируются в `/sdcard/Android/data/com.vkslrzm.Zombim/files` целиком (контрольные суммы дублируются внутри набора), игра перед этим останавливается (`force-stop`, чтобы она не сохранила поверх). Резервная копия — `Download/ModKit/backup/zombim_<время>`.

## 8. Ограничения и грабли

- **Лимит снарядов.** `satellite_more` и `spirit_more` конфликтуют с ним: первый сам режет число до 14 и рисует клонов; при настоящем числе он нарисует 80 клонов с шагом 1° («полоска»). Держать выключенными. `spirit_more` теперь не рисует духов, если заклинание «Дух» не получено.
- Настоящее число в скобках в окне характеристик — не сделано (нужны индексы `Array_BattleStatus_Value`).
- Бонусы за количество («Волшебная палочка», «Термоядерная реакция», «Снежная корона» и т. д.) после обрезки не проверены.
- Ледяные иглы в «Лимите снарядов» не включены (число взрывов серии, вероятно, `UnitApply_IceNovaTime`, не проверено).
- Хуки Harmony: только на вход метода; на обычных «до/после» не использовать `__result` (см. раздел 4).
- Не хукать `Unit_Damage`/`Update` врагов (сотни экземпляров за кадр); трассировка `gamelog` (~300 методов) — только вручную, «Опасно».
- Не править сохраняемые массивы и `ObscuredInt/Float` (контрольные суммы): `API.md` §5.

### Что дальше (открытые вопросы)

- Резать число снарядов до раскладки спутников на C#: нужен либо патч байтов `libil2cpp.so` (по результатам Ghidra), либо возврат к Frida-хукам. Выяснить, где именно в бою перестраивается круг спутников при росте числа (`AbilityStatApply` читать дизассемблером).
- Настоящее число в скобках в окне характеристик; ледяные иглы; бонусы за количество.
- Судьба Frida-версии: рабочая, но с лагом на хуках; `modkit/src` удалять после переноса `spirit_more`, `satellite_more` и других встроенных TS-модов.

## 9. История и тупики

Хронология — рабочие заметки 2026-10-17/18. Устарело: способ «после пересчёта» для Frida-версии, `ctx.*`-методы эффектов и заклинаний (заменены библиотеками, см. `CHANGELOG.md`).

### Frida-мод «Лимит снарядов» (`modkit/src/mods/projectile_cap.ts`)

Встроенный TypeScript-мод, выключен по умолчанию, настройки — через окно «Моды». У каждой магии свой предел числа снарядов; всё, что игра насчитала сверх предела, превращается в бонус урона и один стат магии. Бонус мультипликативный от базы: `урон × (1 + лишние × %)`. Ползунки «Бонус урона» и «Бонус стата» масштабируют таблицу.

| Магия | Поле количества | Предел | Урон за 1 лишний | Доп. стат за 1 лишний |
|---|---|---|---|---|
| Магическая пуля | `MagicBulletNum` | 50 | +2% | пробитие, +1 за 10 лишних |
| Огненный шар | `FireballNum` | 30 | +3% | пробитие, +1 за 10 лишних |
| Молния | `ThunderNum` | 30 | +3% | область +1% |
| Цунами | `TsunamiNum` | 15 | +5% | размер +1,5% |
| Дух | `SpiritNum` | 14 (28 с «Валькирией») | +3% | размер снаряда +1% |
| Спутник | `SatelliteNum` | 14 (28 с кругом B) | +3% | размер +1% |
| Метеорит | `MeteorNum` (статическое) | 30 | +3% | область +1% |
| Вихрь | `TornadoNum` | 15 | +5% | размер +1,5% |
| Электрический удар | `ElectricNum` | 20 | +4% | размер +1% |
| Испепеление | `IncinerateFireNum` | 20 | +4% | размер +1% |
| Энергетическая пуля | `EnergyBulletFireNum` | 30 | +3% | размер +1% |
| Метель | `BlizzardNum` | 20 | +4% | область +1% |
| Таинственный луч | `ArcaneRayNum` | 10 | +8% | размер +2% |
| Лавовая область | `LavaFieldNum` | 15 | +5% | размер +1,5% |
| Световая вспышка | `FlashShockNum` | 20 | +4% | размер +1% |
| Щит | `ShieldMaxNum` | 10 | +5% | область +2% |

Как устроено:

- Число снарядов игра пишет в поля героя внутри `V_Ability_Manager.AbilityStatApply` и `C_Battle_AllyUnit.StartInformation_B`, а раскладку по кругу считает дальше в тех же функциях. Срез «после пересчёта» оставлял круг, поделённый на сотни спутников (все в одной точке).
- Поэтому хуки ставятся сразу после каждой инструкции записи поля (`str s/w, [xN, #смещение]`). Адреса найдены поиском по `libil2cpp.so` и зашиты в таблицу `STORES` (56 мест). Перед установкой проверяется, что по адресу нужная инструкция.
- Настоящее число запоминается (`pending`), бонус применяется в конце пересчёта. Один раз за пересчёт: после обрезки счётчик равен пределу, повторно бонус не накапливается.
- Круг B спутников определяется по `Array_Obj_UnitSatellitesB[0].activeSelf`; «Валькирия» — по `Obj_UnitSpiritMid_Plus.activeSelf`.
- Бонус и показ в окне характеристик только для полученных заклинаний: `Z_Database.List_AllyMagicLevel[ID заклинания] > 0` (индекс = ID, проверено по логу). Число духов складывается из артефактов и без заклинания.
- Окно характеристик берёт урон из `AllyUnitVar_<Магия>DamageRatioU`: на время `Display_BattleStatus` туда временно добавляется процент бонуса.
- Настоящее число других модов: `ctx.expose({ real })`.

Тупик: попытка оставить настоящее число и подменять его только на время `ApplySatelliteGroup` не сработала: функция вызывается только из `StartInformation_B` (два раза), в бою круг перестраивается другим кодом.

### Нативные хуки через CModule — не работают

Горячие хуки переписывались на C (`CModule`) ради снятия лага. Эксперимент по ступеням (настройка `native_test`, 0–5): компиляция, пустой хук, рабочий хук на одном месте, пустые и рабочие на всех 56 местах — установка проходит, игра доходит до меню. **В бою падает**: `SIGSEGV (SEGV_ACCERR)`, `pc` в анонимной памяти, смещение `0x68` (сгенерированный код). Причина не найдена. Код оставлен, включение — `USE_NATIVE`/`native_test` (по умолчанию выключено). Рабочий вариант — JS-хуки.

### Прочие тупики

- Правка строки версии в `libunity.so` f1 вместо родной библиотеки f3: запускалось, но падало (Scudo `invalid chunk state`).
- Трассировка `gamelog` (~300 хуков) на лету: два вылета (SIGSEGV/Scudo), отключена по умолчанию.
- Первая версия `SetAuto` ставила флаг один раз: игра его затирала, авто-каст шёл вместе с ручным.
