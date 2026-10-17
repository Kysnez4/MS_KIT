// Имя пакета берём из /proc/self/cmdline: тот же loader.js работает и в оригинале, и в клоне с другим пакетом.
function detectPkg(): string {
    // путь к libg.so: /data/app/~~xxx/<пакет>-yyy==/lib/arm64/libg.so
    for (const lib of ["libg.so", "libmain.so"]) {
        try {
            const m = /\/([A-Za-z][\w.]*)-[\w=-]{16,}\/lib\//.exec(Process.getModuleByName(lib).path);
            if (m) return m[1];
        } catch (_) { /* модуль не найден */ }
    }
    try {
        const name = File.readAllText("/proc/self/cmdline").split(String.fromCharCode(0))[0].split(":")[0];
        if (/^[\w.]+$/.test(name)) return name;
    } catch (_) { /* fallback ниже */ }
    return "com.vkslrzm.Zombie";
}
export const PKG = detectPkg();
export const APP_DIR = `/sdcard/Android/data/${PKG}/files`;
export const DIR = APP_DIR + "/modkit";
export const LOG_PATH = DIR + "/modkit.log";
export const CONFIG_PATH = DIR + "/config.json";

let chmodFn: NativeFunction<number, [NativePointer, number]> | null = null;
export function chmod(path: string, mode: number): void {
    try {
        chmodFn ??= new NativeFunction(Process.getModuleByName("libc.so").getExportByName("chmod"), "int", ["pointer", "int"]);
        chmodFn(Memory.allocUtf8String(path), mode);
    } catch (_) { /* не критично */ }
}

export function chmod644(path: string): void {
    try {
        chmodFn ??= new NativeFunction(Process.getModuleByName("libc.so").getExportByName("chmod"), "int", ["pointer", "int"]);
        chmodFn(Memory.allocUtf8String(path), 0o644);
    } catch (_) { /* не критично */ }
}

/** Создаёт каталог со всеми родителями (папки modkit при чистой установке нет). */
export function mkdirs(path: string): void {
    try {
        const mkdir = new NativeFunction(Process.getModuleByName("libc.so").getExportByName("mkdir"), "int", ["pointer", "int"]);
        let cur = "";
        for (const part of path.split("/").filter(Boolean)) {
            cur += "/" + part;
            mkdir(Memory.allocUtf8String(cur), 0o777);
        }
    } catch (_) { /* не критично */ }
}

/** Удаляет файл (libc unlink). true — удалён. */
export function removeFile(path: string): boolean {
    try {
        const unlink = new NativeFunction(Process.getModuleByName("libc.so").getExportByName("unlink"), "int", ["pointer"]);
        return unlink(Memory.allocUtf8String(path)) === 0;
    } catch (_) { return false; }
}

let alogFn: NativeFunction<number, [number, NativePointer, NativePointer]> | null = null;
/** Дублирует сообщение в logcat (тег modkit) — видно через `adb logcat -s modkit`, даже если файловый лог недоступен. */
export function alog(msg: string): void {
    try {
        alogFn ??= new NativeFunction(Process.getModuleByName("liblog.so").getExportByName("__android_log_write"), "int", ["int", "pointer", "pointer"]);
        alogFn(4, Memory.allocUtf8String("modkit"), Memory.allocUtf8String(msg));
    } catch (_) { /* не критично */ }
}
alog("loader стартовал, пакет " + PKG + ", каталог " + DIR);

mkdirs(DIR + "/mods");
mkdirs("/sdcard/Download/ModKit");

let logFile: File | null = null;
export function log(msg: string): void {
    try {
        if (!logFile) {
            logFile = new File(LOG_PATH, "a");
            chmod644(LOG_PATH);
        }
        alog(msg);
        logFile.write(`[${new Date().toISOString().substring(11, 19)}] ${msg}\n`);
        logFile.flush();
    } catch (e) { alog("файловый лог недоступен: " + e); }
    console.log("[modkit] " + msg);
}

export function readText(path: string): string | null {
    try {
        return File.readAllText(path);
    } catch (_) {
        return null;
    }
}

export function writeText(path: string, text: string): void {
    const f = new File(path, "w");
    f.write(text);
    f.close();
    chmod644(path);
}
