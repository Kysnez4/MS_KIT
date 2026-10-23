#!/bin/bash
# Полный цикл: чистый APK игры -> патч LemonLoader (с исправленными libunity и Il2CppInterop) -> установка на телефон.
# Запуск (Git Bash):  bash modkit/tools/melon_build.sh
# Внешние файлы лежат в D:\tools\lemon: deps\libunity.so.arm64-v8a (из LavaGang/MelonLoader.UnityDependencies 2022.3.62), deps\melon_data_patched.zip
# (melon_data.zip LemonLoader 0.6.5.1 после modkit/tools/il_patch). Патчер удаляет переданные ему файлы, поэтому каждый раз берём свежие копии.
set -e
export MSYS_NO_PATHCONV=1
ROOT="D:/Projects/frida"; L="D:/tools/lemon"; PKG=${PKG:-com.vkslrzm.Zombim}; export MELON_PKG=$PKG; TAG=${PKG##*.}
ADB="$LOCALAPPDATA/Android/Sdk/platform-tools/adb.exe"; SER=08990372A3000411

python "$ROOT/modkit/tools/build_melon_base.py" | tail -1
# libunity: UNITY_LIB=orig (по умолчанию) — родная библиотека игры (f3); UNITY_LIB=lemon — сборка LemonLoader для f1 с подправленной
# строкой версии (с ней игра падает: Scudo «invalid chunk state», сборки f1 и f3 различаются по коду); UNITY_LIB=full — полная
# Release-библиотека f3 без вырезания движка (deps\libunity_f3_full.so.arm64-v8a из Android Build Support 2022.3.62f3,
# Variations/il2cpp/Release/Libs/arm64-v8a; UNITY_LIB=dev — Development): в ней есть Sprite.Create, НО игра с ней падает —
# движок вызывает вырезанные из игры C#-методы NativeInputSystem (см. docs/MELONLOADER.md)
UNITY_LIB="${UNITY_LIB:-orig}" python - <<'EOF'
import os, zipfile
if os.environ['UNITY_LIB'] == 'orig':
    d = zipfile.ZipFile(r'D:\Projects\frida\modkit\out\config.arm64_v8a-612.apk').read('lib/arm64-v8a/libunity.so')
elif os.environ['UNITY_LIB'] in ('full', 'dev'):
    d = open(r'D:\tools\lemon\deps\libunity_f3_%s.so.arm64-v8a' % os.environ['UNITY_LIB'], 'rb').read()
else:
    d = open(r'D:\tools\lemon\deps\libunity.so.arm64-v8a', 'rb').read()
    d = d.replace(b'2022.3.62f1', b'2022.3.62f3').replace(b'4af31df58517', b'96770f904ca7')
with zipfile.ZipFile(r'D:\tools\lemon\unity_f3.zip', 'w', zipfile.ZIP_STORED) as z:
    z.writestr('arm64-v8a/libunity.so', d)
print('libunity:', os.environ['UNITY_LIB'], len(d))
EOF
cp -f "$L/deps/melon_data_patched.zip" "$L/melon_data.zip"
rm -rf "$L/out" "$L/tmp"; mkdir -p "$L/out"
dotnet "$ROOT/modkit/tools/lemon_patch/bin/Release/net10.0/LemonPatch.dll" "$ROOT/modkit/out/${TAG}_base.apk" "$L/out" $PKG "$L/melon_data.zip" "$L/tmp" "$L/unity_f3.zip" | tail -2
python - <<'EOF'
import zipfile,re
d = zipfile.ZipFile(r'D:\tools\lemon\out\base.apk').read('lib/arm64-v8a/libunity.so')
print('libunity в APK:', [m.group() for m in re.finditer(rb'2022\.3\.62f\d', d)][:1])
EOF
[ -n "$BUILD_ONLY" ] && { echo "собран $L/out/base.apk (BUILD_ONLY)"; exit 0; }
"$ADB" -s $SER uninstall $PKG >/dev/null 2>&1 || true
"$ADB" -s $SER install -r "$L/out/base.apk" | tail -1
"$ADB" -s $SER shell appops set $PKG MANAGE_EXTERNAL_STORAGE allow
"$ADB" -s $SER shell pm grant $PKG android.permission.READ_EXTERNAL_STORAGE 2>/dev/null || true
"$ADB" -s $SER shell pm grant $PKG android.permission.WRITE_EXTERNAL_STORAGE 2>/dev/null || true
"$ADB" -s $SER shell "rm -rf /sdcard/MelonLoader/$PKG"
"$ADB" -s $SER logcat -c
"$ADB" -s $SER shell am start -n $PKG/com.unity3d.player.UnityPlayerActivity | tail -1
