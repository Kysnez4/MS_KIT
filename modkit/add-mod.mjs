// Добавляет мод на телефон (в Download/ModKit), дальше в игре кнопка «+» в окне «Моды»:
//   npm run add-mod -- мод.zip         готовый архив
//   npm run add-mod -- папка_мода      упакует папку (mod.json + main.js + картинки) и зальёт
//   npm run add-mod -- мод.ts          встроенный TypeScript-мод: копирует в src/mods, пересобирает loader.js и заливает
import { execFileSync } from "node:child_process";
import { copyFileSync, existsSync, statSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { basename, dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { packDir } from "./pack-mod.mjs";

const src = process.argv[2];
if (!src || !existsSync(src)) {
    console.error("Использование: npm run add-mod -- мод.zip | папка_мода | мод.ts");
    process.exit(1);
}
const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const adb = join(process.env.LOCALAPPDATA ?? "", "Android", "Sdk", "platform-tools", "adb.exe");
const serial = "08990372A3000411";

if (src.endsWith(".ts")) {
    const dest = join(root, "modkit", "src", "mods", basename(src));
    if (resolve(src) !== resolve(dest)) copyFileSync(src, dest);
    console.log("мод добавлен:", dest);
    execFileSync(process.execPath, [join(root, "modkit", "build.mjs")], { cwd: root, stdio: "inherit" });
    execFileSync("powershell", ["-File", join(root, "modkit", "push.ps1"), join(root, "modkit", "out", "loader.js")], { cwd: root, stdio: "inherit" });
} else {
    let zipPath = src;
    if (statSync(src).isDirectory()) {
        const { zip, id } = packDir(src);
        zipPath = join(tmpdir(), id + ".zip");
        writeFileSync(zipPath, zip);
    }
    const name = basename(zipPath);
    execFileSync(adb, ["-s", serial, "shell", "mkdir -p /sdcard/Download/ModKit"], { stdio: "inherit" });
    execFileSync(adb, ["-s", serial, "push", zipPath, "/sdcard/Download/ModKit/" + name], { stdio: "inherit" });
    console.log(`Залито: Download/ModKit/${name}. В игре откройте «Моды» и нажмите «+».`);
}
