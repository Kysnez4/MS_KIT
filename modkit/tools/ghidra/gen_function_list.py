"""Список функций игры для DecompileAll.java: раздел Assembly-CSharp из dump.cs -> RVA<TAB>класс<TAB>метод.
Запуск: python gen_function_list.py <dump.cs> <functions.tsv>"""
import re, sys

src, dst = sys.argv[1], sys.argv[2]
lines = open(src, encoding='utf-8').read().split('\n')
start = next(i for i, l in enumerate(lines) if l.startswith('// ==== Assembly-CSharp'))
end = next((i for i in range(start + 1, len(lines)) if lines[i].startswith('// ====')), len(lines))
cls, out, seen = None, [], set()
for l in lines[start:end]:
    m = re.match(r'^(?:public |internal |private )?(?:sealed |abstract |static )*(?:class|struct) ([^\s:]+)', l)
    if m:
        cls = m.group(1); continue
    r = re.search(r'\s([^\s(]+)\((.*)\); // RVA (0x[0-9a-f]+)', l)
    if r and cls and int(r.group(3), 16) > 0 and r.group(3) not in seen:
        seen.add(r.group(3))
        out.append(f'{r.group(3)}\t{cls}\t{r.group(1)}')
open(dst, 'w', encoding='utf-8').write('\n'.join(out) + '\n')
print(len(out), 'функций')
