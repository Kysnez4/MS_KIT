import { markSession, mods, notifyMainMenu, restoreSession, sessionDiff, setEnabled, setSetting, type ModState } from "./core";
import { loadExternalMods, MODS_DIRS } from "./ext";
import { log } from "./log";
import { CORE, LOGICAL_W, UI, UIMODULE, add, button, clearChildren, cls, color, fmt, gold, image, kit, label, node, numberField, place, placeAtScreenCenter, tint, v2 } from "./uikit";

/**
 * Меню «MODS» на Unity uGUI: плавающая кнопка + панель со списком модов.
 * Строится из GameObject'ов через IL2CPP, без Java: Java bridge Frida 16 на целевом телефоне падает.
 * Позиционирование через RectTransform.SetInsetAndSizeFromParentEdge, поэтому структуры Vector2 не нужны.
 */


let k = 1;                       // единиц холста на логический пиксель
let panel: Il2Cpp.Object | null = null;      // GameObject окна «Моды»
let content: Il2Cpp.Object | null = null;    // RectTransform списка внутри ScrollRect
let font: Il2Cpp.Object | null = null;
let rootGo: Il2Cpp.Object | null = null;
let screenHeight = 1280;
let status = "";
let entryGo: Il2Cpp.Object | null = null;
let winRt: Il2Cpp.Object | null = null;
let bannerGo: Il2Cpp.Object | null = null;
let dialogShown = false;

/** Изменения, сделанные в окне «Моды» и ещё не подтверждённые перезапуском: мод -> прежнее состояние. */
const pending = new Map<ModState, { enabled: boolean; settings: Record<string, number> }>();
function track(st: ModState): void {
    if (!st.def.restartRequired) return;
    if (!pending.has(st)) pending.set(st, { enabled: st.enabled, settings: { ...st.settings } });
}

function destroy(go: Il2Cpp.Object | null): void {
    if (go) { try { cls(CORE, "UnityEngine.Object").method("Destroy", 1).invoke(go); } catch (_) { /* уже удалён */ } }
}

/** Окно-вопрос поверх всего холста: затемнение, заголовок, текст и кнопки. Закрывается любой кнопкой. */
function showDialog(title: string, text: string, buttons: Array<[string, () => void]>, tall = 0): Il2Cpp.Object {
    const dim = node("ModKitDialog", kit.canvasTf);
    dim.rt.method("set_anchorMin").invoke(v2(0, 0));
    dim.rt.method("set_anchorMax").invoke(v2(1, 1));
    dim.rt.method("set_offsetMin").invoke(v2(0, 0));
    dim.rt.method("set_offsetMax").invoke(v2(0, 0));
    const dimImg = image(dim.go, 0, 0, 0, 0.8);
    add(dim.go, UI, "UnityEngine.UI.Button").method("set_targetGraphic").invoke(dimImg);   // глотает касания под окном
    const h = 420 + tall;
    const top = Math.max(40, (screenHeight - h) / 2);
    const box = node("box", dim.rt);
    place(box.rt, 40, top, LOGICAL_W - 80, h);
    image(box.go, 0.09, 0.09, 0.11, 1);
    const t = label(box.rt, title, 20, 16, LOGICAL_W - 120, 70, 38, 4);
    tint(t, gold());
    const body = label(box.rt, text, 30, 96, LOGICAL_W - 140, h - 96 - 110, 25, 0);
    tint(body, color(0.9, 0.9, 0.9, 1));
    const bw = (LOGICAL_W - 80 - 30 * (buttons.length + 1)) / buttons.length;
    buttons.forEach(([name, fn], i) => {
        button(box.rt, name, 30 + i * (bw + 30), h - 100, bw, 80, 28, [0.22, 0.25, 0.30, 1], () => {
            destroy(dim.go);
            fn();
        });
    });
    return dim.go;
}

const quitGame = () => cls(CORE, "UnityEngine.Application").method("Quit", 0).invoke();

/** Полоса внизу окна «Моды»: изменения применены, но полностью — после перезапуска. */
function showBanner(): void {
    destroy(bannerGo);
    bannerGo = null;
    if (!winRt || pending.size === 0) return;
    const b = node("banner", winRt);
    bannerGo = b.go;
    const h = 210;
    place(b.rt, 20, screenHeight - h - 20, LOGICAL_W - 40, h);
    image(b.go, 0.10, 0.12, 0.18, 0.98);
    const t = label(b.rt, `Изменено модов: ${pending.size}. Часть правок (пулы выпадения, синергии) полностью заработает после перезапуска.`,
        20, 8, LOGICAL_W - 80, 100, 22, 0);
    tint(t, color(0.95, 0.9, 0.7, 1));
    const bw = (LOGICAL_W - 40 - 90) / 2;
    button(b.rt, "Выйти и применить", 30, h - 90, bw, 76, 26, [0.20, 0.38, 0.22, 1], () => {
        markSession();
        log("применение: выход из игры по запросу пользователя");
        quitGame();
    });
    button(b.rt, "Отмена", 60 + bw, h - 90, bw, 76, 26, [0.38, 0.20, 0.20, 1], () => {
        for (const [st, was] of pending) {
            setEnabled(st, was.enabled);
            for (const [key, v] of Object.entries(was.settings)) if (st.settings[key] !== v) setSetting(st, key, v);
        }
        pending.clear();
        rebuild();
    });
}


/** Пересобирает карточки модов (после любого изменения). Позиция прокрутки сохраняется. */
function rebuild(): void {
    if (!content) return;
    clearChildren(content);
    const W = LOGICAL_W - 60;
    let y = 0;
    if (status) {
        const t = label(content, status, 10, 0, W - 20, 96, 22, 0);
        tint(t, color(0.97, 0.85, 0.55, 1));
        y += 106;
    }
    for (const st of mods) {
        const opts = (st.def.options ?? []).filter(o => !o.hidden);
        const hasSettings = !!(st.def.openSettings && st.enabled && st.ctx);
        const shots = (st.def.meta?.screenshots ?? []).slice(0, 3);
        const h = 78 + (st.def.description ? 62 : 0) + (shots.length ? 170 : 0) + (st.def.meta?.version || st.def.meta?.author ? 40 : 0) + opts.length * 76 + (hasSettings ? 80 : 0) + 16;
        // карточка: нажатие переключает мод
        const card = node("card", content);
        place(card.rt, 0, y, W, h);
        const bg = st.enabled ? [0.10, 0.20, 0.12, 1] : [0.09, 0.09, 0.09, 1];
        const cardImg = image(card.go, bg[0], bg[1], bg[2], bg[3]);
        const cardBtn = add(card.go, UI, "UnityEngine.UI.Button");
        cardBtn.method("set_targetGraphic").invoke(cardImg);
        const act = Il2Cpp.delegate(cls(CORE, "UnityEngine.Events.UnityAction"), () => {
            try { track(st); setEnabled(st, !st.enabled); rebuild(); } catch (e) { log("ошибка переключения: " + e); }
        });
        cardBtn.method<Il2Cpp.Object>("get_onClick").invoke().method("AddListener").invoke(act);

        const icon = st.def.meta?.icon;
        if (icon) picture(card.rt, icon, 14, 8, 62, 62);
        const name = label(card.rt, st.def.name, icon ? 88 : 20, 8, W - (icon ? 258 : 190), 62, 32);
        tint(name, st.enabled ? color(1, 1, 1, 1) : color(0.85, 0.85, 0.85, 1));
        const state = label(card.rt, st.enabled ? "ВКЛ" : "ВЫКЛ", W - 160, 8, 140, 62, 30, 5);   // 5 = MiddleRight
        tint(state, st.enabled ? color(0.45, 1, 0.45, 1) : color(0.55, 0.55, 0.55, 1));
        let ly = 74;
        if (st.def.description) {
            const d = label(card.rt, st.def.description, 20, ly - 4, W - 40, 58, 23, 0);   // 0 = UpperLeft
            tint(d, color(0.7, 0.7, 0.7, 1));
            ly += 62;
        }
        const info = [st.def.meta?.version && "v" + st.def.meta.version, st.def.meta?.author].filter(Boolean).join(" · ");
        if (info) {
            const t = label(card.rt, info, 20, ly - 4, W - 40, 36, 21, 0);
            tint(t, color(0.5, 0.5, 0.5, 1));
            ly += 40;
        }
        if (shots.length) {
            const sw = (W - 40 - (shots.length - 1) * 10) / shots.length;
            shots.forEach((b, i) => picture(card.rt, b, 20 + i * (sw + 10), ly - 4, sw, 160));
            ly += 170;
        }
        for (const opt of opts) {
            const lab = label(content, opt.label, 40, y + ly, 250, 64, 26);
            tint(lab, color(0.9, 0.9, 0.9, 1));
            const step = (dir: number) => {
                const next = Math.min(opt.max, Math.max(opt.min, st.settings[opt.key] + dir * opt.step));
                track(st);
                setSetting(st, opt.key, Math.round(next * 1000) / 1000);
                rebuild();
            };
            button(content, "−", 330, y + ly, 80, 64, 38, [0.22, 0.22, 0.22, 1], () => step(-1));
            const decimal = !Number.isInteger(opt.step) || !Number.isInteger(opt.min);
            numberField(content, fmt(st.settings[opt.key]), 414, y + ly + 2, 112, 60, 32, decimal, text => {
                const v = parseFloat(text.replace(",", "."));
                if (!isFinite(v)) { rebuild(); return; }
                const next = Math.round(Math.min(opt.max, Math.max(opt.min, v)) * 1000) / 1000;
                if (next !== st.settings[opt.key]) { track(st); setSetting(st, opt.key, next); }
                rebuild();
            });
            button(content, "+", 530, y + ly, 80, 64, 38, [0.22, 0.22, 0.22, 1], () => step(1));
            ly += 76;
        }
        if (hasSettings) {
            button(content, "Настройки", 40, y + ly, 260, 64, 28, [0.18, 0.28, 0.42, 1], () => st.def.openSettings!(st.ctx!));
            ly += 80;
        }
        y += h + 14;
    }
    const hint = label(content, "Добавить мод: архив .zip в Download/ModKit (или files/modkit/mods) и кнопка «+» вверху.", 10, y, W - 20, 60, 21, 0);
    tint(hint, color(0.5, 0.5, 0.5, 1));
    content.method("set_sizeDelta").invoke(v2(W * k, (y + 90 + (pending.size ? 220 : 0)) * k));
    showBanner();
}

/** Подходящий экранный холст игры (не WorldSpace, корневой, активный) — к нему подвешиваем меню. */
function findGameCanvas(): Il2Cpp.Object {
    const canvases = Il2Cpp.gc.choose(cls(UIMODULE, "UnityEngine.Canvas"));
    let best: Il2Cpp.Object | null = null;
    let bestOrder = -Infinity;
    for (const c of canvases) {
        try {
            const mode = c.method<number>("get_renderMode").invoke();                // 0 Overlay, 1 Camera, 2 WorldSpace
            const root = c.method<boolean>("get_isRootCanvas").invoke();
            const on = c.method<boolean>("get_enabled").invoke() && c.method<Il2Cpp.Object>("get_gameObject").invoke().method<boolean>("get_activeInHierarchy").invoke();
            const order = c.method<number>("get_sortingOrder").invoke();
            const name = c.method<Il2Cpp.Object>("get_gameObject").invoke().method<Il2Cpp.String>("get_name").invoke().content;
            log(`холст «${name}»: mode=${mode} root=${root} active=${on} order=${order} scale=${c.method<number>("get_scaleFactor").invoke()}`);
            if (mode !== 2 && root && on && order >= bestOrder) { best = c; bestOrder = order; }
        } catch (e) { log("холст: " + e); }
    }
    if (!best) throw new Error("подходящий Canvas игры не найден");
    return best;
}

/** Иконка гаечного ключа, нарисованная попиксельно (Sprite.Create вырезан из игры, поэтому RawImage + Texture2D). */
function wrenchTexture(): Il2Cpp.Object {
    const N = 96;
    const sc = 48 / N;                           // рисуем в сетке 48x48, растягиваем до N
    const tex = cls(CORE, "UnityEngine.Texture2D").alloc();
    tex.method(".ctor", 2).invoke(N, N);
    const ink = color(0.96, 0.96, 0.88, 1);      // кремовый, как у иконок нижней панели
    const none = color(0, 0, 0, 0);
    const set = tex.method("SetPixel");
    const R2 = Math.SQRT2;
    for (let y = 0; y < N; y++) {
        for (let x = 0; x < N; x++) {
            const px = x * sc, py = y * sc;
            // головка с зевом в правом верхнем углу
            const hx = px - 34, hy = py - 34;
            const along = (hx + hy) / R2, perp = (hx - hy) / R2;
            const head = hx * hx + hy * hy <= 10.5 * 10.5 && !(along > 1 && Math.abs(perp) < 4);
            // рукоятка по диагонали
            const handle = Math.abs(px - py) / R2 <= 3 && px >= 11 && px <= 32;
            // кольцо на конце рукоятки
            const ex = px - 11, ey = py - 11;
            const d2 = ex * ex + ey * ey;
            const end = d2 <= 5.5 * 5.5 && d2 >= 2 * 2;
            set.invoke(x, y, head || handle || end ? ink : none);
        }
    }
    tex.method("Apply", 0).invoke();
    return tex;
}

const textures = new Map<Uint8Array, Il2Cpp.Object | null>();
/** PNG/JPG из архива мода -> Texture2D (через ImageConversion.LoadImage). Кэшируется, при ошибке null. */
function textureOf(bytes: Uint8Array): Il2Cpp.Object | null {
    if (textures.has(bytes)) return textures.get(bytes)!;
    let tex: Il2Cpp.Object | null = null;
    try {
        const arr = Il2Cpp.array<number>(Il2Cpp.corlib.class("System.Byte"), bytes.length);
        arr.handle.add(Il2Cpp.Array.headerSize).writeByteArray(bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.length) as ArrayBuffer);
        tex = cls(CORE, "UnityEngine.Texture2D").alloc();
        tex.method(".ctor", 2).invoke(2, 2);
        const ok = cls("UnityEngine.ImageConversionModule", "UnityEngine.ImageConversion").method<boolean>("LoadImage", 2).invoke(tex, arr);
        if (!ok) tex = null;
    } catch (e) { log("картинка мода: " + e); tex = null; }
    textures.set(bytes, tex);
    return tex;
}

/** RawImage с текстурой в рамке w×h (вписывается с сохранением пропорций). */
function picture(parent: Il2Cpp.Object, bytes: Uint8Array, x: number, y: number, w: number, h: number): void {
    const tex = textureOf(bytes);
    if (!tex) return;
    const tw = tex.method<number>("get_width").invoke(), th = tex.method<number>("get_height").invoke();
    const sc = Math.min(w / tw, h / th);
    const pw = tw * sc, ph = th * sc;
    const n = node("pic", parent);
    place(n.rt, x + (w - pw) / 2, y + (h - ph) / 2, pw, ph);
    const raw = add(n.go, UI, "UnityEngine.UI.RawImage");
    raw.method("set_texture").invoke(tex);
    raw.method("set_raycastTarget").invoke(false);
}

function buildUi(menu: Il2Cpp.Object): void {
    const dbFont = Il2Cpp.domain.assembly("Assembly-CSharp").image.class("Z_Database").field<Il2Cpp.Object>("Font_Apply_Medium").value;
    font = dbFont.isNull() ? null : dbFont;

    const gameCanvas = findGameCanvas();
    const scale = gameCanvas.method<number>("get_scaleFactor").invoke();
    k = (cls(CORE, "UnityEngine.Screen").method<number>("get_width").invoke() / scale) / LOGICAL_W;
    const screenH = cls(CORE, "UnityEngine.Screen").method<number>("get_height").invoke() / scale / k;
    screenHeight = screenH;
    kit.k = k;
    kit.font = font;
    kit.screenH = screenH;
    kit.canvasTf = gameCanvas.method<Il2Cpp.Object>("get_transform").invoke();
    const canvasTf = gameCanvas.method<Il2Cpp.Object>("get_transform").invoke();

    // окно — растянутый на весь холст дочерний объект (рисуется поверх всего)
    const win = node("ModKitWindow", canvasTf);
    rootGo = win.go;
    win.rt.method("set_anchorMin").invoke(v2(0, 0));
    win.rt.method("set_anchorMax").invoke(v2(1, 1));
    win.rt.method("set_offsetMin").invoke(v2(0, 0));
    win.rt.method("set_offsetMax").invoke(v2(0, 0));
    panel = win.go;
    winRt = win.rt;
    image(win.go, 0, 0, 0, 1);                                    // чёрный фон, как у энциклопедии

    const title = label(win.rt, "Моды", 0, 100, LOGICAL_W, 80, 52, 4);
    tint(title, gold());
    button(win.rt, "X", LOGICAL_W - 100, 70, 80, 80, 54, [0, 0, 0, 0.01], () => panel!.method("SetActive").invoke(false));

    // «+»: подключить новые моды из папки
    button(win.rt, "+", 20, 70, 80, 80, 64, [0, 0, 0, 0.01], () => {
        const r = loadExternalMods();
        status = r.added.length
            ? `Подключено: ${r.added.join(", ")}` + (r.errors.length ? `. Ошибки: ${r.errors.join("; ")}` : "")
            : r.errors.length ? `Ошибки: ${r.errors.join("; ")}`
            : `Новых модов нет (найдено файлов: ${r.files}). Положите архив .zip в ${MODS_DIRS[0].replace("/sdcard/", "")} и нажмите «+».`;
        log("«+»: " + status);
        rebuild();
    });

    // список с прокруткой пальцем
    const viewTop = 210;
    const view = node("view", win.rt);
    place(view.rt, 30, viewTop, LOGICAL_W - 60, screenH - viewTop - 30);
    image(view.go, 0, 0, 0, 0.01);                                // нужен для приёма касаний
    add(view.go, UI, "UnityEngine.UI.RectMask2D");
    const c = node("content", view.rt);
    content = c.rt;
    place(content, 0, 0, LOGICAL_W - 60, 100);
    const sr = add(view.go, UI, "UnityEngine.UI.ScrollRect");
    sr.method("set_content").invoke(content);
    sr.method("set_viewport").invoke(view.rt);
    sr.method("set_horizontal").invoke(false);
    sr.method("set_vertical").invoke(true);
    sr.method("set_movementType").invoke(2);                      // Clamped
    sr.method("set_scrollSensitivity").invoke(30);

    rebuild();
    panel.method("SetActive").invoke(false);

    // запись «Моды» на главной странице — в том же стиле, что и «Играть»; живёт внутри поля главного меню
    const field = menu.field<Il2Cpp.Object>("Obj_FieldMainMenu").value;
    const fieldTf = field.method<Il2Cpp.Object>("get_transform").invoke();
    const entry = node("ModKitEntry", fieldTf);
    entryGo = entry.go;
    // над иконкой «Статистика» (крайняя справа в нижней панели; на экране 720x1640 она в (610, 1460))
    placeAtScreenCenter(entry.rt, 610, 1335 * screenH / 1640, 100, 100);
    const eImg = image(entry.go, 0, 0, 0, 0.01);
    const eBtn = add(entry.go, UI, "UnityEngine.UI.Button");
    eBtn.method("set_targetGraphic").invoke(eImg);
    const open = Il2Cpp.delegate(cls(CORE, "UnityEngine.Events.UnityAction"), () => {
        try { rebuild(); panel!.method("SetActive").invoke(true); } catch (e) { log("ошибка открытия: " + e); }
    });
    eBtn.method<Il2Cpp.Object>("get_onClick").invoke().method("AddListener").invoke(open);
    const icon = node("icon", entry.rt);
    icon.rt.method("set_anchorMin").invoke(v2(0, 0));
    icon.rt.method("set_anchorMax").invoke(v2(1, 1));
    icon.rt.method("set_offsetMin").invoke(v2(8 * k, 8 * k));
    icon.rt.method("set_offsetMax").invoke(v2(-8 * k, -8 * k));
    const raw = add(icon.go, UI, "UnityEngine.UI.RawImage");
    raw.method("set_texture").invoke(wrenchTexture());
    raw.method("set_raycastTarget").invoke(false);
    notifyMainMenu(menu);
    const diff = sessionDiff();
    if (diff && !dialogShown) {
        dialogShown = true;
        const list = (a: string[]) => a.length ? a.join(", ") : "без модов";
        showDialog("Моды изменились",
            `Прошлая игра шла с: ${list(diff.before)}.

Сейчас включены: ${list(diff.now)}.

Продолжить с прежним набором модов или начать новую сессию с текущим?`,
            [["Как раньше", () => { restoreSession(); rebuild(); }], ["Как сейчас", () => { markSession(); }]], 120);
    }
    log("меню «Моды» создано (экран " + Math.round(LOGICAL_W) + "x" + Math.round(screenH) + ", k=" + k.toFixed(2) + ")");
}

/**
 * Строит меню из A_MainMenuInformation.Update — он вызывается каждый кадр на главном потоке Unity
 * и только на главном экране. При каждом новом экземпляре (возврат в меню) меню пересоздаётся.
 */
export function installMenu(): void {
    Il2Cpp.perform(() => {
        const update = Il2Cpp.domain.assembly("Assembly-CSharp").image.class("A_MainMenuInformation").method("Update");
        let lastSelf: NativePointer = NULL;
        let failures = 0;
        Interceptor.attach(update.virtualAddress, {
            onEnter(args) {
                if (args[0].equals(lastSelf) || failures >= 3) return;
                lastSelf = args[0];
                try {
                    buildUi(new Il2Cpp.Object(args[0]));
                } catch (e) {
                    failures++;
                    log("меню: " + e);
                    for (const g of [rootGo, entryGo]) if (g) { try { cls(CORE, "UnityEngine.Object").method("Destroy", 1).invoke(g); } catch (_) { /* ignore */ } }
                }
            },
        });
        log("меню MODS: хук на A_MainMenuInformation.Update установлен");
    });
}
