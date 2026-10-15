// Dev-инструмент: выгружает игровые таблицы Z_Database.Dictionary_* в JSON.
//   npx frida-compile modkit/src/tools/dump_tables.ts -o modkit/out/dump_tables.js -B iife -S
//   .\modkit\push.ps1 modkit\out\dump_tables.js   (потом забрать tables/ с телефона)
import "frida-il2cpp-bridge";
import { Table, tableNames } from "../data";
import { DIR as LOGDIR, chmod644, log } from "../log";

const DIR = LOGDIR + "/tables";

Il2Cpp.perform(() => {
    const libc = Process.getModuleByName("libc.so");
    new NativeFunction(libc.getExportByName("mkdir"), "int", ["pointer", "int"])(Memory.allocUtf8String(DIR), 0o777);
    new NativeFunction(libc.getExportByName("chmod"), "int", ["pointer", "int"])(Memory.allocUtf8String(DIR), 0o755);

    const done = new Set<string>();
    const timer = setInterval(() => Il2Cpp.perform(() => {
        try {
            const names = tableNames();
            for (const n of names) {
                if (done.has(n)) continue;
                try {
                    const t = new Table(n);
                    if (t.length === 0) continue; // игра ещё не наполнила таблицу
                    const rows = t.all();
                    const path = `${DIR}/${n}.json`;
                    const f = new File(path, "w");
                    f.write(JSON.stringify(rows, null, 1));
                    f.close();
                    chmod644(path);
                    done.add(n);
                    log(`таблица ${n}: ${rows.length} строк`);
                } catch (e) {
                    log(`таблица ${n}: ${e}`);
                }
            }
            if (names.length && done.size === names.length) {
                clearInterval(timer);
                log("все таблицы выгружены: " + names.join(", "));
            }
        } catch (e) {
            log("ошибка дампа: " + e);
        }
    }), 4000);
});
