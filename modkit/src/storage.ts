import JavaBridge from "frida-java-bridge";
import { log } from "./log";

// frida-compile отдаёт модуль то с default, то без
const Java: typeof JavaBridge = (JavaBridge as any).perform ? JavaBridge : (JavaBridge as any).default;
const say = (m: string) => log("storage: " + m);

// мост рассчитан на Frida 17, встроенный gadget старше: недостающие функции Module
const M = Module as any;
M.getGlobalExportByName ??= (name: string) => { const p = M.findExportByName(null, name); if (!p) throw new Error("нет экспорта " + name); return p; };
M.findGlobalExportByName ??= (name: string) => M.findExportByName(null, name);

/**
 * При запуске просит «Доступ ко всем файлам» (Android 11+), если его нет: без него не читаются zip-моды из Download/ModKit.
 * Экран открывается из активной Activity Unity: MIUI (Redmi, Xiaomi) не даёт запускать окна из фона, т.е. из контекста приложения.
 * Если экран для конкретного приложения не открылся, открывает общий список «Доступ ко всем файлам».
 * После выдачи доступа моды подхватятся со следующего запуска.
 */
export function requestAllFilesAccess(tries = 0): void {
    if (!tries) say("проверка, Java " + Java.available);
    if (!Java.available) return;
    const retry = () => { if (tries < 120) setTimeout(() => requestAllFilesAccess(tries + 1), 500); else say("Activity так и не появилась"); };
    Java.perform(() => {
        try {
            if (Java.use("android.os.Build$VERSION").SDK_INT.value < 30) return;
            if (Java.use("android.os.Environment").isExternalStorageManager()) { if (!tries) say("доступ уже есть"); return; }
            const act = Java.use("com.unity3d.player.UnityPlayer").currentActivity.value;
            if (act === null) { retry(); return; }
            Java.scheduleOnMainThread(() => {
                Java.perform(() => {
                    const Intent = Java.use("android.content.Intent");
                    const Uri = Java.use("android.net.Uri");
                    try {
                        act.startActivity(Intent.$new("android.settings.MANAGE_APP_ALL_FILES_ACCESS_PERMISSION", Uri.parse("package:" + act.getPackageName())));
                        say("открыт экран доступа для приложения");
                    } catch (e) {
                        say("экран приложения не открылся (" + e + "), открываю общий");
                        try { act.startActivity(Intent.$new("android.settings.MANAGE_ALL_FILES_ACCESS_PERMISSION")); say("открыт общий экран"); }
                        catch (e2) { say("не удалось открыть настройки: " + e2); }
                    }
                });
            });
        } catch (e) { say("ошибка: " + e); }
    });
}
