import "frida-il2cpp-bridge";
import { installArtifactHooks } from "./artifacts";
import { register, start } from "./core";
import { loadExternalMods } from "./ext";
import { log } from "./log";
import { requestAllFilesAccess } from "./storage";
import { installMenu } from "./ui";
import allMods from "./mods.gen";

log("ModKit запущен, модов: " + allMods.length);
register(allMods);

Il2Cpp.perform(() => {
    log("IL2CPP готов, Unity " + Il2Cpp.unityVersion);
    const ext = loadExternalMods();
    log(`внешних файлов: ${ext.files}, подключено модов: ${ext.added.length}, ошибок: ${ext.errors.length}`);
    installArtifactHooks();
    start();
});

try { requestAllFilesAccess(); } catch (e: any) { log("storage: " + e + " " + e.stack); }
installMenu();
