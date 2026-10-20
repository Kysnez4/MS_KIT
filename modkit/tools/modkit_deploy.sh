#!/bin/bash
# Собрать загрузчик ModKit (C#) и залить на телефон в Zombim; перезапустить игру и показать журнал.
# Запуск (Git Bash):  bash modkit/tools/modkit_deploy.sh [секунд_ожидания]
set -e
export MSYS_NO_PATHCONV=1
ADB="$LOCALAPPDATA/Android/Sdk/platform-tools/adb.exe"; P=com.vkslrzm.Zombim; M=/sdcard/MelonLoader/$P
cd "$(dirname "$0")/../melon/ModKit"
dotnet build -c Release 2>&1 | grep -E "error|Ошибок" | sort -u
$ADB shell am force-stop $P
$ADB push "$(cygpath -m "$PWD")/bin/Release/net8.0/ModKit.dll" $M/Mods/ | tail -1
$ADB logcat -c
$ADB shell am start -n $P/com.unity3d.player.UnityPlayerActivity | tail -1
sleep "${1:-40}"
echo "--- modkit.log"; $ADB shell "cat $M/UserData/ModKit/modkit.log" || true
