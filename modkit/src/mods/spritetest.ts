import { defineMod } from "../api";
import { CORE, cls } from "../uikit";
import { DIR } from "../log";
import { refreshBookTiles } from "../artifacts";

/**
 * Отладка: разведка иконок артефактов (Sprite → Texture2D) и проверка подмены пикселей. Результат — в modkit.log, строки «[spritetest]».
 * Работает на главном потоке Unity (хук A_MainMenuInformation.Update), один раз через ~300 кадров после появления меню.
 */
const db = () => Il2Cpp.domain.assembly("Assembly-CSharp").image.class("Z_Database");

function rectOf(r: Il2Cpp.ValueType): string {
    const g = (a: string, b: string) => { try { return r.field<number>(a).value; } catch (_) { return r.field<number>(b).value; } };
    return `${g("m_XMin", "x")},${g("m_YMin", "y")} ${g("m_Width", "width")}x${g("m_Height", "height")}`;
}

function describe(arr: string, id: number, log: (s: string) => void): void {
    const a = db().field<Il2Cpp.Array<Il2Cpp.Object>>(arr).value;
    if (a.isNull() || id >= a.length) { log(`${arr}[${id}]: нет (длина ${a.isNull() ? "null" : a.length})`); return; }
    const sp = a.get(id);
    if (sp.isNull()) { log(`${arr}[${id}]: null`); return; }
    const tex = sp.method<Il2Cpp.Object>("get_texture").invoke();
    let s = `${arr}[${id}] sprite="${sp.method<Il2Cpp.String>("get_name").invoke().content}" spriteH=${sp.handle}`;
    s += ` rect=${rectOf(sp.method<Il2Cpp.ValueType>("get_rect").invoke())}`;
    try { s += ` packed=${sp.method<boolean>("get_packed").invoke()}`; } catch (e) { s += ` packed=ERR`; }
    try { s += ` ppu=${sp.method<number>("get_pixelsPerUnit").invoke()}`; } catch (e) { /* */ }
    if (tex.isNull()) { log(s + " texture=null"); return; }
    s += ` | tex="${tex.method<Il2Cpp.String>("get_name").invoke().content}" texH=${tex.handle}`;
    s += ` ${tex.method<number>("get_width").invoke()}x${tex.method<number>("get_height").invoke()}`;
    try { s += ` readable=${tex.method<boolean>("get_isReadable").invoke()}`; } catch (e) { s += ` readable=ERR`; }
    try { s += ` format=${tex.method<number>("get_format").invoke()}`; } catch (e) { s += ` format=ERR`; }
    log(s);
}

function readFile(path: string): ArrayBuffer | null {
    try { return (File as any).readAllBytes(path) as ArrayBuffer; } catch (e) { return null; }
}

export default defineMod({
    id: "spritetest",
    name: "Тест иконок (отладка)",
    description: "Пишет в лог данные текстур иконок артефактов и пробует подменить пиксели.",
    defaultEnabled: false,
    options: [{ key: "artifact", label: "Добавить тест-артефакт со своей иконкой", default: 1, min: 0, max: 1, step: 1 }],
    onEnable(ctx) {
        let frames = 0, done = false, bookDone = false, artId = -1;
        const L = (s: string) => ctx.log("[spritetest] " + s);
        ctx.after("A_MainMenuInformation", "Update", 0, self => {
            frames++;
            if (!done && frames >= 300) {
                done = true;
                if (ctx.settings.artifact >= 1) {
                    try {
                        artId = ctx.data.addArtifact({
                            name: "Тест иконки", effects: [[4, 1]], lines: ["Проверка своей иконки"],
                            iconFile: DIR + "/test.png",
                        });
                        L("добавлен артефакт " + artId);
                    } catch (e) { L("ОШИБКА addArtifact: " + e); }
                }
                try { run(ctx, L); } catch (e) { L("ОШИБКА: " + e + " " + (e as any).stack); }
            }
            if (!bookDone && frames >= 600 && artId >= 0) {
                bookDone = true;
                try {
                    self.method("Button_BookArtifact").invoke(); refreshBookTiles(self);   // хуки не срабатывают на вызов из хука — зовём вручную
                          // как нажатие «Артефакты»: хук загрузчика добавит плитку
                } catch (e) { L("ОШИБКА книги: " + e + " " + (e as any).stack); }
            }
            if (bookDone && (frames === 700 || frames === 1000) && artId >= 0) {
                try { checkTile(self, artId, L); } catch (e) { L("ОШИБКА проверки: " + e); }
            }
        });
    },
});

function run(ctx: any, L: (s: string) => void): void {
    L("=== иконки артефактов (Ability) ===");
    for (const id of [41, 42, 60, 100, 150, 200, 222]) describe("SpriteList_AbilityPortrait", id, L);
    L("=== комбинации / синергии ===");
    for (const id of [1, 20, 40]) describe("SpriteList_MagicComPortrait", id, L);
    for (const id of [1, 30, 60]) describe("SpriteList_SynergyPortrait", id, L);

    // --- LoadImage в новую текстуру из файла
    const path = DIR + "/test.png";
    const buf = readFile(path);
    L(`файл ${path}: ${buf ? buf.byteLength + " байт" : "не прочитан (File.readAllBytes)"}`);
    if (buf) {
        const bytes = Il2Cpp.array<number>(Il2Cpp.corlib.class("System.Byte"), buf.byteLength);
        bytes.handle.add(0x20).writeByteArray(buf);
        const tex = cls(CORE, "UnityEngine.Texture2D").alloc();
        tex.method(".ctor", 2).invoke(2, 2);
        const ok = cls("UnityEngine.ImageConversionModule", "UnityEngine.ImageConversion").method<boolean>("LoadImage", 2).invoke(tex, bytes);
        L(`LoadImage -> ${ok}, текстура ${tex.method<number>("get_width").invoke()}x${tex.method<number>("get_height").invoke()} format=${tex.method<number>("get_format").invoke()}`);
        try {
            const c = tex.method<Il2Cpp.ValueType>("GetPixel").invoke(1, 1);
            L(`GetPixel(1,1) = ${c.field<number>("r").value.toFixed(2)},${c.field<number>("g").value.toFixed(2)},${c.field<number>("b").value.toFixed(2)},${c.field<number>("a").value.toFixed(2)}`);
        } catch (e) { L("GetPixel: " + e); }
    }
}

function checkTile(menu: Il2Cpp.Object, id: number, L: (s: string) => void): void {
    const game = (n: string) => Il2Cpp.domain.assembly("Assembly-CSharp").image.class(n);
    const content = menu.field<Il2Cpp.Object>("Obj_FieldBook_ArtifactContentB").value;
    const tf = content.method<Il2Cpp.Object>("get_transform").invoke();
    const type = game("X_Select_ArtifactShow").type.object;
    const n = tf.method<number>("get_childCount").invoke();
    for (let i = 0; i < n; i++) {
        const go = tf.method<Il2Cpp.Object>("GetChild").invoke(i).method<Il2Cpp.Object>("get_gameObject").invoke();
        const comp = go.method<Il2Cpp.Object>("GetComponent", 1).invoke(type);
        if (comp.isNull() || comp.field<number>("Apply_ArtifactID").value !== id) continue;
        const img = comp.field<Il2Cpp.Object>("Img_ArtifactPortrait").value;
        const itf = img.method<Il2Cpp.Object>("get_transform").invoke();
        const c = itf.method<number>("get_childCount").invoke();
        L(`плитка ${id}: Image.alpha=${img.method<Il2Cpp.ValueType>("get_color").invoke().field<number>("a").value}, дочерних=${c}`);
        for (let k = 0; k < c; k++) {
            const cg = itf.method<Il2Cpp.Object>("GetChild").invoke(k).method<Il2Cpp.Object>("get_gameObject").invoke();
            const raw = cg.method<Il2Cpp.Object>("GetComponent", 1).invoke(cls("UnityEngine.UI", "UnityEngine.UI.RawImage").type.object);
            if (raw.isNull()) continue;
            const t = raw.method<Il2Cpp.Object>("get_texture").invoke();
            const r = itf.method<Il2Cpp.Object>("GetChild").invoke(k).method<Il2Cpp.ValueType>("get_rect").invoke();
            L(`  RawImage "${cg.method<Il2Cpp.String>("get_name").invoke().content}" active=${cg.method<boolean>("get_activeInHierarchy").invoke()} tex=${t.isNull() ? "null" : t.method<number>("get_width").invoke() + "x" + t.method<number>("get_height").invoke()} rect=${rectOf(r)}`);
        }
        return;
    }
    L("плитка " + id + " не найдена, дочерних в книге: " + n);
}
