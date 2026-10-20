r"""Собирает чистый одиночный APK игры БЕЗ Frida (для патча LemonLoader/MelonLoader): пакет com.vkslrzm.Zombim.

Запуск:  python modkit\tools\build_melon_base.py
Результат: modkit/out/Zombim_base.apk (подписан ключом modkit; LemonLoader потом переподпишет его своим).
Берёт оригинальные base + split arm64 и делает то же переименование пакета, что build_standalone.py (клон ставится рядом с оригиналом).
"""
import os, sys, zipfile

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))
import patch_apk as P
import build_standalone as B

B.NEW = os.environ.get("MELON_PKG", "com.vkslrzm.Zombim")
TAG = B.NEW.split(".")[-1]
assert len(B.OLD) == len(B.NEW)
RAW = os.path.join(P.OUT, "raw_%s.apk" % TAG)
OUT = os.path.join(P.OUT, "%s_base.apk" % TAG)


def build():
    os.makedirs(P.OUT, exist_ok=True)
    with zipfile.ZipFile(B.BASE) as zb, zipfile.ZipFile(B.SPLIT) as zs, zipfile.ZipFile(RAW, "w") as zo:
        for it in zb.infolist():
            if it.filename.startswith("META-INF/"):
                continue
            data = zb.read(it.filename)
            if it.filename == "AndroidManifest.xml":
                data = B.patch_manifest(data)
            elif it.filename == "resources.arsc" or it.filename.endswith(".xml"):
                data, n = B.rename(data)
            zo.writestr(it, data, compress_type=it.compress_type)
        for it in zs.infolist():
            if it.filename.startswith("lib/arm64-v8a/"):
                zo.writestr(it.filename, zs.read(it.filename), compress_type=zipfile.ZIP_STORED)
    P.run(os.path.join(P.SDK, "zipalign.exe"), "-f", "-P", "16", "4", RAW, OUT)
    P.run(os.path.join(P.SDK, "apksigner.bat"), "sign", "--ks", P.KS, "--ks-pass", "pass:modkit123",
          "--key-pass", "pass:modkit123", OUT)
    os.remove(RAW)
    print("OK", OUT, os.path.getsize(OUT) // 1024 // 1024, "МБ")


if __name__ == "__main__":
    build()
