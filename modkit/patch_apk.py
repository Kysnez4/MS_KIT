r"""Встраивает frida-gadget в split-APK игры и подписывает всё одним ключом.

Запуск:  .venv\Scripts\python.exe modkit\patch_apk.py
Результат: modkit/out/*.apk  (ставятся через install.ps1)
"""
import os, shutil, subprocess, sys, zipfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "xapk")
OUT = os.path.join(ROOT, "modkit", "out")
GADGET = os.path.join(ROOT, "tools", "frida-gadget-16.7.19-android-arm64.so")
PKG = "com.vkslrzm.Zombie"
SDK = os.path.join(os.environ["LOCALAPPDATA"], "Android", "Sdk", "build-tools", "36.0.0")
KEYTOOL = "C:/Program Files/Java/jdk-11.0.17/bin/keytool.exe"
KS = os.path.join(ROOT, "modkit", "modkit.keystore")
ABI_DIR = "lib/arm64-v8a/"
GNAME = "libfrida-gadget.so" if os.environ.get("MODE") == "lief" else "libg.so"

# Гаджет читает loader.js из папки приложения на карте памяти (доступно без root).
CONFIG = ('{"interaction":{"type":"script",' f'"path":"/sdcard/Android/data/{PKG}/files/modkit/loader.js",' '"on_change":"ignore"},"teardown":"minimal"}')


def run(*a):
    subprocess.run(a, check=True, shell=a[0].endswith(".bat"))


def patch_arm64_split(src, dst):
    with zipfile.ZipFile(src) as zi, zipfile.ZipFile(dst, "w") as zo:
        for it in zi.infolist():
            if it.filename.startswith("META-INF/"):
                continue
            data = zi.read(it.filename)
            if it.filename == ABI_DIR + "libmain.so":
                # libm.so -> libg.so: подменяем имя зависимости in-place (раскладка файла не меняется)
                if os.environ.get("MODE") == "lief":
                    import lief
                    tmp = os.path.join(OUT, "libmain.so")
                    open(tmp, "wb").write(data)
                    elf = lief.ELF.parse(tmp)
                    elf.add_library(GNAME)
                    elf.write(tmp)
                    data = open(tmp, "rb").read()
                    os.remove(tmp)
                else:
                    assert data.count(b"libm.so") == 1
                    data = data.replace(b"libm.so", b"libg.so")
            zo.writestr(it, data, compress_type=it.compress_type)
        zo.write(GADGET, ABI_DIR + GNAME, compress_type=zipfile.ZIP_STORED)
        zo.writestr(ABI_DIR + GNAME[:-3] + ".config.so", CONFIG, compress_type=zipfile.ZIP_STORED)


def patch_base_manifest(src, dst):
    """Подменяет в строковой таблице манифеста необязательное разрешение ACCESS_ADSERVICES_AD_ID (42 символа)
    на MANAGE_EXTERNAL_STORAGE (тоже 42) — длина совпадает, поэтому структура файла не меняется."""
    old = "android.permission.ACCESS_ADSERVICES_AD_ID".encode("utf-16-le")
    new = "android.permission.MANAGE_EXTERNAL_STORAGE".encode("utf-16-le")
    assert len(old) == len(new)
    with zipfile.ZipFile(src) as zi, zipfile.ZipFile(dst, "w") as zo:
        for it in zi.infolist():
            if it.filename.startswith("META-INF/"):
                continue
            data = zi.read(it.filename)
            if it.filename == "AndroidManifest.xml":
                assert data.count(old) == 1, "строка разрешения в манифесте не найдена"
                data = data.replace(old, new)
            zo.writestr(it, data, compress_type=it.compress_type)


def main():
    os.makedirs(OUT, exist_ok=True)
    if not os.path.exists(KS):
        run(KEYTOOL, "-genkeypair", "-keystore", KS, "-storepass", "modkit123", "-keypass", "modkit123",
            "-alias", "modkit", "-keyalg", "RSA", "-keysize", "2048", "-validity", "10000",
            "-dname", "CN=modkit")
    for name in os.listdir(SRC):
        if not name.endswith(".apk"):
            continue
        raw = os.path.join(OUT, "raw_" + name)
        if name.startswith("config.arm64"):
            patch_arm64_split(os.path.join(SRC, name), raw)
        elif "config." not in name:
            patch_base_manifest(os.path.join(SRC, name), raw)
        else:
            shutil.copy(os.path.join(SRC, name), raw)
        aligned = os.path.join(OUT, name)
        run(os.path.join(SDK, "zipalign.exe"), "-f", "-P", "16", "4", raw, aligned)
        run(os.path.join(SDK, "apksigner.bat"), "sign", "--ks", KS, "--ks-pass", "pass:modkit123",
            "--key-pass", "pass:modkit123", aligned)
        os.remove(raw)
        print("OK", name)


if __name__ == "__main__":
    main()


