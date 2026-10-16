import type { ModDef } from "./api";
import { addExternal, mods } from "./core";
import { APP_DIR, DIR, PKG, chmod, log, mkdirs, readText, writeText } from "./log";
import * as ui from "./uikit";
import * as data from "./data";
import { ZipReader } from "./zip";

/**
 * Внешние моды: архивы .zip. При старте грузятся установленные (files/modkit/mods); новые ищет кнопка «+»
 * в Download/ModKit и Download (только архивы с mod.json), выбранный копируется в files/modkit/mods.
 * Внутри: `mod.json` (META: id, название, описание, иконка, скриншоты...) и скрипт с `ModKit.define({...})` — см. docs/README.md.
 */
// Download/ читается только при выданном «доступе ко всем файлам» (install.ps1 выдаёт его через appops).
export const MODS_DIRS = [DIR + "/mods"];
/** Где «+» ищет архивы с mod.json (только по нажатию). Установленные копируются в MODS_DIRS[0]. */
export const SEARCH_DIRS = ["/sdcard/Download/ModKit", "/sdcard/Download"];

function libc(name: string): NativePointer {
    return Process.getModuleByName("libc.so").getExportByName(name);
}

function listZips(dirPath: string): string[] {
    const opendir = new NativeFunction(libc("opendir"), "pointer", ["pointer"]);
    const readdir = new NativeFunction(libc("readdir"), "pointer", ["pointer"]);
    const closedir = new NativeFunction(libc("closedir"), "int", ["pointer"]);
    const dir = opendir(Memory.allocUtf8String(dirPath));
    if (dir.isNull()) return [];
    const names: string[] = [];
    try {
        for (;;) {
            const ent = readdir(dir);
            if (ent.isNull()) break;
            const name = ent.add(19).readUtf8String();        // struct dirent64 на arm64: d_name со смещения 19
            if (name && name.toLowerCase().endsWith(".zip") && !name.startsWith("_")) names.push(name);
        }
    } finally {
        closedir(dir);
    }
    return names.sort();
}

export interface LoadResult {
    added: string[];
    errors: string[];
    files: number;
}

/** Создаёт папку модов в каталоге приложения и подгружает новые файлы из всех папок. */
export function loadExternalMods(): LoadResult {
    const res: LoadResult = { added: [], errors: [], files: 0 };
    try {
        new NativeFunction(libc("mkdir"), "int", ["pointer", "int"])(Memory.allocUtf8String(MODS_DIRS[0]), 0o777);
        chmod(MODS_DIRS[0], 0o777);
    } catch (_) { /* уже есть */ }

    for (const dirPath of MODS_DIRS) {
        for (const file of listZips(dirPath)) {
            res.files++;
            try {
                const def = readModZip(dirPath + "/" + file);
                if (addExternal(def)) { res.added.push(def.name); log(`мод загружен: ${def.id} (${dirPath}/${file})`); }
            } catch (e) {
                res.errors.push(`${file}: ${e}`);
                log(`мод ${file}: ошибка ${e}`);
            }
        }
    }
    return res;
}

export interface Candidate { path: string; file: string; id: string; name: string; version?: string; author?: string; installed: boolean }
const MAX_SCAN_BYTES = 40 * 1024 * 1024;

/** Ищет в папках загрузок .zip с mod.json (содержимое скрипта не выполняется). */
export function findCandidates(): Candidate[] {
    const out: Candidate[] = [];
    const seen = new Set<string>();
    for (const dirPath of SEARCH_DIRS) {
        for (const file of listZips(dirPath)) {
            const path = dirPath + "/" + file;
            if (seen.has(path)) continue;
            seen.add(path);
            try {
                const bytes = File.readAllBytes(path);
                if (bytes.byteLength > MAX_SCAN_BYTES) continue;
                const zip = new ZipReader(new Uint8Array(bytes));
                if (!zip.has("mod.json")) continue;
                const meta = JSON.parse(zip.text("mod.json"));
                const id = String(meta.id ?? "");
                if (!id) continue;
                out.push({
                    path, file, id, name: String(meta.name ?? id),
                    version: meta.version ? String(meta.version) : undefined,
                    author: meta.author ? String(meta.author) : undefined,
                    installed: mods.some(m => m.def.id === id),
                });
            } catch (_) { /* не мод или битый архив */ }
        }
    }
    return out;
}

/** Подключает мод из найденного архива и копирует его в папку установленных. Бросает исключение с причиной. */
export function installCandidate(c: Candidate): string {
    const def = readModZip(c.path);
    if (!addExternal(def)) throw new Error("мод с таким id уже установлен");
    try {
        mkdirs(MODS_DIRS[0]);
        const dest = MODS_DIRS[0] + "/" + c.file;
        const f = new File(dest, "wb");
        f.write(File.readAllBytes(c.path));
        f.close();
        chmod(dest, 0o666);
    } catch (e) { log(`копия ${c.file} в mods не удалась: ${e} (мод работает до перезапуска)`); }
    log(`мод установлен: ${def.id} (${c.path})`);
    return def.name;
}

const strList = (v: unknown): string[] => Array.isArray(v) ? v.filter((x): x is string => typeof x === "string") : [];

/** Разбирает архив мода: mod.json + скрипт + картинки. Бросает исключение с понятным текстом. */
function readModZip(path: string): ModDef {
    const bytes = File.readAllBytes(path);
    const zip = new ZipReader(new Uint8Array(bytes));
    if (!zip.has("mod.json")) throw new Error("нет файла mod.json");
    const meta = JSON.parse(zip.text("mod.json"));
    const mainName: string = meta.main ?? "main.js";
    if (!zip.has(mainName)) throw new Error(`нет скрипта ${mainName}`);

    const pending: ModDef[] = [];
    // ui — инструменты uikit.ts для своих экранов (окно рисуется на холсте игры: ui.kit.canvasTf); data — таблицы игры (data.ts: Table и др.)
    (globalThis as any).ModKit = {
        define: (d: ModDef) => { pending.push(d); return d; },
        ui, data, pkg: PKG, appDir: APP_DIR, dir: DIR, log, mkdirs, chmod, readText, writeText,
    };
    (0, eval)(zip.text(mainName));
    if (pending.length !== 1) throw new Error(`${mainName}: нужен один вызов ModKit.define(...)`);
    const def = pending[0];
    if (typeof def.onEnable !== "function") throw new Error("в ModKit.define нет onEnable");

    // META главнее данных в скрипте
    def.id = meta.id ?? def.id;
    def.name = meta.name ?? def.name;
    def.description = meta.description ?? def.description;
    if (meta.options) def.options = meta.options;
    if (meta.restartRequired !== undefined) def.restartRequired = !!meta.restartRequired;
    if (meta.defaultEnabled !== undefined) def.defaultEnabled = !!meta.defaultEnabled;
    if (!def.id || !/^[\w.-]+$/.test(def.id)) throw new Error("в mod.json нужен id (латиница, цифры, _ . -)");
    if (!def.name) throw new Error("в mod.json нужен name");

    const image = (n: string): Uint8Array | null => (n && zip.has(n)) ? zip.read(n) : null;
    def.meta = {
        version: meta.version ? String(meta.version) : undefined,
        author: meta.author ? String(meta.author) : undefined,
        icon: image(meta.icon ?? "icon.png"),
        screenshots: strList(meta.screenshots).map(image).filter((b): b is Uint8Array => !!b),
    };
    return def;
}
