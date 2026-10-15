"""Генерирует docs/CATALOG.md: все артефакты, комбинации и синергии с ID, описанием и эффектами.
Запуск:  python modkit/tools_gen_catalog.py   (редкость берётся из modkit/gamelog.txt, если он есть)."""
import json, os, re, collections

ROOT = os.path.dirname(os.path.abspath(__file__))
load = lambda n: json.load(open(os.path.join(ROOT, "tables", n + ".json"), encoding="utf8"))
clean = lambda s: re.sub(r"</?color[^>]*>", "", str(s)).strip()
used = lambda r, c: [(int(r[f"효과ID_{i:02d}"]), r[f"효과ID_{i:02d}값"]) for i in range(1, 10)
                     if r.get(f"효과ID_{i:02d}") not in (None, 0, 1)]
lines_of = lambda r, n: " ".join(clean(r[f"효과 설명줄{i:02d}"]) for i in range(1, n + 1)
                                 if r.get(f"효과 설명줄{i:02d}") not in (None, "`", ""))

ab = {r["ID"]: r for r in load("Ability")}
# известные смыслы эффектов — из EFFECTS.md (строка «| ID | значение → артефакт N: текст |»)
eff_hint = {}
p = os.path.join(ROOT, "docs", "EFFECTS.md")
if os.path.exists(p):
    for m in re.finditer(r"^\| (-?\d+) \| .*?: (.*) \|$", open(p, encoding="utf8").read(), re.M):
        eff_hint[int(m.group(1))] = m.group(2)
fx = lambda pairs: "; ".join(f"`{e}={v}`" for e, v in pairs) or "—"

# редкость по пулам выпадения из журнала игры
grade = {}
log = os.path.join(ROOT, "gamelog.txt")
if os.path.exists(log):
    for m in re.finditer(r"SET Z_Database\.List_ArtifactPossibleGrade_([CBAPS]): .*? -> \[([\d,]+)\]", open(log, encoding="utf8").read()):
        for i in set(map(int, m.group(2).split(","))):
            grade[i] = m.group(1)
GR = {"C": "обычное", "B": "редкое", "A": "героическое", "P": "особое", "S": "легендарное"}

out = ["# Каталог: артефакты, комбинации, синергии", "",
       "Автогенерация: `python modkit/tools_gen_catalog.py` (из `modkit/tables/*.json` и `modkit/gamelog.txt`).",
       "Эффект записан как `ID=значение`; что делает ID — в [EFFECTS.md](EFFECTS.md). Редкость берётся из пулов выпадения; `?` — не определена.", ""]

arts = [r for r in ab.values() if r["유형"] == "아티팩트"]
out += [f"## Артефакты ({len(arts)})", "", "| ID | Название | Редкость | Описание | Эффекты |", "|---|---|---|---|---|"]
for r in sorted(arts, key=lambda r: r["ID"]):
    g = grade.get(r["ID"]) or ("A" if grade and r["ID"] not in grade else "?")
    out.append(f"| {r['ID']} | {clean(r['이름'])} | {GR.get(g, '?')} | {lines_of(r, 6)} | {fx(used(r, 9))} |")

name = lambda i: clean(ab[i]["이름"]) if i in ab else f"#{i}"
combos = [r for r in load("MagicCom") if r["유형"] == "조합"]
out += ["", f"## Комбинации ({len(combos)})", "",
        "Условия `A`, `B`, `C` — ID требуемых навыков/способностей (таблица `Ability`), `특성A레벨`…`C레벨`.", "",
        "| ID | Название | Требует (A/B/C) | Описание | Эффекты | Ультимейт |", "|---|---|---|---|---|---|"]
for r in combos:
    req = " + ".join(f"{name(r[k])} ({r[k]})" for k in ("특성A레벨", "특성B레벨", "특성C레벨") if r[k])
    ult = clean(r.get("궁극기 이름") or "")
    out.append(f"| {r['ID']} | {clean(r['이름'])} | {req or '—'} | {lines_of(r, 6)} | {fx(used(r, 9))} | {ult if ult not in ('', '`') else '—'} |")

syn = [r for r in load("Synergy") if r["개수"]]
out += ["", f"## Синергии ({len(syn)})", "",
        "Синергия включается, когда у героя есть нужные артефакты (`ID_01…ID_05`).", "",
        "| ID | Название | Артефакты | Описание | Эффекты |", "|---|---|---|---|---|"]
for r in syn:
    members = ", ".join(f"{name(r[f'ID_{i:02d}'])} ({r[f'ID_{i:02d}']})" for i in range(1, 6) if r[f"ID_{i:02d}"])
    out.append(f"| {r['ID']} | {clean(r['이름'])} | {members} | {lines_of(r, 3)} | {fx(used(r, 3))} |")

open(os.path.join(ROOT, "docs", "CATALOG.md"), "w", encoding="utf8").write("\n".join(out) + "\n")
print("артефактов", len(arts), "комбинаций", len(combos), "синергий", len(syn), "с редкостью", len(grade))
