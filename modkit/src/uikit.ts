import { log } from "./log";

/** Общие инструменты для построения Unity uGUI из JS (через IL2CPP). Используются окном «Моды» и модами с собственными экранами. */

export const CORE = "UnityEngine.CoreModule";
export const UI = "UnityEngine.UI";
export const UIMODULE = "UnityEngine.UIModule";

export const cls = (asm: string, name: string) => Il2Cpp.domain.assembly(asm).image.class(name);

/** Ширина «логического» экрана; всё остальное масштабируется по ней. */
export const LOGICAL_W = 720;

/** Состояние, которое выставляется при построении меню (см. ui.ts). */
export const kit: { k: number; font: Il2Cpp.Object | null; screenH: number; canvasTf: Il2Cpp.Object | null } =
    { k: 1, font: null, screenH: 1280, canvasTf: null };

export function str(s: string): Il2Cpp.String {
    return Il2Cpp.string(s);
}

export function color(r: number, g: number, b: number, a: number): Il2Cpp.ValueType {
    const o = cls(CORE, "UnityEngine.Color").alloc();
    o.method(".ctor", 4).invoke(r, g, b, a);
    return o.unbox();
}

export function typeOf(asm: string, name: string): Il2Cpp.Object {
    return cls(asm, name).type.object;
}

/** Новый GameObject с RectTransform под parent. */
export function node(name: string, parent: Il2Cpp.Object | null): { go: Il2Cpp.Object; rt: Il2Cpp.Object } {
    const go = cls(CORE, "UnityEngine.GameObject").alloc();
    go.method(".ctor", 1).invoke(str(name));
    const rt = go.method<Il2Cpp.Object>("AddComponent").invoke(typeOf(CORE, "UnityEngine.RectTransform"));
    if (parent) rt.method("SetParent", 2).invoke(parent, false);
    return { go, rt };
}

export function add(go: Il2Cpp.Object, asm: string, type: string): Il2Cpp.Object {
    return go.method<Il2Cpp.Object>("AddComponent").invoke(typeOf(asm, type));
}

export function v2(x: number, y: number): Il2Cpp.ValueType {
    const o = cls(CORE, "UnityEngine.Vector2").alloc();
    o.method(".ctor", 2).invoke(x, y);
    return o.unbox();
}

/** Позиция/размер в логических пикселях от левого верхнего угла родителя (якорь и pivot — левый верхний угол). */
export function place(rt: Il2Cpp.Object, x: number, y: number, w: number, h: number): void {
    rt.method("set_anchorMin").invoke(v2(0, 1));
    rt.method("set_anchorMax").invoke(v2(0, 1));
    rt.method("set_pivot").invoke(v2(0, 1));
    rt.method("set_anchoredPosition").invoke(v2(x * kit.k, -y * kit.k));
    rt.method("set_sizeDelta").invoke(v2(w * kit.k, h * kit.k));
}

/** Позиция по центру элемента в логических пикселях экрана (начало — левый верхний угол); для родителя с центральным якорем в центре холста. */
export function placeAtScreenCenter(rt: Il2Cpp.Object, cx: number, cy: number, w: number, h: number, parentOffset: [number, number] = [0, 0]): void {
    rt.method("set_anchorMin").invoke(v2(0.5, 0.5));
    rt.method("set_anchorMax").invoke(v2(0.5, 0.5));
    rt.method("set_pivot").invoke(v2(0.5, 0.5));
    rt.method("set_anchoredPosition").invoke(v2((cx - LOGICAL_W / 2) * kit.k - parentOffset[0], (kit.screenH / 2 - cy) * kit.k - parentOffset[1]));
    rt.method("set_sizeDelta").invoke(v2(w * kit.k, h * kit.k));
}

export function image(go: Il2Cpp.Object, r: number, g: number, b: number, a: number): Il2Cpp.Object {
    const img = add(go, UI, "UnityEngine.UI.Image");
    img.method("set_color").invoke(color(r, g, b, a));
    return img;
}

export function label(parent: Il2Cpp.Object, text: string, x: number, y: number, w: number, h: number, size: number, align = 3): Il2Cpp.Object {
    const n = node("txt", parent);
    place(n.rt, x, y, w, h);
    const t = add(n.go, UI, "UnityEngine.UI.Text");
    if (kit.font) t.method("set_font").invoke(kit.font);
    t.method("set_text").invoke(str(text));
    t.method("set_fontSize").invoke(Math.round(size * kit.k));
    t.method("set_alignment").invoke(align);      // 3 = MiddleLeft, 4 = MiddleCenter
    t.method("set_color").invoke(color(1, 1, 1, 1));
    t.method("set_raycastTarget").invoke(false);
    return t;
}

const crcTable = Array.from({ length: 256 }, (_, n) => { let c = n; for (let i = 0; i < 8; i++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1; return c >>> 0; });
function crc32(b: Uint8Array, from = 0, to = b.length): number {
    let c = 0xffffffff;
    for (let i = from; i < to; i++) c = crcTable[(c ^ b[i]) & 255] ^ (c >>> 8);
    return (c ^ 0xffffffff) >>> 0;
}

/** PNG (RGBA, «store»-deflate) со скруглённым белым прямоугольником w×h и антиалиасингом по краю. */
function roundedPng(w: number, h: number, r: number): Uint8Array {
    const stride = w * 4 + 1, raw = new Uint8Array(stride * h);
    for (let y = 0; y < h; y++) {
        for (let x = 0; x < w; x++) {
            // расстояние до угловой дуги: считаем только в угловых квадратах r×r
            const dx = Math.max(r - x - 0.5, x + 0.5 - (w - r), 0), dy = Math.max(r - y - 0.5, y + 0.5 - (h - r), 0);
            const cover = Math.max(0, Math.min(1, r + 0.5 - Math.hypot(dx, dy)));
            const o = y * stride + 1 + x * 4;
            raw[o] = raw[o + 1] = raw[o + 2] = 255; raw[o + 3] = Math.round(cover * 255);
        }
    }
    let a = 1, bsum = 0;
    for (const v of raw) { a = (a + v) % 65521; bsum = (bsum + a) % 65521; }
    const blocks = Math.ceil(raw.length / 65535);
    const z = new Uint8Array(2 + raw.length + blocks * 5 + 4);
    let o = 0;
    z[o++] = 0x78; z[o++] = 0x01;
    for (let i = 0; i < blocks; i++) {
        const len = Math.min(65535, raw.length - i * 65535);
        z[o++] = i === blocks - 1 ? 1 : 0;
        z[o++] = len & 255; z[o++] = len >> 8; z[o++] = ~len & 255; z[o++] = (~len >> 8) & 255;
        z.set(raw.subarray(i * 65535, i * 65535 + len), o); o += len;
    }
    const adler = ((bsum << 16) | a) >>> 0;
    z[o++] = adler >>> 24; z[o++] = (adler >>> 16) & 255; z[o++] = (adler >>> 8) & 255; z[o++] = adler & 255;
    const parts: Uint8Array[] = [Uint8Array.from([137, 80, 78, 71, 13, 10, 26, 10])];
    const chunk = (type: string, data: Uint8Array) => {
        const buf = new Uint8Array(12 + data.length), dv = new DataView(buf.buffer);
        dv.setUint32(0, data.length);
        for (let i = 0; i < 4; i++) buf[4 + i] = type.charCodeAt(i);
        buf.set(data, 8);
        dv.setUint32(8 + data.length, crc32(buf, 4, 8 + data.length));
        parts.push(buf);                               // не push(...buf): у большого буфера движок падает с «too many arguments»
    };
    const ihdr = new Uint8Array(13), hv = new DataView(ihdr.buffer);
    hv.setUint32(0, w); hv.setUint32(4, h); ihdr[8] = 8; ihdr[9] = 6;
    chunk("IHDR", ihdr); chunk("IDAT", z.subarray(0, o)); chunk("IEND", new Uint8Array(0));
    const png = new Uint8Array(parts.reduce((n, p) => n + p.length, 0));
    let at = 0;
    for (const p of parts) { png.set(p, at); at += p.length; }
    return png;
}

const roundTex = new Map<string, Il2Cpp.Object>();
const roundFailed = new Set<string>();
/** Текстура скруглённого прямоугольника (кэшируется по размеру); null, если собрать не удалось. */
function roundedTexture(w: number, h: number, r: number): Il2Cpp.Object | null {
    const key = `${w}x${h}x${r}`;
    if (roundFailed.has(key)) return null;
    const old = roundTex.get(key);
    if (old && !old.handle.add(0x10).readPointer().isNull()) return old;     // m_CachedPtr: не выгружена игрой
    try {
        const bytes = roundedPng(w, h, r);
        const arr = Il2Cpp.array<number>(Il2Cpp.corlib.class("System.Byte"), bytes.length);
        arr.handle.add(Il2Cpp.Array.headerSize).writeByteArray(bytes.buffer as ArrayBuffer);
        const tex = cls(CORE, "UnityEngine.Texture2D").alloc();
        tex.method(".ctor", 2).invoke(2, 2);
        if (!cls("UnityEngine.ImageConversionModule", "UnityEngine.ImageConversion").method<boolean>("LoadImage", 2).invoke(tex, arr)) return null;
        tex.method("set_hideFlags").invoke(61);
        try { tex.ref(true); } catch (_) { /* без закрепления */ }
        roundTex.set(key, tex);
        return tex;
    } catch (e) { roundFailed.add(key); log("скруглённая кнопка: " + e); return null; }
}

/** Радиус скругления кнопок в логических пикселях. */
export const BUTTON_RADIUS = 16;

export function button(parent: Il2Cpp.Object, text: string, x: number, y: number, w: number, h: number, size: number,
                bg: [number, number, number, number], onClick: () => void): Il2Cpp.Object {
    const n = node("btn", parent);
    place(n.rt, x, y, w, h);
    let img: Il2Cpp.Object;
    const tex = bg[3] >= 0.5 ? roundedTexture(Math.round(w), Math.round(h), Math.min(BUTTON_RADIUS, Math.floor(Math.min(w, h) / 2))) : null;
    if (tex) {
        img = add(n.go, UI, "UnityEngine.UI.RawImage");
        img.method("set_texture").invoke(tex);
        img.method("set_color").invoke(color(...bg));
    } else {
        img = image(n.go, ...bg);                      // невидимые кнопки-«хитбоксы» и запасной вариант
    }
    const btn = add(n.go, UI, "UnityEngine.UI.Button");
    btn.method("set_targetGraphic").invoke(img);
    const action = Il2Cpp.delegate(cls(CORE, "UnityEngine.Events.UnityAction"), () => {
        try { onClick(); } catch (e) { log("ошибка клика: " + e); }
    });
    btn.method<Il2Cpp.Object>("get_onClick").invoke().method("AddListener").invoke(action);
    label(n.rt, text, 0, 0, w, h, size, 4);
    return btn;
}

/**
 * Поле ввода числа: по касанию открывается цифровая клавиатура телефона. `onCommit` вызывается по завершении ввода
 * (кнопка «Готово» клавиатуры или потеря фокуса) с введённым текстом.
 */
export function numberField(parent: Il2Cpp.Object, value: string, x: number, y: number, w: number, h: number, size: number,
                decimal: boolean, onCommit: (text: string) => void): Il2Cpp.Object {
    const n = node("input", parent);
    place(n.rt, x, y, w, h);
    const bg = image(n.go, 0.14, 0.14, 0.10, 1);
    const txt = label(n.rt, value, 8, 0, w - 16, h, size, 4);
    tint(txt, gold());
    txt.method("set_raycastTarget").invoke(false);
    const input = add(n.go, UI, "UnityEngine.UI.InputField");
    input.method("set_targetGraphic").invoke(bg);
    input.method("set_textComponent").invoke(txt);
    input.method("set_contentType").invoke(decimal ? 3 : 2);        // 2 = IntegerNumber, 3 = DecimalNumber
    input.method("set_lineType").invoke(0);                         // SingleLine
    input.method("set_characterLimit").invoke(10);
    input.method("set_text").invoke(str(value));
    const str1 = cls(CORE, "UnityEngine.Events.UnityAction`1").inflate(Il2Cpp.corlib.class("System.String"));
    const act = Il2Cpp.delegate(str1, () => {
        try { onCommit(input.method<Il2Cpp.String>("get_text").invoke().content ?? ""); } catch (e) { log("ввод числа: " + e); }
    });
    input.method<Il2Cpp.Object>("get_onEndEdit").invoke().method("AddListener").invoke(act);
    return input;
}

export function clearChildren(rt: Il2Cpp.Object): void {
    const count = rt.method<number>("get_childCount").invoke();
    const destroy = cls(CORE, "UnityEngine.Object").method("Destroy", 1);
    for (let i = count - 1; i >= 0; i--) {
        const child = rt.method<Il2Cpp.Object>("GetChild").invoke(i);
        destroy.invoke(child.method<Il2Cpp.Object>("get_gameObject").invoke());
    }
}

export const fmt = (v: number) => String(Math.round(v * 100) / 100);

// палитра окна «Энциклопедия»
export const gold = () => color(0.97, 0.85, 0.55, 1);

export function tint(t: Il2Cpp.Object, c: Il2Cpp.ValueType): void {
    t.method("set_color").invoke(c);
}

// ---------------------------------------------------------------- окно со списком галочек

export interface ChecklistItem { key: string; label: string }
export interface ChecklistSection { header: string; items: ChecklistItem[] }
export interface ChecklistOptions {
    /** Имя объекта окна: повторное открытие с тем же именем заменяет прежнее окно. */
    name: string;
    title: string;
    subtitle?: string;
    sections: ChecklistSection[];
    get: (key: string) => boolean;
    set: (key: string, value: boolean) => void;
}

const checklistWindows = new Map<string, Il2Cpp.Object>();

/**
 * Окно «список галочек» на весь холст, с прокруткой пальцем. Нажатие на строку переключает галочку и сразу вызывает `set`.
 * Вызывать на главном потоке (из обработчика кнопки). Возвращает объект окна.
 */
export function openChecklist(o: ChecklistOptions): Il2Cpp.Object {
    const destroyGo = (go: Il2Cpp.Object) => { try { cls(CORE, "UnityEngine.Object").method("Destroy", 1).invoke(go); } catch (_) { /* уже удалён */ } };
    const prev = checklistWindows.get(o.name);
    if (prev) destroyGo(prev);

    const win = node(o.name, kit.canvasTf);
    checklistWindows.set(o.name, win.go);
    win.rt.method("set_anchorMin").invoke(v2(0, 0));
    win.rt.method("set_anchorMax").invoke(v2(1, 1));
    win.rt.method("set_offsetMin").invoke(v2(0, 0));
    win.rt.method("set_offsetMax").invoke(v2(0, 0));
    image(win.go, 0.02, 0.03, 0.06, 1);

    tint(label(win.rt, o.title, 0, 90, LOGICAL_W, 80, 46, 4), gold());
    if (o.subtitle) tint(label(win.rt, o.subtitle, 30, 170, LOGICAL_W - 60, 60, 21, 4), color(0.7, 0.75, 0.85, 1));
    button(win.rt, "X", LOGICAL_W - 100, 60, 80, 80, 54, [0, 0, 0, 0.01], () => { win.go.method("SetActive").invoke(false); });

    const top = o.subtitle ? 240 : 190;
    const W = LOGICAL_W - 60;
    const view = node("view", win.rt);
    place(view.rt, 30, top, W, kit.screenH - top - 30);
    image(view.go, 0, 0, 0, 0.01);                                 // нужен для приёма касаний
    add(view.go, UI, "UnityEngine.UI.RectMask2D");
    const c = node("content", view.rt);
    place(c.rt, 0, 0, W, 100);
    const sr = add(view.go, UI, "UnityEngine.UI.ScrollRect");
    sr.method("set_content").invoke(c.rt);
    sr.method("set_viewport").invoke(view.rt);
    sr.method("set_horizontal").invoke(false);
    sr.method("set_vertical").invoke(true);
    sr.method("set_movementType").invoke(2);                       // Clamped
    sr.method("set_scrollSensitivity").invoke(30);

    let y = 0;
    for (const sec of o.sections) {
        tint(label(c.rt, sec.header, 10, y, W - 20, 56, 24, 3), color(0.55, 0.8, 1, 1));
        y += 60;
        for (const it of sec.items) {
            const row = node("row", c.rt);
            place(row.rt, 0, y, W, 76);
            const bg = image(row.go, 0.08, 0.10, 0.15, 1);
            const btn = add(row.go, UI, "UnityEngine.UI.Button");
            btn.method("set_targetGraphic").invoke(bg);
            const box = node("box", row.rt);
            place(box.rt, 14, 12, 52, 52);
            image(box.go, 0.35, 0.4, 0.5, 1);
            const mark = node("mark", box.rt);
            place(mark.rt, 8, 8, 36, 36);
            image(mark.go, 0.97, 0.85, 0.55, 1);
            mark.go.method("SetActive").invoke(o.get(it.key));
            tint(label(row.rt, it.label, 84, 0, W - 100, 76, 23, 3), color(0.92, 0.92, 0.92, 1));
            const act = Il2Cpp.delegate(cls(CORE, "UnityEngine.Events.UnityAction"), () => {
                try {
                    const v = !o.get(it.key);
                    o.set(it.key, v);
                    mark.go.method("SetActive").invoke(v);
                } catch (e) { log("галочка: " + e); }
            });
            btn.method<Il2Cpp.Object>("get_onClick").invoke().method("AddListener").invoke(act);
            y += 82;
        }
        y += 14;
    }
    c.rt.method("set_sizeDelta").invoke(v2(W * kit.k, (y + 40) * kit.k));
    return win.go;
}

// ---------------------------------------------------------------- окно с числовыми полями

export interface NumberFormField { key: string; min: number; max: number; decimal?: boolean }
export interface NumberFormRow { label: string; fields: NumberFormField[] }
export interface NumberFormOptions {
    /** Имя объекта окна: повторное открытие с тем же именем заменяет прежнее окно. */
    name: string;
    title: string;
    subtitle?: string;
    /** Подписи над столбцами полей (по одной на поле строки). */
    columns?: string[];
    rows: NumberFormRow[];
    get: (key: string) => number;
    set: (key: string, value: number) => void;
}

/**
 * Окно «таблица чисел» на весь холст, с прокруткой: строка = подпись + несколько полей ввода. Значение зажимается в [min, max]
 * и сразу передаётся в `set`. Вызывать на главном потоке.
 */
export function openNumberForm(o: NumberFormOptions): Il2Cpp.Object {
    const destroyGo = (go: Il2Cpp.Object) => { try { cls(CORE, "UnityEngine.Object").method("Destroy", 1).invoke(go); } catch (_) { /* уже удалён */ } };
    const prev = checklistWindows.get(o.name);
    if (prev) destroyGo(prev);

    const win = node(o.name, kit.canvasTf);
    checklistWindows.set(o.name, win.go);
    win.rt.method("set_anchorMin").invoke(v2(0, 0));
    win.rt.method("set_anchorMax").invoke(v2(1, 1));
    win.rt.method("set_offsetMin").invoke(v2(0, 0));
    win.rt.method("set_offsetMax").invoke(v2(0, 0));
    image(win.go, 0.02, 0.03, 0.06, 1);

    tint(label(win.rt, o.title, 0, 90, LOGICAL_W, 80, 46, 4), gold());
    if (o.subtitle) tint(label(win.rt, o.subtitle, 30, 170, LOGICAL_W - 60, 60, 21, 4), color(0.7, 0.75, 0.85, 1));
    button(win.rt, "X", LOGICAL_W - 100, 60, 80, 80, 54, [0, 0, 0, 0.01], () => { win.go.method("SetActive").invoke(false); });

    const top = o.subtitle ? 240 : 190;
    const W = LOGICAL_W - 60;
    const view = node("view", win.rt);
    place(view.rt, 30, top, W, kit.screenH - top - 30);
    image(view.go, 0, 0, 0, 0.01);
    add(view.go, UI, "UnityEngine.UI.RectMask2D");
    const c = node("content", view.rt);
    place(c.rt, 0, 0, W, 100);
    const sr = add(view.go, UI, "UnityEngine.UI.ScrollRect");
    sr.method("set_content").invoke(c.rt);
    sr.method("set_viewport").invoke(view.rt);
    sr.method("set_horizontal").invoke(false);
    sr.method("set_vertical").invoke(true);
    sr.method("set_movementType").invoke(2);
    sr.method("set_scrollSensitivity").invoke(30);

    const nf = Math.max(1, ...o.rows.map(r => r.fields.length));
    const labelW = 220, gap = 16;
    const fw = (W - labelW - 20 - gap * nf) / nf;
    const fx = (i: number) => labelW + 20 + i * (fw + gap);
    let y = 0;
    if (o.columns) {
        o.columns.forEach((t, i) => tint(label(c.rt, t, fx(i), y, fw, 50, 24, 4), color(0.55, 0.8, 1, 1)));
        y += 56;
    }
    for (const r of o.rows) {
        const row = node("row", c.rt);
        place(row.rt, 0, y, W, 76);
        image(row.go, 0.08, 0.10, 0.15, 1);
        tint(label(row.rt, r.label, 20, 0, labelW, 76, 24, 3), color(0.92, 0.92, 0.92, 1));
        r.fields.forEach((f, i) => {
            const input = numberField(row.rt, fmt(o.get(f.key)), fx(i), 8, fw, 60, 32, !!f.decimal, text => {
                let v = parseFloat(text.replace(",", "."));
                if (!isFinite(v)) v = o.get(f.key);
                v = Math.min(f.max, Math.max(f.min, f.decimal ? v : Math.round(v)));
                if (v !== o.get(f.key)) o.set(f.key, v);
                input.method("set_text").invoke(str(fmt(v)));
            });
        });
        y += 82;
    }
    c.rt.method("set_sizeDelta").invoke(v2(W * kit.k, (y + 40) * kit.k));
    return win.go;
}
