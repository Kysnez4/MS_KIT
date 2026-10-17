// Резервная копия сохранений в Download/ModKit/backup/<пакет>/latest (+ prev — предыдущая копия).
// Набор *.json копируется целиком, как в import_progress. Копия делается после боя (через 8 с, когда игра дописала автосохранение)
// и по таймеру; если ничего не изменилось с прошлой копии, файлы не трогаются.
// Восстановление: скопировать файлы из latest (или prev) в Android/data/<пакет>/files при закрытой игре
// либо положить их в Download/ModKit/progress и открыть мод «Импорт прогресса».
const K = ModKit;
const ROOT = `/sdcard/Download/ModKit/backup/${K.pkg}`;
const libc = n => Process.getModuleByName("libc.so").getExportByName(n);

function listDir(path) {
    const opendir = new NativeFunction(libc("opendir"), "pointer", ["pointer"]);
    const readdir = new NativeFunction(libc("readdir"), "pointer", ["pointer"]);
    const closedir = new NativeFunction(libc("closedir"), "int", ["pointer"]);
    const dir = opendir(Memory.allocUtf8String(path));
    if (dir.isNull()) return [];
    const names = [];
    try {
        for (;;) {
            const ent = readdir(dir);
            if (ent.isNull()) break;
            const name = ent.add(19).readUtf8String();         // struct dirent64 на arm64
            if (name && name.toLowerCase().endsWith(".json") && !name.startsWith("_")) names.push(name);
        }
    } finally { closedir(dir); }
    return names.sort();
}

function snapshot(dir) {
    const out = new Map();
    for (const f of listDir(dir)) {
        const t = K.readText(`${dir}/${f}`);
        if (t === null) return null;
        out.set(f, t);
    }
    return out;
}
const same = (a, b) => !!b && a.size === b.size && [...a].every(([f, t]) => b.get(f) === t);

function writeSet(dir, set) {
    K.mkdirs(dir);
    K.chmod(dir, 0o777);
    for (const [f, t] of set) K.writeText(`${dir}/${f}`, t);
}

let busy = false;
function backup(reason) {
    if (busy) return;
    busy = true;
    try {
        const now = snapshot(K.appDir);
        if (!now || !now.has("List_SingleValue.json")) return;        // нет сохранения или файл занят записью — попробуем в следующий раз
        const last = snapshot(`${ROOT}/latest`);
        if (same(now, last)) return;
        if (last && last.size) writeSet(`${ROOT}/prev`, last);
        writeSet(`${ROOT}/latest`, now);
        K.log(`[progress_backup] ${reason}: ${now.size} файлов -> ${ROOT}/latest`);
    } catch (e) {
        K.log(`[progress_backup] ошибка: ${e}`);
    } finally { busy = false; }
}

ModKit.define({
    onEnable(ctx) {
        const minutes = ctx.settings.minutes || 5;
        ctx.every(minutes * 60000, () => backup("по таймеру"));
        ctx.events.on("battleEnd", () => {
            const h = setTimeout(() => Il2Cpp.perform(() => backup("после боя")), 8000);
            ctx.onCleanup(() => clearTimeout(h));
        });
        backup("при запуске");
    },
});
