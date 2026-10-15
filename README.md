# MS_KIT — ModKit для Magic Survival

Менеджер модов для Magic Survival: моды на TypeScript встраиваются в игру через `frida-gadget`, в игре появляется окно «Моды».
Готовый автономный клон **Zombix** (моды «Артефакт на старте боя» и «Проклятия») — в [Releases](https://github.com/Kysnez4/MS_KIT/releases).

## Установка Zombix

Достаточно скачать APK из [Releases](https://github.com/Kysnez4/MS_KIT/releases) и установить его на телефон. ADB, ПК и оригинальная игра не нужны.

1. Скачайте `Zombix.apk` со страницы [Releases](https://github.com/Kysnez4/MS_KIT/releases).
2. Откройте файл на телефоне и разрешите установку из неизвестных источников, если система попросит.
3. Если Play Protect предупредит, выберите «Всё равно установить».
4. Запустите игру. Окно «Моды» открывает значок гаечного ключа на главном экране.

Zombix ставится рядом с оригинальной игрой и хранит отдельные сохранения. Нужен телефон с arm64, покупки в клоне не работают. Чтобы подключать свои zip-моды из `Download/ModKit`, включите для Zombix «Доступ ко всем файлам» в настройках приложения.

## Документация

| Документ | О чём |
|---|---|
| [Общее руководство](modkit/docs/README.md) | Как устроен ModKit, установка, `ctx`, моды в формате `.zip` (`mod.json`, иконка, скриншоты) |
| [Каталог артефактов и комбинаций](modkit/docs/CATALOG.md) | Список артефактов, комбинаций и синергий игры |
| [Таблицы игры](modkit/docs/TABLES.md) | Колонки таблиц `Z_Database.Dictionary_*` |
| [Эффекты](modkit/docs/EFFECTS.md) | ID эффектов для артефактов и комбинаций |
| [Условные эффекты](modkit/docs/CONDITIONAL.md) | Эффекты с условиями |

## Быстрый старт

```powershell
npm install
npm run build                                   # собрать modkit/out/loader.js
npm run pack-mod -- modkit\examples\wrath_x8    # упаковать пример мода в .zip
python modkit\build_standalone.py               # собрать автономный APK Zombix
```

## Примеры

- [Мод в формате zip](modkit/examples/wrath_x8/mod.json) — папка с `mod.json`, `main.js`, `icon.png`
- [Импорт прогресса из оригинала в Zombix](modkit/examples/import_progress/main.js) — zip-мод с собственным окном (`ModKit.ui`)
- [Примеры скриптов](modkit/examples/)
