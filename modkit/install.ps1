# Собирает патченый APK (frida-gadget внутри) и ставит его на телефон. Оригинальную игру нужно удалить —
# подпись другая, поэтому локальные сохранения пропадут (сделайте резервную копию заранее).
#   .\modkit\install.ps1 [-Serial 08990372A3000411]
param([string]$Serial = "08990372A3000411")
$adb = "$env:LOCALAPPDATA\Android\Sdk\platform-tools\adb.exe"
$root = Split-Path -Parent $PSScriptRoot
& "$root\.venv\Scripts\python.exe" "$PSScriptRoot\patch_apk.py"
& $adb -s $Serial uninstall com.vkslrzm.Zombie
& $adb -s $Serial install-multiple "$PSScriptRoot\out\com.vkslrzm.Zombie-612.apk" "$PSScriptRoot\out\config.arm64_v8a-612.apk"
# доступ ко всем файлам — чтобы игра читала моды из Download/ModKit
& $adb -s $Serial shell appops set com.vkslrzm.Zombie MANAGE_EXTERNAL_STORAGE allow
& node "$PSScriptRoot\build.mjs"
& "$PSScriptRoot\push.ps1" "$PSScriptRoot\out\loader.js" $Serial
