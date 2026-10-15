import { defineMod } from "../api";
import { APP_DIR, DIR, readText, writeText } from "../log";

const SAVE_DIR = APP_DIR;
const NAMES = ("Array_AbilityClassPractice Array_AreaBestScoreNum Array_ExperimentON Array_Int_PlusSaveGame Array_JournalProgress " +
    "Array_ResearchCore_Level Array_RestoreValue List_AbilityClassObtain List_BookMark_MagicCom List_Book_Ultimate " +
    "List_FirstValue List_GlobalValue List_SingleValue").split(" ");

/** Отладка: расшифровывает файлы сохранения игры (Z_AESCrypto.AESDecrypt128) в modkit/save_dump/*.txt. Ничего не меняет. */
export default defineMod({
    id: "savedump",
    name: "Дамп сохранения (отладка)",
    description: "Расшифровывает файлы сохранения в modkit/save_dump/.",
    options: [],
    onEnable(ctx) {
        let done = false;
        let calls = 0;
        ctx.after("A_MainMenuInformation", "Update", 0, () => {
            if (done || ++calls % 60 !== 0) return;
            done = true;
            const aes = Il2Cpp.domain.assembly("Assembly-CSharp").image.class("Z_AESCrypto");
            ctx.log(`AES PASSWORD=${aes.field<Il2Cpp.String>("PASSWORD").value.content} KEY=${aes.field<Il2Cpp.String>("KEY").value.content}`);
            const dec = aes.method<Il2Cpp.String>("AESDecrypt128", 1);
            for (const n of NAMES) {
                const raw = readText(`${SAVE_DIR}/${n}.json`);
                if (raw === null) { ctx.log(`${n}: нет файла`); continue; }
                let out: string;
                try {
                    const r = dec.invoke(Il2Cpp.string(raw.trim()));
                    out = r.isNull() ? "<null>" : r.content ?? "<null>";
                } catch (e) {
                    out = "<ошибка: " + e + ">";
                }
                writeText(`${DIR}/save_dump/${n}.txt`, out);
            }
        });
    },
});
