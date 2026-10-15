# Заливает файл на телефон как loader.js:  .\modkit\push.ps1 modkit\out\dump.js
param([string]$File = "modkit\out\loader.js", [string]$Serial = "08990372A3000411")
$adb = "$env:LOCALAPPDATA\Android\Sdk\platform-tools\adb.exe"
$d = "/sdcard/Android/data/com.vkslrzm.Zombie/files/modkit"
& $adb -s $Serial shell "mkdir -p $d; chmod 777 $d"
& $adb -s $Serial push $File /data/local/tmp/loader.js | Out-Null
& $adb -s $Serial shell "cp /data/local/tmp/loader.js $d/loader.js; chmod 644 $d/loader.js"
& $adb -s $Serial shell am force-stop com.vkslrzm.Zombie
& $adb -s $Serial shell am start -n com.vkslrzm.Zombie/com.unity3d.player.UnityPlayerActivity | Out-Null
