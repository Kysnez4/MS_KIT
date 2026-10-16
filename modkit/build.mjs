// Собирает modkit/out/loader.js: сканирует src/mods/*.ts, генерирует src/mods.gen.ts и вызывает frida-compile.
//   node modkit/build.mjs
import { execFileSync } from "node:child_process";
import { readdirSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const modsDir = join(root, "modkit", "src", "mods");
const allFiles = readdirSync(modsDir).filter(f => f.endsWith(".ts") && !f.startsWith("_")).sort();

// --only id1,id2  — собрать только эти моды (имена файлов без .ts); --out путь — куда писать (по умолчанию modkit/out/loader.js)
const args = process.argv.slice(2);
const take = name => { const i = args.indexOf(name); return i < 0 ? null : args.splice(i, 2)[1]; };
const only = take("--only")?.split(",").filter(Boolean);
const outFile = take("--out") ?? "modkit/out/loader.js";
const files = only ? allFiles.filter(f => only.includes(f.replace(/\.ts$/, ""))) : allFiles;
if (only && files.length !== only.length) throw new Error(`не найдены моды: ${only.filter(o => !allFiles.includes(o + ".ts"))}`);

const genFile = join(root, "modkit", "src", "mods.gen.ts");
function writeGen(list) {
    const imports = list.map((f, i) => `import m${i} from "./mods/${f.replace(/\.ts$/, "")}";`).join("\n");
    const names = list.map((_, i) => `m${i}`).join(", ");
    writeFileSync(genFile,
        "// Файл генерируется build.mjs, руками не править.\n" +
        'import type { ModDef } from "./api";\n' +
        `${imports}\n` +
        `const all: ModDef[] = [${names}];\n` +
        "export default all;\n");
}
writeGen(files);

const cli = join(root, "node_modules", "frida-compile", "dist", "cli.js");
try {
    execFileSync(process.execPath, [cli, "modkit/src/main.ts", "-o", outFile, "-B", "iife", "-S", ...args],
        { cwd: root, stdio: "inherit" });
} finally {
    if (only) writeGen(allFiles);   // оставляем в репозитории полный список
}
console.log(`OK: ${files.length} мод(ов) → ${outFile}`);
