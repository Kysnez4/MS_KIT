import { defineMod } from "../api";
import { APP_DIR, DIR, readText, writeText } from "../log";

const SAVE_DIR = APP_DIR;

/**
 * Отладка: готовит изменённый List_SingleValue (индекс 5 — золото, 6 — алмазы) как modkit/save_dump/List_SingleValue.new.
 * Подменять файл нужно при остановленной игре, иначе она перезапишет его из памяти.
 */
export default defineMod({
    id: "saveedit",
    name: "Правка сохранения (отладка)",
    description: "Готовит List_SingleValue.new с заданными золотом и алмазами.",
    options: [
        { key: "gold", label: "Золото, тыс.", default: 2000, min: 1, max: 100000, step: 500 },
        { key: "gems", label: "Алмазы", default: 2000, min: 0, max: 100000, step: 1000 },
    ],
    onEnable(ctx) {
        let done = false;
        let calls = 0;
        ctx.after("A_MainMenuInformation", "Update", 0, () => {
            if (done || ++calls % 60 !== 0) return;
            done = true;
            const aes = Il2Cpp.domain.assembly("Assembly-CSharp").image.class("Z_AESCrypto");
            const raw = readText(`${SAVE_DIR}/List_SingleValue.json`);
            if (raw === null) { ctx.log("нет List_SingleValue.json"); return; }
            const plain = aes.method<Il2Cpp.String>("AESDecrypt128", 1).invoke(Il2Cpp.string(raw.trim())).content!;
            const arr: string[] = JSON.parse(plain);
            arr[5] = String(ctx.settings.gold * 1000);
            arr[6] = String(ctx.settings.gems);
            const enc = aes.method<Il2Cpp.String>("AESEncrypt128", 1).invoke(Il2Cpp.string(JSON.stringify(arr))).content!;
            writeText(`${DIR}/save_dump/List_SingleValue.new`, enc);
            ctx.log(`[saveedit] было ${plain} -> стало ${JSON.stringify(arr)}`);
        });
    },
});
