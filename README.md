# MS_KIT — моды для Magic Survival (Android)

Менеджер модов для *Magic Survival*: ставите готовое приложение **Zombix**, открываете окно «Моды», включаете нужное. Без root. Оригинальная игра остаётся на месте: Zombix ставится рядом, сохранения у него отдельные.

Есть два поколения Zombix. Берите **MelonLoader-версию** (новая): моды на C#, библиотеки, больше возможностей.

| | Zombix (MelonLoader) — новая | Zombix (Frida) — старая |
|---|---|---|
| Моды | C#, zip с `mod.json` и DLL | TypeScript / JS, zip |
| Что умеют | враги, урон, заклинания, союзники, свои эффекты, артефакты, классы, интерфейс | правка таблиц и эффектов, окна |
| Релиз | [zombix-melon-20261020](https://github.com/Kysnez4/MS_KIT/releases/tag/zombix-melon-20261020) | [zombix-20261017-1020](https://github.com/Kysnez4/MS_KIT/releases/tag/zombix-20261017-1020) |
| Моды | [modkit-2.0-mods](https://github.com/Kysnez4/MS_KIT/releases/tag/modkit-2.0-mods) | [Моды для Zombix](https://github.com/Kysnez4/MS_KIT/releases/tag/mods) |

## Установка Zombix (MelonLoader)

Нужен телефон с arm64. Компьютер для установки не обязателен.

1. Скачайте `Zombix-melon.apk` и `Zombix-melon-ModKit.zip` из [релиза](https://github.com/Kysnez4/MS_KIT/releases/tag/zombix-melon-20261020).
2. Установите APK (разрешите установку из неизвестных источников; если Play Protect предупредит — «Всё равно установить»).
3. Запустите игру. Она сама откроет настройки «Доступ ко всем файлам»: разрешите его и вернитесь в игру. Дождитесь главного меню (первый запуск занимает несколько минут: идёт подготовка библиотек игры), затем закройте игру.
4. Распакуйте `Zombix-melon-ModKit.zip` в папку `/sdcard/MelonLoader/com.vkslrzm.Zombix/` (папки `Mods` и `UserData` должны слиться с существующими). Запустите игру снова.
5. В главном меню откройте окно «Моды»: карточка мода включает и выключает его, у мода могут быть настройки.

Покупки в клоне не работают. Если что-то не так, смотрите журнал: `/sdcard/MelonLoader/com.vkslrzm.Zombix/UserData/ModKit/modkit.log`.

### Как поставить ещё моды
Скачайте zip мода из [релиза с модами](https://github.com/Kysnez4/MS_KIT/releases/tag/modkit-2.0-mods) в папку `Download` (или `Download/ModKit`), в окне «Моды» нажмите «+» и выберите версию. Библиотеки `modkit_*` включаются сами, когда включён мод, которому они нужны. Если мод пишет, что нужна библиотека, скачайте её тем же способом.

### Какие моды есть
`achievements` (достижения), `active_pack` (активные заклинания), `area_mechanics` (механики зон), `art_swap` (свои иконки артефактов), `artifact_pack` (12 новых артефактов и 2 синергии), `auto_artifact` (артефакт на старте боя), `class_menu` (свои классы), `class_practice` (практика классов за бой), `kill_streak` (серия убийств), `projectile_cap` (лимит снарядов), `spell_tuner` (настройка заклинаний), `gamelog` (журнал игры).

## Установка Zombix (Frida, старая)
Скачайте `Zombix.apk` из [релиза](https://github.com/Kysnez4/MS_KIT/releases/tag/zombix-20261017-1020), установите, запустите; окно «Моды» открывает значок гаечного ключа. Для zip-модов из `Download/ModKit` включите для Zombix «Доступ ко всем файлам».

## Для авторов модов

| Документ | О чём |
|---|---|
| [Журнал изменений ModKit](modkit/melon/CHANGELOG.md) | что нового в версии 2.0 |
| [API ModKit (C#)](modkit/docs/API.md) | как устроен мод, события, ядро и 16 библиотек, `ctx.Use<T>` |
| [Установка MelonLoader](modkit/docs/MELONLOADER.md) | сборка APK, заливка, журналы, грабли |
| [Руководство по Frida-версии](modkit/docs/README.md) | TypeScript-моды, формат zip |
| [Каталог артефактов](modkit/docs/CATALOG.md), [таблицы](modkit/docs/TABLES.md), [эффекты](modkit/docs/EFFECTS.md), [условные эффекты](modkit/docs/CONDITIONAL.md) | данные игры |

Минимальный мод на C#:

```csharp
public class MyMod : IKitMod {
    public ModDef Define() => new ModDef {
        Id = "my_mod", Name = "Мой мод", Version = "1.0",
        OnEnable = ctx => {
            var enemies = ctx.Use<IEnemies>("modkit_combat");   // библиотека из mod.json: "depends"
            ctx.Every(1000, () => ctx.Log($"врагов: {enemies.Alive.Count}"));
        },
    };
}
```

Сборка и упаковка: `python modkit/melon/pack_mod.py my_mod` (сначала библиотеки, от которых мод зависит). Подробности в [API.md](modkit/docs/API.md).

## Для разработчиков

```powershell
dotnet build -c Release modkit\melon\ModKit          # ядро
python modkit\melon\pack_mod.py <мод> ...            # zip мода или библиотеки
bash modkit/tools/modkit_deploy.sh                   # ядро на телефон и запуск
PKG=com.vkslrzm.Zombix bash modkit/tools/melon_build.sh   # собрать Zombix-APK (BUILD_ONLY=1 — без установки)
python modkit/tools/autotest.py --mods api_probe --duration 120   # автотест боя на телефоне
```

Frida-версия: `npm install`, `npm run build`, `python modkit\build_standalone.py`.

Для ИИ-агентов есть отдельная памятка: [AGENTS.md](AGENTS.md).

## Благодарности и ИИ

Загрузчик: [MelonLoader](https://github.com/LavaGang/MelonLoader) / LemonLoader, Il2CppInterop, Harmony; старая версия: frida-gadget. Большая часть кода написана с помощью ИИ-агентов (Claude Code), результат проверяется сборкой и запуском на телефоне. Проект неофициальный, не связан с разработчиками игры; играть с модами в сетевых режимах не стоит.
