"""Генерирует docs/TABLES.md (EFFECTS.md — tools_gen_effects.py) из выгрузки таблиц (modkit/tables/*.json).
Запуск:  python modkit/tools_gen_docs.py
Выгрузка таблиц: см. docs/README.md, раздел «Справочник по таблицам»."""
import json, os, re, collections

ROOT = os.path.dirname(os.path.abspath(__file__))
T = os.path.join(ROOT, "tables")
OUT = os.path.join(ROOT, "docs")
os.makedirs(OUT, exist_ok=True)
load = lambda n: json.load(open(os.path.join(T, n + ".json"), encoding="utf8"))

# ---- TABLES.md
lines = ["# Таблицы игры", "",
         "Автогенерация (`modkit/tools_gen_docs.py`). Колонки названы по-корейски — как в оригинальных CSV игры.", ""]
for n in sorted(f[:-5] for f in os.listdir(T) if f.endswith(".json")):
    rows = load(n)
    if not rows:
        continue  # таблица наполняется только в бою — выгрузите её во время боя
    lines += [f"## {n}  ({len(rows)} строк)", "", "Колонки: " + ", ".join(f"`{c}`" for c in rows[0].keys()), ""]
    if "유형" in rows[0]:
        cnt = collections.Counter(r["유형"] for r in rows)
        lines += ["Типы (`유형`): " + ", ".join(f"`{k}` × {v}" for k, v in cnt.items()), ""]
open(os.path.join(OUT, "TABLES.md"), "w", encoding="utf8").write("\n".join(lines))

print("TABLES.md updated; EFFECTS.md — python modkit/tools_gen_effects.py")
