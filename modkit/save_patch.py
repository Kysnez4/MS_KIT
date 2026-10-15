"""Меняет золото в сохранении (две копии: List_SingleValue[5] и Array_AbilityClassPractice[-1]).
Использование: save_patch.py <папка_с_файлами> <золото> <выходная_папка>"""
import json, os, shutil, sys
sys.path.insert(0, os.path.dirname(__file__))
from save_tool import dec, enc

src, gold, out = sys.argv[1], int(sys.argv[2]), sys.argv[3]
os.makedirs(out, exist_ok=True)
single = json.loads(dec(open(f"{src}/List_SingleValue.json", encoding="utf-8-sig").read()))
prac = json.loads(dec(open(f"{src}/Array_AbilityClassPractice.json", encoding="utf-8-sig").read()))
assert str(prac[-1]) == single[5], (prac[-1], single[5])
single[5] = str(gold); prac[-1] = gold
open(f"{out}/List_SingleValue.json", "w").write(enc(json.dumps(single, separators=(",", ":"))))
open(f"{out}/Array_AbilityClassPractice.json", "w").write(enc(json.dumps(prac, separators=(",", ":"))))
print("ok", single, prac[-2:])
