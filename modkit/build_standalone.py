r"""Собирает ОДИН автономный APK-клон игры: пакет com.vkslrzm.Zombix, frida-gadget и loader.js внутри.

Запуск:  python modkit\build_standalone.py        (предварительно: node modkit\build.mjs)
Результат: modkit/out/Zombix.apk — ставится на любой arm64-телефон тапом, без adb и без удаления оригинала.
"""
import os, re, re, struct, subprocess, sys, zipfile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import patch_apk as P

OLD, NEW = "com.vkslrzm.Zombie", "com.vkslrzm.Zombix"
assert len(OLD) == len(NEW)
BASE = os.path.join(P.SRC, "com.vkslrzm.Zombie-612.apk")
SPLIT = os.path.join(P.SRC, "config.arm64_v8a-612.apk")
LOADER = os.path.join(P.OUT, "loader.standalone.js")
MODS = []   # клон без встроенных модов; все моды — внешние zip из Download/ModKit
OUT_APK = os.path.join(P.OUT, "Zombix.apk")
RAW = os.path.join(P.OUT, "raw_Zombix.apk")
# relative path: the gadget resolves it next to itself (extracted native lib dir)
SCRIPT_NAME = "libmodkit.so"
CONFIG = '{"interaction":{"type":"script","path":"%s","on_change":"ignore"},"teardown":"minimal"}' % os.environ.get("SCRIPT_PATH", SCRIPT_NAME)


def rename(data):
    n = 0
    for enc in ("utf-16-le", "utf-8"):
        o, w = OLD.encode(enc), NEW.encode(enc)
        n += data.count(o)
        data = data.replace(o, w)
    return data, n


def patch_manifest(data):
    data, n = rename(data)
    old = "android.permission.ACCESS_ADSERVICES_AD_ID".encode("utf-16-le")
    assert data.count(old) == 1
    data = data.replace(old, "android.permission.MANAGE_EXTERNAL_STORAGE".encode("utf-16-le"))
    # requiredSplitTypes="base__abi" нельзя выполнить в одном APK -> прячем атрибут, подменив его id в resource map
    data = bytearray(data)
    pos, pool = 8, None
    while pos < len(data):
        t, hs, sz = struct.unpack_from("<HHI", data, pos)
        if t == 0x0001:
            pool = pos
        elif t == 0x0180:
            assert pool is not None
            cnt, = struct.unpack_from("<I", data, pool + 8)
            offs, = struct.unpack_from("<I", data, pool + 20)
            for i in range(cnt):
                so, = struct.unpack_from("<I", data, pool + 28 + 4 * i)
                p = pool + offs + so
                ln, = struct.unpack_from("<H", data, p)
                if data[p + 2:p + 2 + ln * 2].decode("utf-16-le") == "requiredSplitTypes":
                    rid, = struct.unpack_from("<I", data, pos + hs + 4 * i)
                    assert rid == 0x0101064E, hex(rid)
                    struct.pack_into("<I", data, pos + hs + 4 * i, 0x0101F000)
                    # Play Protect читает атрибут по имени -> переименовываем строку (та же длина)
                    data[p + 2:p + 2 + ln * 2] = "requiredSplitTypez".encode("utf-16-le")
                    print("manifest: пакет x%d, requiredSplitTypes скрыт" % n)
                    return bytes(data)
        pos += sz
    raise SystemExit("requiredSplitTypes не найден")


def build():
    os.makedirs(P.OUT, exist_ok=True)
    P.run("node", os.path.join(P.ROOT, "modkit", "build.mjs"), "--only", ",".join(MODS), "--out", "modkit/out/loader.standalone.js")
    with zipfile.ZipFile(BASE) as zb, zipfile.ZipFile(SPLIT) as zs, zipfile.ZipFile(RAW, "w") as zo:
        for it in zb.infolist():
            if it.filename.startswith("META-INF/"):
                continue
            data = zb.read(it.filename)
            if it.filename == "AndroidManifest.xml":
                data = patch_manifest(data)
            elif it.filename == "resources.arsc":
                data, n = rename(data)
                print("resources.arsc: замен", n)
            elif it.filename.endswith(".xml"):
                data, n = rename(data)
                if n: print(it.filename, "замен", n)
            elif not it.filename.endswith((".dex", ".so", ".png", ".ogg")) and OLD.encode() in data:
                print("ВНИМАНИЕ: имя пакета в", it.filename)
            zo.writestr(it, data, compress_type=it.compress_type)
        for it in zs.infolist():
            if not it.filename.startswith("lib/arm64-v8a/"):
                continue
            data = zs.read(it.filename)
            if it.filename == P.ABI_DIR + "libmain.so":
                assert data.count(b"libm.so") == 1
                data = data.replace(b"libm.so", b"libg.so")
            zo.writestr(it.filename, data, compress_type=zipfile.ZIP_STORED)
        zo.write(P.GADGET, P.ABI_DIR + "libg.so", compress_type=zipfile.ZIP_STORED)
        zo.writestr(P.ABI_DIR + "libg.config.so", CONFIG, compress_type=zipfile.ZIP_STORED)
        zo.write(LOADER, P.ABI_DIR + SCRIPT_NAME, compress_type=zipfile.ZIP_STORED)
    P.run(os.path.join(P.SDK, "zipalign.exe"), "-f", "-P", "16", "4", RAW, OUT_APK)
    P.run(os.path.join(P.SDK, "apksigner.bat"), "sign", "--ks", P.KS, "--ks-pass", "pass:modkit123",
          "--key-pass", "pass:modkit123", OUT_APK)
    os.remove(RAW)
    print("OK", OUT_APK, os.path.getsize(OUT_APK) // 1024 // 1024, "МБ")


if __name__ == "__main__":
    build()
