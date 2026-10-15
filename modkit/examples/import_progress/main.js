// Перенос прогресса из оригинальной игры (com.vkslrzm.Zombie) в Zombix.
// Сохранение — набор шифрованных *.json в Android/data/<пакет>/files. Копируется весь набор целиком (файлы сверяются друг с другом).
// Источники по порядку: папка оригинала напрямую; Download/ModKit/progress (туда файлы кладёт пользователь или этот же мод из оригинала с ModKit).
// Перед заменой текущие сохранения уходят в modkit/progress_backup/<время>. После записи игра закрывается, иначе она перезапишет файлы из памяти.
const K = ModKit;
const U = K.ui;
const ORIGINAL_PKG = "com.vkslrzm.Zombie";
const ORIGINAL_DIR = `/sdcard/Android/data/${ORIGINAL_PKG}/files`;
const SHARED_DIR = "/sdcard/Download/ModKit/progress";
const BACKUP_DIR = K.dir + "/progress_backup";
const IS_ORIGINAL = K.pkg === ORIGINAL_PKG;
const libc = n => Process.getModuleByName("libc.so").getExportByName(n);

/** Имена файлов каталога по фильтру; null — каталог не открылся (нет доступа или нет такой папки). */
function listDir(path, filter) {
    const opendir = new NativeFunction(libc("opendir"), "pointer", ["pointer"]);
    const readdir = new NativeFunction(libc("readdir"), "pointer", ["pointer"]);
    const closedir = new NativeFunction(libc("closedir"), "int", ["pointer"]);
    const dir = opendir(Memory.allocUtf8String(path));
    if (dir.isNull()) return null;
    const names = [];
    try {
        for (;;) {
            const ent = readdir(dir);
            if (ent.isNull()) break;
            const name = ent.add(19).readUtf8String();        // struct dirent64 на arm64
            if (name && filter(name)) names.push(name);
        }
    } finally { closedir(dir); }
    return names.sort();
}
const isSave = n => n.toLowerCase().endsWith(".json") && !n.startsWith("_");
const makeDir = p => { K.mkdirs(p); K.chmod(p, 0o777); };
const thousands = v => String(v).replace(/\B(?=(\d{3})+(?!\d))/g, " ");

function decode(raw) {
    try {
        const aes = Il2Cpp.domain.assembly("Assembly-CSharp").image.class("Z_AESCrypto");
        return JSON.parse(aes.method("AESDecrypt128", 1).invoke(Il2Cpp.string(raw.trim())).content);
    } catch (_) { return null; }
}

/** Осматривает каталог сохранения: доступ, наличие List_SingleValue, золото/алмазы/исследования. */
function inspect(title, dir) {
    const s = { title, dir, problem: null, files: [], gold: "?", gems: "?", level: "?" };
    const files = listDir(dir, isSave);
    if (files === null) { s.problem = "нет доступа к папке"; return s; }
    s.files = files;
    if (!files.includes("List_SingleValue.json")) { s.problem = "сохранения не найдены"; return s; }
    const raw = K.readText(`${dir}/List_SingleValue.json`);
    const sv = raw === null ? null : decode(raw);
    if (!sv) { s.problem = "сохранение не читается"; return s; }
    s.gold = thousands(sv[5]); s.gems = thousands(sv[9]); s.level = String(sv[7]);
    return s;
}
const describe = s => `золото ${s.gold}, алмазы ${s.gems}, исследования ${s.level}, файлов ${s.files.length}`;

function readAll(dir, files) {
    const out = new Map();
    for (const f of files) {
        const t = K.readText(`${dir}/${f}`);
        if (t === null) throw new Error(`не читается ${f}`);
        out.set(f, t);
    }
    return out;
}

function killSelf() {
    const pid = new NativeFunction(libc("getpid"), "int", [])();
    new NativeFunction(libc("kill"), "int", ["int", "int"])(pid, 9);
}

/** Заменяет сохранения этой игры набором snap, сохранив текущие в копию, и закрывает игру. */
function applySnapshot(snap) {
    const incoming = readAll(snap.dir, snap.files);                    // сначала всё читаем: при ошибке ничего не тронуто
    const current = listDir(K.appDir, isSave) || [];
    const stamp = new Date().toISOString().replace(/\D/g, "").substring(0, 14);
    makeDir(`${BACKUP_DIR}/${stamp}`);
    for (const [f, t] of readAll(K.appDir, current)) K.writeText(`${BACKUP_DIR}/${stamp}/${f}`, t);
    for (const [f, t] of incoming) {
        K.writeText(`${K.appDir}/${f}`, t);
        if (K.readText(`${K.appDir}/${f}`) !== t) throw new Error(`не записался ${f}`);
    }
    K.log(`[import_progress] из ${snap.dir}: файлов ${incoming.size}, копия прежних в ${BACKUP_DIR}/${stamp}; закрываю игру`);
    killSelf();
}

/** В оригинале с ModKit: выкладывает сохранения в Download/ModKit/progress. */
function exportSaves() {
    const files = listDir(K.appDir, isSave) || [];
    if (!files.includes("List_SingleValue.json")) return "Сохранение не найдено.";
    makeDir(SHARED_DIR);
    for (const [f, t] of readAll(K.appDir, files)) K.writeText(`${SHARED_DIR}/${f}`, t);
    K.log(`[import_progress] экспорт: ${files.length} файлов в ${SHARED_DIR}`);
    return `Готово: ${files.length} файлов в Download/ModKit/progress. Откройте этот мод в Zombix.`;
}

// ---------------------------------------------------------------- окно
let win = null;
function openWindow() {
    const { kit, node, image, label, button, clearChildren, color, gold, tint, v2, place, LOGICAL_W, cls, CORE } = U;
    if (win) { try { cls(CORE, "UnityEngine.Object").method("Destroy", 1).invoke(win); } catch (_) { /* уже удалено */ } }
    const root = node("ModKitImportProgress", kit.canvasTf);
    win = root.go;
    root.rt.method("set_anchorMin").invoke(v2(0, 0));
    root.rt.method("set_anchorMax").invoke(v2(1, 1));
    root.rt.method("set_offsetMin").invoke(v2(0, 0));
    root.rt.method("set_offsetMax").invoke(v2(0, 0));
    image(root.go, 0.02, 0.03, 0.06, 1);
    tint(label(root.rt, IS_ORIGINAL ? "Экспорт прогресса" : "Импорт прогресса", 0, 90, LOGICAL_W, 80, 46, 4), gold());
    button(root.rt, "X", LOGICAL_W - 100, 60, 80, 80, 54, [0, 0, 0, 0.01], () => root.go.method("SetActive").invoke(false));
    const body = node("body", root.rt);
    place(body.rt, 0, 190, LOGICAL_W, kit.screenH - 190);

    const W = LOGICAL_W - 60;
    const text = (y, s, h, size = 23, c = color(0.85, 0.88, 0.95, 1)) => tint(label(body.rt, s, 30, y, W, h, size, 0), c);
    const btn = (y, s, bg, fn) => button(body.rt, s, 30, y, W, 80, 28, bg, fn);
    const show = view => { clearChildren(body.rt); try { view(); } catch (e) { K.log("[import_progress] ошибка окна: " + e); text(0, "Ошибка: " + e, 200); } };
    const green = [0.20, 0.38, 0.22, 1], red = [0.38, 0.20, 0.20, 1], blue = [0.18, 0.28, 0.42, 1], gray = [0.22, 0.22, 0.22, 1];

    const done = message => show(() => { text(0, message, 400, 26, gold()); btn(420, "Назад", gray, main); });

    const confirm = snap => show(() => {
        text(0, `Заменить сохранения Zombix данными из «${snap.title}»?`, 90, 28, gold());
        text(100, `Будет: ${describe(snap)}.`, 100);
        text(210, "Текущий прогресс Zombix сохранится в копию. После записи игра закроется: запустите её снова.", 130, 23, color(0.95, 0.8, 0.6, 1));
        btn(380, "Заменить и закрыть игру", green, () => {
            try { applySnapshot(snap); } catch (e) {
                K.log("[import_progress] ошибка: " + e);
                done("Не получилось: " + e + ". Текущие сохранения не тронуты.");
            }
        });
        btn(480, "Отмена", red, main);
    });

    const main = () => show(() => {
        if (IS_ORIGINAL) {
            text(0, "Выложить сохранения этой игры в Download/ModKit/progress, чтобы забрать их в Zombix. Файл на диске обновляется автосохранением игры.", 150);
            btn(170, "Экспортировать прогресс", green, () => done(exportSaves()));
            return;
        }
        const found = [inspect("Оригинальная игра", ORIGINAL_DIR), inspect("Download/ModKit/progress", SHARED_DIR)];
        const src = found.find(s => !s.problem) || null;
        let y = 0;
        for (const s of found) {
            text(y, `${s.title}: ${s.problem || describe(s)}`, 70, 22, s.problem ? color(0.65, 0.65, 0.65, 1) : color(0.5, 1, 0.5, 1));
            y += 76;
        }
        const now = inspect("Zombix", K.appDir);
        text(y, `Сейчас в Zombix: ${now.problem || describe(now)}`, 70, 22, color(0.9, 0.8, 0.5, 1));
        y += 90;
        if (src) {
            btn(y, `Импортировать из: ${src.title}`, green, () => confirm(src));
            y += 100;
        } else {
            text(y, "Источник не найден: Android закрыл доступ к папке оригинала. Скопируйте файлы *.json из Android/data/com.vkslrzm.Zombie/files в Download/ModKit/progress (на телефоне или с ПК) и откройте окно снова.", 260, 22, color(0.95, 0.8, 0.6, 1));
            y += 270;
        }
        const last = (listDir(BACKUP_DIR, n => /^\d{14}$/.test(n)) || []).pop();
        if (last) {
            const b = inspect("Копия " + last, `${BACKUP_DIR}/${last}`);
            if (!b.problem) {
                btn(y, `Вернуть копию ${last.substring(6, 8)}.${last.substring(4, 6)} ${last.substring(8, 10)}:${last.substring(10, 12)}`, blue, () => confirm(b));
                y += 100;
            }
        }
    });
    main();
}

ModKit.define({
    id: "ext_import_progress",
    name: "Импорт прогресса",
    onEnable(ctx) { ctx.log(`[import_progress] роль: ${IS_ORIGINAL ? "экспорт" : "импорт"}, пакет ${K.pkg}`); },
    openSettings() { openWindow(); },
});
