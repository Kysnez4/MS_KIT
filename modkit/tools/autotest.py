"""Автотест модов ModKit на телефоне: запускает бой с заданным набором модов, ждёт бота `autotest` или вылета.

Примеры (из корня репозитория):
  python modkit/tools/autotest.py --mods effects_demo,projectile_cap --duration 120
  python modkit/tools/autotest.py --mods effects_demo,projectile_cap --expect "UnitApply_MagicBulletNum<=3" --duration 90
  python modkit/tools/autotest.py --mods effects_demo,projectile_cap,gamelog --bisect     # при вылете найти виновника перебором
  python modkit/tools/autotest.py --mods gamelog --set gamelog.g_art=0 --picks 1

Исходы: PASS (бот дошёл до конца, проверки прошли), FAIL (проверка бота или --expect не прошла), CRASH (игра упала),
TIMEOUT (бот не закончил). Для CRASH скрипт перезапускает игру и печатает отчёт ядра о прошлом вылете из modkit.log.
Вход в бой делается тапами по главному меню (170,1270 → 360,1500 → 360,1490): `adb input swipe` на этом телефоне запрещён,
движение героя делает сам бот изнутри игры.
"""
import argparse, json, os, re, subprocess, sys, tempfile, time

sys.stdout.reconfigure(encoding='utf-8', errors='replace')

ADB = os.path.join(os.environ.get('LOCALAPPDATA', ''), 'Android', 'Sdk', 'platform-tools', 'adb.exe')
PKG = 'com.vkslrzm.Zombim'
BASE = f'/sdcard/MelonLoader/{PKG}'
K = f'{BASE}/UserData/ModKit'
HERE = os.path.dirname(os.path.abspath(__file__))
MELON = os.path.join(HERE, '..', 'melon')
ENV = dict(os.environ, MSYS_NO_PATHCONV='1')


def adb(*a, check=False):
    r = subprocess.run([ADB, *a], capture_output=True, text=True, encoding='utf-8', errors='replace', env=ENV)
    if check and r.returncode:
        raise RuntimeError(r.stderr)
    return r.stdout


def sh(cmd):
    return adb('shell', cmd)


def alive():
    return bool(sh(f'pidof {PKG}').strip())


def read(path):
    return sh(f'cat {path} 2>/dev/null')


def log(msg):
    print(f'[{time.strftime("%H:%M:%S")}] {msg}', flush=True)


def push_mods(names):
    for n in names:
        for sub in ('mods', 'examples'):
            z = os.path.join(MELON, sub, n + '.zip')
            if os.path.exists(z):
                adb('push', z, f'{K}/mods/', check=True)
                break
        else:
            log(f'внимание: нет {n}.zip (python modkit/melon/pack_mod.py {n})')


def write_config(enabled, settings):
    cfg = json.loads(read(f'{K}/config.json').lstrip('﻿') or '{}')
    cfg.setdefault('enabled', {}); cfg.setdefault('settings', {})
    for k in list(cfg['enabled']):
        cfg['enabled'][k] = False
    for k in enabled:
        cfg['enabled'][k] = True
    for k, v in settings.items():
        cfg['settings'].setdefault(k, {}).update(v)
    cfg['session'] = dict(cfg['enabled'])
    cfg['booting'] = False
    fd, p = tempfile.mkstemp(suffix='.json'); os.close(fd)
    with open(p, 'w', encoding='utf-8') as f:
        json.dump(cfg, f, ensure_ascii=False, indent=2)
    adb('push', p, f'{K}/config.json', check=True)
    os.remove(p)


def start_game():
    sh(f'am start -n {PKG}/com.unity3d.player.UnityPlayerActivity')


def wait_menu(timeout=150):
    t0 = time.time()
    while time.time() - t0 < timeout:
        if 'меню «Моды» создано' in read(f'{K}/modkit.log'):
            return True
        if time.time() - t0 > 20 and not alive():
            return False
        time.sleep(3)
    return False


def enter_battle():
    for xy in ('170 1270', '360 1500', '360 1490'):
        sh(f'input tap {xy}')
        time.sleep(4)


def crash_report():
    """Перезапускает игру и достаёт отчёт ядра о прошлом аварийном запуске."""
    last = read(f'{K}/lastrun.txt').strip().splitlines()[:1]
    sh(f'rm -f {K}/modkit.log')
    start_game()
    time.sleep(40)
    lines = [l for l in read(f'{K}/modkit.log').splitlines() if 'ОШИБКА' in l or l.startswith(('[', ' ')) and ('последним' in l or 'недавние' in l or 'отключ' in l)]
    sh(f'am force-stop {PKG}')
    return (['lastrun: ' + last[0]] if last else []) + lines


def parse_stats(text):
    st = {}
    for m in re.finditer(r'STAT (\w+)=(-?[\d.,]+)', text):
        st[m.group(1)] = float(m.group(2).replace(',', '.'))
    return st


def check_expect(expects, stats):
    res = []
    for e in expects:
        m = re.match(r'(\w+)\s*(<=|>=|==|<|>)\s*(-?[\d.]+)$', e.strip())
        if not m:
            res.append((e, False, 'не разобрал условие')); continue
        name, op, val = m.group(1), m.group(2), float(m.group(3))
        if name not in stats:
            res.append((e, False, 'нет значения в снимках STAT')); continue
        v = stats[name]
        ok = {'<=': v <= val, '>=': v >= val, '==': abs(v - val) < 1e-6, '<': v < val, '>': v > val}[op]
        res.append((e, ok, f'{name}={v:g}'))
    return res


def run_once(mods, settings, duration, picks, expects, move=1, report=True):
    names = list(mods) + ['autotest']
    st = {k: dict(v) for k, v in settings.items()}
    st.setdefault('autotest', {}).update({'duration': duration, 'picks': picks, 'move': move})
    sh(f'am force-stop {PKG}')
    write_config(names, st)
    sh(f'rm -f {K}/running.lock {K}/autotest.txt {K}/modkit.log {K}/lastrun.txt')
    start_game()
    log(f'запуск: {",".join(mods) or "без модов"}')
    if not wait_menu():
        return {'outcome': 'CRASH', 'why': 'игра не дошла до главного меню', 'stats': {}, 'report': crash_report() if report else []}
    enter_battle()
    t0 = time.time(); limit = duration * 3 + 300; res = None; txt = ''
    while time.time() - t0 < limit:
        time.sleep(5)
        txt = read(f'{K}/autotest.txt')
        m = re.search(r'RESULT (PASS|FAIL) (.*)', txt)
        if m and 'DONE' in txt:
            res = (m.group(1), m.group(2).strip()); break
        if not alive():
            res = ('CRASH', 'игра упала'); break
    stats = parse_stats(txt)
    out = {'stats': stats, 'checks': re.findall(r'CHECK (.*)', txt), 'events': re.findall(r'EVENT (.*)', txt)[-6:], 'report': []}
    if res is None:
        out.update(outcome='TIMEOUT', why='бот не закончил за отведённое время')
    else:
        out.update(outcome=res[0], why=res[1])
    if out['outcome'] == 'PASS':
        for e, ok, info in check_expect(expects, stats):
            out['checks'].append(f'{e} ({info}) {"PASS" if ok else "FAIL"}')
            if not ok:
                out.update(outcome='FAIL', why=f'не выполнено: {e} ({info})')
    if out['outcome'] == 'CRASH' and report:
        out['report'] = crash_report()
    sh(f'am force-stop {PKG}')
    release()
    return out


def release():
    """После теста выключает бота в конфиге: иначе он будет перехватывать управление при обычной игре."""
    try:
        cfg = json.loads(read(f'{K}/config.json').lstrip('﻿'))
        cfg['enabled']['autotest'] = False
        cfg['session'] = dict(cfg['enabled'])
        fd, p = tempfile.mkstemp(suffix='.json'); os.close(fd)
        with open(p, 'w', encoding='utf-8') as f:
            json.dump(cfg, f, ensure_ascii=False, indent=2)
        adb('push', p, f'{K}/config.json'); os.remove(p)
    except Exception as e:
        log(f'не удалось выключить бота в конфиге: {e}')


def show(out):
    print(f'  ИТОГ: {out["outcome"]} — {out["why"]}')
    for c in out.get('checks', []):
        print('   CHECK', c)
    for e in out.get('events', []):
        print('   событие:', e)
    for k, v in out.get('stats', {}).items():
        print(f'   {k} = {v:g}')
    for r in out.get('report', []):
        print('   отчёт:', r)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('--mods', default='', help='моды через запятую (кроме autotest, он добавляется сам)')
    ap.add_argument('--set', action='append', default=[], help='настройка мода: мод.ключ=число (можно несколько раз)')
    ap.add_argument('--duration', type=int, default=150, help='секунд боя')
    ap.add_argument('--picks', type=int, default=0, help='завершить после N выбранных артефактов (0 — по времени)')
    ap.add_argument('--expect', action='append', default=[], help='условие по снимкам STAT, например "UnitApply_MagicBulletNum<=3"')
    ap.add_argument('--bisect', action='store_true', help='при вылете запустить по разу без каждого мода')
    ap.add_argument('--push', action='store_true', help='залить zip-моды на телефон перед запуском')
    ap.add_argument('--stand', action='store_true', help='не ходить (по умолчанию бот идёт к шарикам и сундуку)')
    a = ap.parse_args()
    mods = [m for m in a.mods.split(',') if m]
    settings = {}
    for s in a.set:
        k, v = s.split('=')
        mod, key = k.split('.')
        settings.setdefault(mod, {})[key] = float(v)
    if a.push:
        push_mods(mods + ['autotest'])
    base = run_once(mods, settings, a.duration, a.picks, a.expect, 0 if a.stand else 1)
    show(base)
    if a.bisect and base['outcome'] in ('CRASH', 'TIMEOUT') and len(mods) > 1:
        print('\nПЕРЕБОР: по разу без каждого мода')
        table = []
        for m in mods:
            sub = [x for x in mods if x != m]
            log(f'--- без {m}')
            r = run_once(sub, settings, a.duration, a.picks, a.expect, 0 if a.stand else 1, report=False)
            table.append((m, r['outcome'], r['why']))
        print('\nРезультат перебора (без мода → исход):')
        for m, o, w in table:
            print(f'  без {m:20s} → {o}  {w}')
        fixed = [m for m, o, _ in table if o == 'PASS']
        print('Виновник (после выключения игра не падает):', ', '.join(fixed) if fixed else 'не найден одним модом — причина в сочетании или в самой игре')
    sys.exit(0 if base['outcome'] == 'PASS' else 1)


if __name__ == '__main__':
    main()
