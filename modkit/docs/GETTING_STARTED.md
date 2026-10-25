# Первый мод за 15 минут

## 1. Что понадобится
- **.NET SDK 8 или новее** (`dotnet --version`). Мод собирается под `net8.0`.
- **Телефон с Zombix (MelonLoader)** (arm64) и кабель: для просмотра журнала и копирования файлов удобен `adb` (Android SDK Platform-Tools). Без `adb` тоже можно: zip кладётся в `Download/ModKit` и ставится кнопкой «+».
- **Ядро `ModKit.dll`**: возьмите из `ModKit-core.zip` (файл `Mods/ModKit.dll`) в [релизе ядра](https://github.com/Kysnez4/MS_KIT/releases/tag/modkit-core) или соберите: `dotnet build -c Release modkit/melon/ModKit`.
- **Библиотеки `modkit_*.dll`**, если мод их использует: из zip [релиза библиотек](https://github.com/Kysnez4/MS_KIT/releases/tag/modkit-libs) (внутри каждого zip лежит одноимённая `.dll`) или соберите из `modkit/melon/mods/modkit_*`.
- **Ссылочные сборки игры.** Это сгенерированные MelonLoader обёртки кода игры; на ПК их нет, их забирают с телефона после первого запуска Zombix:

```bash
adb pull /sdcard/MelonLoader/com.vkslrzm.Zombix/MelonLoader/net8             C:/ref/net8
adb pull /sdcard/MelonLoader/com.vkslrzm.Zombix/MelonLoader/Il2CppAssemblies C:/ref/il2cpp
```

Эти файлы — код игры и MelonLoader: не публикуйте их и не добавляйте в репозиторий.

## 2. Структура проекта

```
my_mod/
  my_mod.csproj
  Mod.props          # копия modkit/melon/Mod.props
  MyMod.cs
  mod.json
```

`my_mod.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <Import Project="Mod.props" />
  <PropertyGroup><AssemblyName>my_mod</AssemblyName></PropertyGroup>
  <!-- библиотеки, которые использует мод: -->
  <ItemGroup>
    <Reference Include="modkit_combat"><HintPath>libs/modkit_combat.dll</HintPath><Private>false</Private></Reference>
  </ItemGroup>
</Project>
```

`Mod.props` подключает ядро, MelonLoader, Harmony, Il2CppInterop и сборки игры. Пути задаются свойствами сборки:

| Свойство | Значение по умолчанию | Что |
|---|---|---|
| `Ref` | `D:\tools\lemon\ref` | папка с `net8/` и `il2cpp/` из шага 1 |
| `ModKitDll` | `<папка Mod.props>/ModKit/bin/Release/net8.0/ModKit.dll` | путь к ядру |

Передавайте их в командной строке: `dotnet build -c Release -p:Ref=C:\ref -p:ModKitDll=C:\kit\ModKit.dll`.

## 3. Код

`MyMod.cs` (в zip должен быть **ровно один** класс `IKitMod` с публичным конструктором без параметров):

```csharp
using ModKit;

namespace MyMod
{
    public class MyModEntry : IKitMod
    {
        public ModDef Define() => new ModDef
        {
            Id = "my_mod", Name = "Мой мод", Version = "1.0",
            Options = { new ModOption { Key = "every", Label = "Раз в, с", Default = 10, Min = 1, Max = 60, Step = 1 } },
            OnEnable = ctx =>
            {
                ctx.Events.On("battleStart", e => ctx.Log("бой начался"));
                ctx.Every(ctx.Setting("every") * 1000, () => ctx.Log("тик"));
                ctx.After("C_Battle_Manager", "Start", 0, (self, args) => ctx.Toast("Удачи!", 2));
            },
        };
    }
}
```

Всё, что мод зарегистрировал через `ctx` (хуки, события, таймеры, правки), снимается само при выключении мода.

`mod.json`:
```json
{
  "id": "my_mod",
  "name": "Мой мод",
  "description": "Что делает мод",
  "version": "1.0",
  "author": "Вы",
  "main": "my_mod.dll"
}
```
Полный список полей — в [MOD_FORMAT.md](MOD_FORMAT.md).

## 4. Сборка и упаковка

```bash
dotnet build -c Release
```
Zip — это `mod.json` и DLL в корне архива (необязательно: `icon.png`, `screenshots`):

```bash
cd bin/Release/net8.0 && zip my_mod.zip my_mod.dll && zip my_mod.zip ../../../mod.json
```
Если работаете внутри репозитория, проще скрипт: положите папку мода в `modkit/melon/mods/<имя>` и запустите `python modkit/melon/pack_mod.py <имя>` — он соберёт проект и создаст `<имя>.zip` рядом с папкой (поле `main` проставит сам; сначала упакуйте библиотеки, от которых мод зависит).

## 5. Установка на телефон

Способ А, без компьютера: скопируйте zip в `Download/ModKit` на телефоне, откройте в игре окно «Моды», нажмите «+», выберите мод и версию. Мод появится в списке; включите карточку.

Способ Б, по `adb`:
```bash
adb push my_mod.zip /sdcard/MelonLoader/com.vkslrzm.Zombix/UserData/ModKit/mods/
```
и перезапустите игру. В Git Bash перед `adb` добавьте `MSYS_NO_PATHCONV=1`.

Если мод зависит от библиотек, установите их заранее (или одновременно). Подробнее о зависимостях — [MOD_FORMAT.md](MOD_FORMAT.md).

## 6. Журнал

```bash
adb shell cat /sdcard/MelonLoader/com.vkslrzm.Zombix/UserData/ModKit/modkit.log
```
Ищите строки `мод включён: my_mod`, ваши `ctx.Log` (префикс `[my_mod]`) и `ОШИБКА`. Все приёмы отладки — в [DEBUGGING.md](DEBUGGING.md).

## 7. Обновление мода
Поднимайте `Version` в `mod.json` при каждой правке: загрузчик обновляет установленный мод по версии (кнопка «+» покажет «обновить» или «откатить»). Если новая версия не запустилась, остаётся прежняя.

## 8. Что дальше
Хотите бить врагов, добавить артефакт или заклинание-кнопку? Откройте [COOKBOOK.md](COOKBOOK.md): там готовые примеры, а справочник по возможностям — [API.md](API.md) и [LIBRARIES.md](LIBRARIES.md). Классы игры и их методы ищите через `modkit/docs/DECOMP.md` (Ghidra) или дамп `dump.cs`.
