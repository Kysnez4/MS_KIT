"""Собирает C#-мод из modkit/melon/mods/<имя> (или examples/<имя>) в zip: mod.json + <имя>.dll.
Запуск: python modkit/melon/pack_mod.py <имя> [...]   Результат: <имя>.zip рядом с папкой мода.
mod.json берётся из папки мода (id, name, description, version, depends, options...); поле main ставится само."""
import json, os, subprocess, sys, zipfile

base = os.path.dirname(os.path.abspath(__file__))
for name in sys.argv[1:]:
    root = next(os.path.join(base, r) for r in ('mods', 'examples') if os.path.isdir(os.path.join(base, r, name)))
    d = os.path.join(root, name)
    subprocess.run(['dotnet', 'build', '-c', 'Release', os.path.join(d, name + '.csproj')], check=True, stdout=subprocess.DEVNULL)
    dll = os.path.join(d, 'bin', 'Release', 'net8.0', name + '.dll')
    meta_path = os.path.join(d, 'mod.json')
    meta = json.load(open(meta_path, encoding='utf-8')) if os.path.exists(meta_path) else {}
    meta.setdefault('id', name)
    meta.setdefault('name', name)
    meta['main'] = name + '.dll'
    out = os.path.join(root, name + '.zip')
    with zipfile.ZipFile(out, 'w', zipfile.ZIP_DEFLATED) as z:
        z.writestr('mod.json', json.dumps(meta, ensure_ascii=False, indent=2))
        z.write(dll, name + '.dll')
        for f in [meta.get('icon')] + list(meta.get('screenshots', [])):
            if f and os.path.exists(os.path.join(d, f)) and f not in z.namelist():
                z.write(os.path.join(d, f), f)
        for sub in meta.get('assets', []):            # папки с файлами мода (например icons) кладутся в zip как есть
            for dp, _, fs in os.walk(os.path.join(d, sub)):
                for f in fs:
                    full = os.path.join(dp, f)
                    z.write(full, os.path.relpath(full, d).replace(os.sep, '/'))
    print('->', out)
