"""Правит List_SingleValue.  save_set.py <папка_с_файлами> <выходная_папка> idx=значение ...
Золото (idx 5) автоматически правится и в Array_AbilityClassPractice[-1] (вторая копия)."""
import json, os, sys
sys.path.insert(0, os.path.dirname(__file__))
from save_tool import dec, enc

src, out = sys.argv[1], sys.argv[2]
os.makedirs(out, exist_ok=True)
rd = lambda n: json.loads(dec(open(f"{src}/{n}.json", encoding="utf-8-sig").read()))
single = rd("List_SingleValue")
for kv in sys.argv[3:]:
    i, v = kv.split("="); single[int(i)] = v
    if int(i) == 5:
        prac = rd("Array_AbilityClassPractice"); prac[-1] = int(v)
        open(f"{out}/Array_AbilityClassPractice.json", "w").write(enc(json.dumps(prac, separators=(",", ":"))))
open(f"{out}/List_SingleValue.json", "w").write(enc(json.dumps(single, separators=(",", ":"))))
print("ok", single)
