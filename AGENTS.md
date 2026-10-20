# AGENTS.md — памятка для ИИ-агентов (Claude Code, Codex и др.)

Репозиторий: загрузчик модов для Android-игры Magic Survival. Язык общения с владельцем — русский. Читай этот файл до правок; человеческое описание — в `README.md`.

## Карта репозитория
- `modkit/melon/ModKit/` — ядро C# (MelonLoader). **Только** мод-менеджер: `Api.cs` (контракт), `Core.cs`, `Hooks.cs`, `Events.cs`, `External.cs` (zip-загрузка), `Registries.cs`, `BattleState.cs`, `MenuUi.cs`, `Ui*.cs`, `Extras.cs` (Timer/Store/Tags), `Screen.cs`. Игровой логики в ядре быть не должно.
- `modkit/melon/mods/modkit_*` — 16 библиотек (`library: true`): data, artifacts, combos, synergies, spells_data, effects, spells, combat, companions, zones, hero, triggers, resources, research, game, art. Граф зависимостей: `modkit/docs/API.md`, раздел 1а.
- `modkit/melon/mods/*`, `modkit/melon/examples/*` — моды и примеры. `pack_mod.py` собирает zip.
- `modkit/src`, `modkit/build*.py`, `modkit/out` — старая Frida-версия (TypeScript).
- `modkit/docs/` — документация для авторов модов (оглавление `modkit/docs/README.md`: GETTING_STARTED, MOD_FORMAT, API, LIBRARIES, COOKBOOK, DEBUGGING). При изменении API обновляй эти файлы; примеры кода из COOKBOOK живут в `modkit/melon/examples/cookbook` и должны собираться. `modkit/tools/` — деплой, автотест, сборка APK, Ghidra.
- `modkit/decomp`, `modkit/tools/ghidra/*.tsv` — **локальные** результаты декомпиляции игры, в git не добавлять (публичный репозиторий, чужой код).

## Главные правила API
- Из `ModContext` нет `Game/Combat/Spells/...`. Библиотеки берутся через `ctx.Use<IX>("modkit_x")` (кэш на мод; хуки и правки откатываются при выключении мода). `ctx.Mod<T>` — только чужой Expose.
- Библиотека = класс `IKitMod` + `ILibrary<T>`; в `OnEnable` вызывает `ctx.Expose(this)`; её внутренние классы получают `ctx.CleanupList()`.
- Мод, использующий библиотеку: `depends` в `mod.json` (значение «>=1.0», для `modkit_data` «>=1.5»), `Reference` на DLL в `csproj` (HintPath `../modkit_x/bin/Release/net8.0/modkit_x.dll`, `Private=false`), **поднять версию** при правке.
- Библиотеки видят `internal` ядра через `InternalsVisibleTo` (`ModKit/Properties.cs`); новая библиотека — добавить строку туда. Между собой библиотеки видят только `public`.
- Порядок загрузки zip задаёт `External.LoadOrder` (по `depends`, библиотеки первыми).

## Сборка и проверка
```bash
dotnet build -c Release modkit/melon/ModKit
python modkit/melon/pack_mod.py <имена>          # библиотеки раньше потребителей
bash modkit/tools/modkit_deploy.sh               # ядро на телефон (adb, пакет com.vkslrzm.Zombim)
python modkit/tools/autotest.py --mods <ids> --duration 120   # бой на телефоне, исходы PASS/FAIL/CRASH
```
- Проверяй игру **по журналам и `ctx.Screen`**, не по скриншотам и слепым тапам. Журнал: `/sdcard/MelonLoader/<пакет>/UserData/ModKit/modkit.log`.
- В Git Bash для adb: `MSYS_NO_PATHCONV=1`, пути к файлам на ПК писать как `D:/...`.
- Сборка APK: `PKG=com.vkslrzm.Zombix bash modkit/tools/melon_build.sh` (`BUILD_ONLY=1` — без установки). Первый запуск APK долго генерирует сборки IL2CPP.
- Хостовая проверка загрузки zip без телефона: маленький консольный харнесс (загрузить DLL из zip, `GetTypes`, `Define()`), но хуки и IL2CPP на ПК не работают.

## Ограничения и грабли
- Не править сохраняемые массивы и `ObscuredInt/Float` (контрольные суммы): `Array_AbilityClassPractice`, `Array_AreaBestScoreNum`, `Array_JournalProgress`, золото, самоцветы. См. `modkit/docs/API.md` §5, `MELONLOADER.md`.
- Не хукать `Unit_Damage`/`Update` врагов (сотни экземпляров за кадр). Хуки внутри функций недоступны.
- `UnityEngine.Texture/Texture2D` и `Sprite` создавать через `Raw`/указатели (обёртки роняют Mono).
- Класс игры задаётся один раз при `Start_Ability`; свои классы — через `class_menu` + `CustomClass`, не через таблицу `Class`.
- Тексты на кириллице: правь файлы `Edit`/`sed` или Python с `encoding='utf-8'`; PowerShell `Set-Content`/`Out-File` портит кодировку.
- Не добавляй в репозиторий: файлы игры (APK, `libil2cpp`, декомпилят), ключи и токены, `bin/obj`, zip и аудио с чужими правами. Релизные артефакты — только в GitHub Releases.
- В публичные релизы и репозиторий ничего не выкладывай без явной просьбы владельца; коммиты — только по просьбе.

## Как вести работу
1. Прочитай `modkit/docs/API.md` и затронутый код, ищи существующий хелпер до написания нового.
2. Правь по месту, стиль и плотность комментариев как у соседнего кода (комментарии по-русски).
3. Собери затронутые проекты, упакуй, прогони `autotest.py`; результат (PASS/FAIL/не проверено) пиши честно.
4. Запись в `modkit/melon/CHANGELOG.md` при заметном изменении; память/заметки о деталях игры — в `modkit/docs/*.md`.
