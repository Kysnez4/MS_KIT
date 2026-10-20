# Декомпиляция игры (Ghidra)

Ghidra с `libil2cpp.so` игры настроен так, что код читается почти как C#: функции названы `Класс__Метод`, у них есть прототипы, а поля классов видны как `this->Поле`.

## Запуск
```powershell
powershell -File modkit\tools\start_ghidra.ps1      # сервер стартует за ~10 секунд
```
Затем из `modkit/tools/ghidra` (нужен python из venv Ghidra, в нём стоит `mcp`):
```bash
P=/d/tools/ghidra/venv/Scripts/python.exe
$P decomp.py 'C_Battle_AllyUnit$$Start' --save   # метод -> modkit/decomp/<имя>.c
$P decomp.py AllyUnit Damage                     # поиск по подстрокам имени
$P decomp.py -c C_Battle_AllyUnit                # методы класса: RVA и сигнатуры C#
$P decomp.py 0x1043f9c                           # по RVA из dump.cs (база 0x100000 добавляется сама)
$P mcpcall.py                                    # список инструментов GhidraMCP
```
Строки из метаданных: `grep -n "текст" modkit/tools/ghidra/strings.txt` (`dump_strings.py`, метаданные из `D:\tools\ghidra\meta`).

## Как это построено (пересборка после обновления игры)
1. `dump.cs` -> `gen_names.py` (names.tsv), `gen_types.py` (structs.tsv, funcs.tsv).
2. Остановить сервер, затем `analyzeHeadless ... -postScript ApplyIl2cppNames.java` и `bash apply_types.sh`.
3. Полный анализ делается один раз (`analyzeHeadless -process libil2cpp.so`), сервер идёт с `--no-analyze`.

## Ограничения
- Имя функции в Ghidra `Класс$$Метод`, в декомпиляторе `Класс__Метод`.
- Структурами описаны только ссылочные классы с уникальным именем; значимые типы (кроме Vector2/3/4, Quaternion, Color) и классы-дубликаты идут как `void *`.
- Графа вызовов (`--calls`) нет: список вызываемых функций пуст.
- Присваивания float парами по 8 байт декомпилятор иногда показывает через временные `_UNK_...`; значения полей видны по `this->Поле`.
