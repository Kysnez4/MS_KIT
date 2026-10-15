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

export function button(parent: Il2Cpp.Object, text: string, x: number, y: number, w: number, h: number, size: number,
                bg: [number, number, number, number], onClick: () => void): Il2Cpp.Object {
    const n = node("btn", parent);
    place(n.rt, x, y, w, h);
    const img = image(n.go, ...bg);
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
