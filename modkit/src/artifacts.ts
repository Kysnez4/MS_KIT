import { Table, ensureSprite, intList } from "./data";
import { CORE, UI, add, cls, color, node, v2 } from "./uikit";
import { log } from "./log";

/**
 * Добавление новых артефактов. Что для этого нужно (всё делает addArtifact):
 *  1. строка в таблице Ability (копия образца) + иконка в SpriteList_AbilityPortrait;
 *  2. запись в пулы выпадения List_ArtifactPossibleGrade_* (вес = число копий; у обычных артефактов их 5);
 *  3. запись в Z_Database.Dictionary_ApplySynergy (список по одному словарю на артефакт, индекс = ID − 41) —
 *     без неё окно описания в бою падает с ArgumentOutOfRange;
 *  4. плитка в энциклопедии (плитки заранее созданы по числу существующих артефактов, поэтому клонируем) и своё окно описания.
 */

export interface ArtifactDef {
    /** Название (показывается в игре). */
    name: string;
    /** Строки описания: каждая выводится зелёным в окне артефакта. Не больше 6. Если не заданы — строка «Эффект ID: значение» на каждый эффект. */
    lines?: string[];
    /** Редкость: C обычное, B редкое, A героическое, P особое, S легендарное. По умолчанию — как у образца. */
    rarity?: "C" | "B" | "A" | "P" | "S";
    /** ID существующего артефакта, чью иконку взять (по умолчанию иконка образца `cloneFrom`). */
    icon?: number;
    /**
     * Путь к PNG на телефоне со СВОЕЙ иконкой (например `/sdcard/Android/data/com.vkslrzm.Zombie/files/modkit/icon.png` или `/sdcard/Download/ModKit/icon.png`).
     * Рекомендуемый размер 248x288 (пропорции иконок артефактов), с прозрачностью. Показывается только в энциклопедии (плитка и окно описания);
     * в бою (окно выбора, список полученных) остаётся иконка образца `cloneFrom`/`icon` — см. docs/README.md, «Свои иконки».
     */
    iconFile?: string;
    /** Эффекты: пары [ID эффекта, значение], не больше 9. Список эффектов — docs/EFFECTS.md. */
    effects: Array<[number, number]>;
    /** ID существующего артефакта-образца: от него берутся иконка, редкость и прочие колонки. По умолчанию 42 («Алмаз», класс A). */
    cloneFrom?: number;
    /** Вес выпадения: число копий в пуле своей редкости. По умолчанию 5 (как у обычных). */
    weight?: number;
    /** Тест: положить во все пулы с очень большим весом, чтобы артефакт выпадал почти всегда. */
    guaranteed?: boolean;
}

interface Entry {
    id: number;
    def: ArtifactDef & { lines: string[] };
    grade: number;          // 1=C 2=B 3=A 4=P 5=S
    cloneFrom: number;
    synergyAssigned: boolean;
}

const FIRST_ARTIFACT_ID = 41;      // ID существующих артефактов идут подряд 41..222
const GRADES = ["C", "B", "A", "P", "S"];
const registry: Entry[] = [];

// ---------------------------------------------------------------- свои иконки (PNG -> Texture2D -> RawImage поверх Image)
// Спрайт иконки — область общего сжатого (ETC2, isReadable=false) атласа 2048x2048: писать в него пиксели нельзя, Sprite.Create вырезан.
// Поэтому текстура создаётся отдельно (Texture2D + ImageConversion.LoadImage), а поверх Image кладётся дочерний RawImage.

const iconCache = new Map<string, Il2Cpp.Object | null>();

/** Загружает PNG в новую Texture2D (главный поток Unity!). null — файла нет или это не PNG/JPG. Результат кэшируется. */
function loadIcon(path: string): Il2Cpp.Object | null {
    const cached = iconCache.get(path);
    if (cached) { try { cached.method<number>("get_width").invoke(); return cached; } catch (_) { /* текстуру уничтожили — грузим заново */ } }
    else if (cached === null) return null;
    let buf: ArrayBuffer;
    try { buf = (File as any).readAllBytes(path) as ArrayBuffer; } catch (e) { log(`иконка ${path}: не прочитан (${e})`); iconCache.set(path, null); return null; }
    const bytes = Il2Cpp.array<number>(Il2Cpp.corlib.class("System.Byte"), buf.byteLength);
    bytes.handle.add(0x20).writeByteArray(buf);
    const tex = cls(CORE, "UnityEngine.Texture2D").alloc();
    tex.method(".ctor", 2).invoke(2, 2);
    const ok = cls("UnityEngine.ImageConversionModule", "UnityEngine.ImageConversion").method<boolean>("LoadImage", 2).invoke(tex, bytes);
    if (!ok) { log(`иконка ${path}: LoadImage не принял файл`); iconCache.set(path, null); return null; }
    log(`иконка ${path}: ${tex.method<number>("get_width").invoke()}x${tex.method<number>("get_height").invoke()}`);
    iconCache.set(path, tex);
    return tex;
}

const ICON_NODE = "ModKitIcon";

/** Кладёт поверх UI.Image дочерний RawImage с текстурой (tex=null — убирает его) и прячет/возвращает сам спрайт. */
function overlayIcon(img: Il2Cpp.Object, tex: Il2Cpp.Object | null, origColor: { v: Il2Cpp.ValueType | null }): void {
    const tf = img.method<Il2Cpp.Object>("get_transform").invoke();
    let raw: Il2Cpp.Object | null = null;
    const n = tf.method<number>("get_childCount").invoke();
    for (let i = 0; i < n; i++) {
        const go = tf.method<Il2Cpp.Object>("GetChild", 1).invoke(i).method<Il2Cpp.Object>("get_gameObject").invoke();
        if (go.method<Il2Cpp.String>("get_name").invoke().content === ICON_NODE) { raw = go.method<Il2Cpp.Object>("GetComponent", 1).invoke(cls(UI, "UnityEngine.UI.RawImage").type.object); break; }
    }
    if (!origColor.v) origColor.v = img.method<Il2Cpp.ValueType>("get_color").invoke();
    if (!tex) {
        if (raw) raw.method<Il2Cpp.Object>("get_gameObject").invoke().method("SetActive").invoke(false);
        img.method("set_color").invoke(origColor.v);
        return;
    }
    if (!raw) {
        const nd = node(ICON_NODE, tf);
        nd.rt.method("set_anchorMin").invoke(v2(0, 0));
        nd.rt.method("set_anchorMax").invoke(v2(1, 1));
        nd.rt.method("set_pivot").invoke(v2(0.5, 0.5));
        nd.rt.method("set_anchoredPosition").invoke(v2(0, 0));
        nd.rt.method("set_sizeDelta").invoke(v2(0, 0));
        raw = add(nd.go, UI, "UnityEngine.UI.RawImage");
        raw.method("set_raycastTarget").invoke(false);
    }
    raw.method("set_texture").invoke(tex);
    raw.method<Il2Cpp.Object>("get_gameObject").invoke().method("SetActive").invoke(true);
    img.method("set_color").invoke(color(0, 0, 0, 0));       // сам спрайт образца делаем невидимым (Image остаётся — на него опираются игровые скрипты)
}
const detailColor: { v: Il2Cpp.ValueType | null } = { v: null };
let detailShownFor = -1;        // listIndex артефакта со своей иконкой, показанного сейчас в окне описания (-1 — нет)

const db = () => Il2Cpp.domain.assembly("Assembly-CSharp").image.class("Z_Database");
const game = (name: string) => Il2Cpp.domain.assembly("Assembly-CSharp").image.class(name);

function gradeOf(id: number): number {
    for (let g = 0; g < GRADES.length; g++) {
        const l = intList("List_ArtifactPossibleGrade_" + GRADES[g]);
        if (l.method<number>("IndexOf").invoke(id) >= 0) return g + 1;
    }
    return 3;
}

/** Добавляет артефакт. Вызывать из `ctx.data.whenReady`. Возвращает ID. Откат возвращает onCleanup-функции. */
/** Добавляет сразу несколько артефактов: массив иконок растёт один раз, а не на каждый артефакт. Возвращает ID по порядку. */
export function addArtifacts(defs: ArtifactDef[], cleanups: Array<() => void>, owner: string): number[] {
    if (!defs.length) return [];
    const first = defs[0].icon ?? defs[0].cloneFrom ?? 42;
    ensureSprite("SpriteList_AbilityPortrait", new Table("Ability").maxId() + defs.length, first);
    return defs.map(d => addArtifact(d, cleanups, owner));
}

/** Запись артефакта, добавленного модами (для обновления описания в энциклопедии). */
export function findAddedArtifact(id: number): { def: { name: string; lines: string[] } } | undefined {
    return registry.find(e => e.id === id);
}

export function addArtifact(def: ArtifactDef, cleanups: Array<() => void>, owner: string): number {
    const cloneFrom = def.cloneFrom ?? 42;
    const t = new Table("Ability");
    const src = t.indexOf(cloneFrom);
    if (src < 0) throw new Error(`Ability: нет артефакта-образца ${cloneFrom}`);
    const grade = def.rarity ? GRADES.indexOf(def.rarity) + 1 : gradeOf(cloneFrom);
    const lines = def.lines ?? def.effects.map(([e, v]) => `Эффект ${e}: ${v}`);

    const row = t.cloneRow(src);
    const id = t.maxId() + 1;
    t.set(row, t.idColumn, id);
    const was = ensureSprite("SpriteList_AbilityPortrait", id, def.icon ?? cloneFrom);
    t.set(row, "이름", def.name);
    const pad = (n: number) => String(n).padStart(2, "0");
    const cols = new Set(t.columns);
    for (let n = 1; n <= 9; n++) {
        const e = def.effects[n - 1];
        if (cols.has(`효과ID_${pad(n)}`)) t.set(row, `효과ID_${pad(n)}`, e ? e[0] : 1);
        if (cols.has(`효과ID_${pad(n)}값`)) t.set(row, `효과ID_${pad(n)}값`, e ? e[1] : 0);
    }
    for (let n = 1; n <= 6; n++) if (cols.has(`효과 설명줄${pad(n)}`)) t.set(row, `효과 설명줄${pad(n)}`, lines[n - 1] ?? "`");
    if (cols.has("효과 설명줄 개수")) t.set(row, "효과 설명줄 개수", lines.length);
    if (def.iconFile) log(`[${owner}] артефакт «${def.name}»: своя иконка ${def.iconFile} (только энциклопедия)`);
    log(`[${owner}] артефакт «${def.name}»: ID ${id}, образец ${cloneFrom}, редкость ${GRADES[grade - 1]}, иконки было ${was}`);

    // пулы выпадения
    const pools = def.guaranteed ? GRADES : [GRADES[grade - 1]];
    const copies = def.guaranteed ? 400 : (def.weight ?? 5);
    const added: Array<[Il2Cpp.Object, number]> = [];
    for (const g of pools) {
        const l = intList("List_ArtifactPossibleGrade_" + g);
        for (let i = 0; i < copies; i++) { l.method("Add").invoke(id); added.push([l, id]); }
    }

    const entry: Entry = { id, def: { ...def, lines }, grade, cloneFrom, synergyAssigned: false };
    registry.push(entry);
    try { ensureSynergySlot(entry); } catch (e) { log(`[${owner}] ApplySynergy: ${e}`); }

    cleanups.push(() => Il2Cpp.perform(() => {
        for (const [l, v] of added) l.method("Remove").invoke(v);
        const k = registry.indexOf(entry);
        if (k >= 0) registry.splice(k, 1);
        const ti = t.indexOf(id);
        if (ti === t.length - 1) t.removeLast();
    }));
    return id;
}

// ---------------------------------------------------------------- Dictionary_ApplySynergy

function cloneDict(src: Il2Cpp.Object): Il2Cpp.Object {
    const copy = src.class.new();
    const entries = src.field<Il2Cpp.Array<any>>("_entries").value.handle.add(0x20);
    const used = src.field<number>("_count").value;
    for (let i = 0; i < used; i++) {
        const e = entries.add(i * 16);                              // Entry<int,int>: hashCode, next, key, value
        if (e.add(4).readS32() < -1) continue;
        copy.method("set_Item").invoke(e.add(8).readS32(), e.add(12).readS32());
    }
    return copy;
}

function ensureSynergySlot(e: Entry): void {
    const list = db().field<Il2Cpp.Object>("Dictionary_ApplySynergy").value;
    if (list.isNull()) return;
    const need = e.id - FIRST_ARTIFACT_ID;
    let count = list.method<number>("get_Count").invoke();
    if (count > need && e.synergyAssigned) return;
    const sampleIdx = e.cloneFrom - FIRST_ARTIFACT_ID;
    // список наполняется игрой позже запуска мода; обращение за границу даёт управляемое исключение IL2CPP -> abort, после которого игра зависает
    if (count <= sampleIdx) return;
    const sample = list.method<Il2Cpp.Object>("get_Item").invoke(sampleIdx);
    if (count <= need) e.synergyAssigned = false;                    // игра пересобрала список — заполняем заново
    while (count <= need) { list.method("Add").invoke(cloneDict(sample)); count++; }
    list.method("set_Item").invoke(need, cloneDict(sample));
    e.synergyAssigned = true;
    log(`Dictionary_ApplySynergy: запись для ID ${e.id} (в списке ${count})`);
}

// ---------------------------------------------------------------- энциклопедия

/** Окно описания артефакта (V_ArtifactShow): заполняем поля вручную — штатный путь не знает про чужой ID. */
function showDetail(refTile: Il2Cpp.Object, e: Entry, listIndex: number): void {
    const vt = game("V_ArtifactShow");
    const vs = cls(CORE, "UnityEngine.Object").method<Il2Cpp.Array<Il2Cpp.Object>>("FindObjectsOfType", 1).invoke(vt.type.object).get(0);
    vt.field<number>("ArtifactShowIndex").value = listIndex;
    const sprites = db().field<Il2Cpp.Array<Il2Cpp.Object>>("SpriteList_AbilityPortrait").value;
    const portrait = vs.field<Il2Cpp.Object>("Img_ArtifactDetail_Portrait").value;
    portrait.method("set_sprite").invoke(sprites.get(e.id));
    const tex = e.def.iconFile ? loadIcon(e.def.iconFile) : null;
    overlayIcon(portrait, tex, detailColor);
    detailShownFor = tex ? listIndex : -1;
    const g = vs.field<Il2Cpp.Object>("Img_ArtifactDetail_SheetGround").value;
    const rg = refTile.field<Il2Cpp.Object>("Img_ArtifactSheetGround").value;
    g.method("set_sprite").invoke(rg.method<Il2Cpp.Object>("get_sprite").invoke());
    g.method("set_color").invoke(rg.method<Il2Cpp.ValueType>("get_color").invoke());
    vs.field<Il2Cpp.Object>("Text_ArtifactDetail_Name").value.method("set_text").invoke(Il2Cpp.string(e.def.name));
    vs.field<Il2Cpp.Object>("Text_ArtifactDetail_Grade").value.method("set_text").invoke(Il2Cpp.string("Артефакт ModKit"));
    const text = e.def.lines.map(l => `<color=#6EFF3C>${l}</color>`).join("\n");
    vs.field<Il2Cpp.Object>("Text_ArtifactDetail_Effect").value.method("set_text").invoke(Il2Cpp.string(text));
    vs.field<Il2Cpp.Object>("Obj_FieldArtifactDetail").value.method("SetActive").invoke(true);
}

/** Плитки в книге расставлены координатами, а не сеткой: клон ставим в следующую ячейку после последней. */
function addBookTile(menu: Il2Cpp.Object, e: Entry): void {
    const content = menu.field<Il2Cpp.Object>("Obj_FieldBook_ArtifactContentB").value;
    const tf = content.method<Il2Cpp.Object>("get_transform").invoke();
    const n = tf.method<number>("get_childCount").invoke();
    const type = game("X_Select_ArtifactShow").type.object;

    // окно описания берёт артефакт из списка V_ArtifactShow.Apply_AllyArtifactLearn по ArtifactIndex плитки
    const shows = cls(CORE, "UnityEngine.Object").method<Il2Cpp.Array<Il2Cpp.Object>>("FindObjectsOfType", 1).invoke(game("V_ArtifactShow").type.object);
    const list = shows.length ? shows.get(0).field<Il2Cpp.Object>("Apply_AllyArtifactLearn").value : null;
    let listIndex = -1;
    if (list && !list.isNull()) {
        listIndex = list.method<number>("IndexOf").invoke(e.id);
        if (listIndex < 0) { list.method("Add").invoke(e.id); listIndex = list.method<number>("get_Count").invoke() - 1; }
    }

    let last: Il2Cpp.Object | null = null;
    let ref: Il2Cpp.Object | null = null;            // плитка той же редкости — берём цвета рамки и фона
    const tiles: Il2Cpp.Object[] = [];
    for (let i = 0; i < n; i++) {
        const go = tf.method<Il2Cpp.Object>("GetChild").invoke(i).method<Il2Cpp.Object>("get_gameObject").invoke();
        const comp = go.method<Il2Cpp.Object>("GetComponent", 1).invoke(type);
        if (comp.isNull()) continue;
        if (comp.field<number>("Apply_ArtifactID").value === e.id) {
            if (listIndex >= 0) comp.field<number>("ArtifactIndex").value = listIndex;      // плитка уже есть
            return;
        }
        if (!ref && comp.field<number>("Apply_ArtifactGrade").value === e.grade) ref = comp;
        last = go;
        tiles.push(go);
    }
    if (!last || !ref) { log("книга артефактов: нет плитки-образца"); return; }

    const clone = cls(CORE, "UnityEngine.Object").method<Il2Cpp.Object>("Instantiate", 1).invoke(last);
    clone.method<Il2Cpp.Object>("get_transform").invoke().method("SetParent", 2).invoke(tf, false);
    const comp = clone.method<Il2Cpp.Object>("GetComponent", 1).invoke(type);
    comp.field<number>("Apply_ArtifactID").value = e.id;
    comp.field<number>("Apply_ArtifactGrade").value = e.grade;
    if (listIndex >= 0) comp.field<number>("ArtifactIndex").value = listIndex;
    // Start_ArtifactShow не зовём: он выбирает ID по номеру плитки среди существующих и для нового даёт 0. Вид задаём вручную.
    const sprites = db().field<Il2Cpp.Array<Il2Cpp.Object>>("SpriteList_AbilityPortrait").value;
    const tileImg = comp.field<Il2Cpp.Object>("Img_ArtifactPortrait").value;
    tileImg.method("set_sprite").invoke(sprites.get(e.id));
    if (e.def.iconFile) { const tex = loadIcon(e.def.iconFile); if (tex) overlayIcon(tileImg, tex, { v: null }); }
    for (const f of ["Img_ArtifactSheetContour", "Img_ArtifactSheetGround"]) {
        const from = ref.field<Il2Cpp.Object>(f).value, to = comp.field<Il2Cpp.Object>(f).value;
        to.method("set_sprite").invoke(from.method<Il2Cpp.Object>("get_sprite").invoke());
        to.method("set_color").invoke(from.method<Il2Cpp.ValueType>("get_color").invoke());
    }
    const pos = (go: Il2Cpp.Object): [number, number] => {
        const p = go.method<Il2Cpp.Object>("get_transform").invoke().method<Il2Cpp.ValueType>("get_anchoredPosition").invoke();
        return [p.field<number>("x").value, p.field<number>("y").value];
    };
    const [x0, y0] = pos(tiles[0]), [x1] = pos(tiles[1]);
    const colStep = x1 - x0;
    const cols = tiles.findIndex(t => Math.abs(pos(t)[1] - y0) > 1);       // плиток в первой строке
    const rowStep = cols > 0 ? pos(tiles[cols])[1] - y0 : 0;
    const [lx, ly] = pos(last);
    let nx = lx + colStep, ny = ly;
    if (cols > 0 && nx > x0 + colStep * (cols - 0.5)) {                     // строка заполнена — следующая строка
        nx = x0; ny = ly + rowStep;
    }
    clone.method<Il2Cpp.Object>("get_transform").invoke().method("set_anchoredPosition").invoke(v2(nx, ny));
    clone.method("SetActive").invoke(true);

    // штатная кнопка плитки для нового ID бросает исключение — заменяем своей (Selectable не терпит двух Button, поэтому DestroyImmediate)
    const btnType = cls("UnityEngine.UI", "UnityEngine.UI.Button").type.object;
    const oldBtn = clone.method<Il2Cpp.Object>("GetComponent", 1).invoke(btnType);
    if (!oldBtn.isNull()) cls(CORE, "UnityEngine.Object").method("DestroyImmediate", 1).invoke(oldBtn);
    const btn = clone.method<Il2Cpp.Object>("AddComponent").invoke(btnType);
    btn.method("set_targetGraphic").invoke(comp.field<Il2Cpp.Object>("Img_ArtifactSheetGround").value);
    const refTile = ref;
    const act = Il2Cpp.delegate(cls(CORE, "UnityEngine.Events.UnityAction"), () => {
        try { showDetail(refTile, e, listIndex); } catch (err) { log("описание артефакта: " + err); }
    });
    btn.method<Il2Cpp.Object>("get_onClick").invoke().method("AddListener").invoke(act);
    log(`книга артефактов: плитка для «${e.def.name}» (ID ${e.id})`);
}


/**
 * Высота списка книги. Игра задаёт её при каждом открытии под свои артефакты (sizeDelta.y), а наши плитки идут сверх,
 * поэтому каждый раз добавляем высоту ровно на те строки, которые заняли наши плитки: строк_всего - строк_игры.
 * Новая строка появляется каждые `cols` плиток (кратность считается сама). Повторный вызов в том же открытии ничего не добавляет:
 * запоминаем, какой sizeDelta.y получили, и если он не менялся, игра не пересчитывала высоту.
 */

let lastFit: { handle: string; y: number } | null = null;
let baseTilesY: number | null = null;       // положение Field_ContentB до нашего сдвига

function fitBookHeight(menu: Il2Cpp.Object): void {
    const content = menu.field<Il2Cpp.Object>("Obj_FieldBook_ArtifactContentB").value;
    const tf = content.method<Il2Cpp.Object>("get_transform").invoke();
    const type = game("X_Select_ArtifactShow").type.object;
    const ours = new Set(registry.map(e => e.id));
    const ys: number[] = [];
    let total = 0, own = 0;
    for (let i = 0; i < tf.method<number>("get_childCount").invoke(); i++) {
        const go = tf.method<Il2Cpp.Object>("GetChild").invoke(i).method<Il2Cpp.Object>("get_gameObject").invoke();
        const comp = go.method<Il2Cpp.Object>("GetComponent", 1).invoke(type);
        if (comp.isNull()) continue;
        if (ys.length < 2 || total < 200) ys.push(go.method<Il2Cpp.Object>("get_transform").invoke().method<Il2Cpp.ValueType>("get_anchoredPosition").invoke().field<number>("y").value);
        total++;
        if (ours.has(comp.field<number>("Apply_ArtifactID").value)) own++;
    }
    if (!own) return;
    const y0 = ys[0];
    const cols = ys.findIndex(y => Math.abs(y - y0) > 1);
    if (cols <= 0) return;
    const rowStep = Math.abs(ys[cols] - y0);
    const rows = (n: number) => Math.ceil(n / cols);
    const extra = rows(total) - rows(total - own);
    if (extra <= 0) return;
    // прокручивается не Field_ContentB (точка 100x100, в ней лежат плитки), а его родитель Field_ContentA: игра задаёт ему sizeDelta.y под свои артефакты
    const holder = tf.method<Il2Cpp.Object>("get_parent").invoke();
    const size = holder.method<Il2Cpp.ValueType>("get_sizeDelta").invoke();
    const cur = size.field<number>("y").value;
    const handle = holder.handle.toString();
    if (lastFit && lastFit.handle === handle && Math.abs(lastFit.y - cur) < 0.5) return;     // уже добавили в этом открытии
    const next = cur + extra * rowStep;
    holder.method("set_sizeDelta").invoke(v2(size.field<number>("x").value, next));
    lastFit = { handle, y: next };
    // якоря Field_ContentA по центру: рост высоты расширяет список и вверх, и вниз. Сдвигаем плитки вверх на половину прироста,
    // чтобы верхний ряд остался у верхнего края, а весь прирост ушёл вниз.
    const pos = tf.method<Il2Cpp.ValueType>("get_anchoredPosition").invoke();
    if (baseTilesY === null) baseTilesY = pos.field<number>("y").value;
    tf.method("set_anchoredPosition").invoke(v2(pos.field<number>("x").value, baseTilesY + (extra * rowStep) / 2));
    log(`книга артефактов: плиток ${total} (наших ${own}), колонок ${cols}, +${extra} стр. по ${Math.round(rowStep)}: высота ${Math.round(cur)} -> ${Math.round(next)}`);
}

// ---------------------------------------------------------------- хуки (ставятся один раз при старте загрузчика)

let hooked = false;

/** Добавляет плитки всех зарегистрированных артефактов в книгу (то же, что хук кнопки «Артефакты»); для отладочных модов. */
export function refreshBookTiles(menu: Il2Cpp.Object): void { for (const e of registry) addBookTile(menu, e); }

/** Периодические задачи других модулей (раз в ~2 с на главном экране и в бою), например вычистка запрещённых артефактов из пулов. */
export const tickHooks: Array<() => void> = [];
const runTicks = () => { for (const f of tickHooks) { try { f(); } catch (e) { log("периодическая задача: " + e); } } };

/** Своя иконка в окне описания — общий Image игры: как только открыт другой артефакт, убираем наш RawImage. */
function watchDetail(): void {
    const shows = cls(CORE, "UnityEngine.Object").method<Il2Cpp.Array<Il2Cpp.Object>>("FindObjectsOfType", 1).invoke(game("V_ArtifactShow").type.object);
    if (!shows.length) { detailShownFor = -1; return; }
    const vs = shows.get(0);
    const open = vs.field<Il2Cpp.Object>("Obj_FieldArtifactDetail").value.method<boolean>("get_activeSelf").invoke();
    if (open && vs.field<number>("ArtifactShowIndex").value === detailShownFor) return;
    overlayIcon(vs.field<Il2Cpp.Object>("Img_ArtifactDetail_Portrait").value, null, detailColor);
    detailShownFor = -1;
}

export function installArtifactHooks(): void {
    if (hooked) return;
    hooked = true;
    const attach = (className: string, method: string, fn: (self: Il2Cpp.Object) => void) => {
        const m = game(className).method(method, 0);
        Interceptor.attach(m.virtualAddress, {
            onEnter(args) { (this as any).self = args[0]; },
            onLeave() {
                if (!registry.length) return;
                try { fn(new Il2Cpp.Object((this as any).self)); } catch (e) { log(`ошибка артефактов в ${className}.${method}: ${e}`); }
            },
        });
    };
    attach("A_MainMenuInformation", "Button_BookArtifact", menu => { for (const e of registry) addBookTile(menu, e); fitBookHeight(menu); });
    const tickOn = (className: string) => {
        let tick = 0;
        Interceptor.attach(game(className).method("Update", 0).virtualAddress, {
            onEnter() {
                if (detailShownFor >= 0) { try { watchDetail(); } catch (e) { detailShownFor = -1; log("иконка в окне описания: " + e); } }
                if (++tick % 120 !== 0) return;
                try { for (const e of registry) ensureSynergySlot(e); } catch (_) { /* список ещё не создан */ }    // игра может пересобрать список при старте боя
                runTicks();
            },
        });
    };
    tickOn("C_Battle_Manager");
    tickOn("A_MainMenuInformation");
}
