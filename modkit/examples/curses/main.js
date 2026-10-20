"use strict";
const { CORE, LOGICAL_W, UI, add, button, cls, color, gold, image, kit, label, node, place, placeAtScreenCenter, tint, v2 } = ModKit.ui;
/**
 * «Проклятия и дары»: экран, похожий на «Исследование» (открывается по нажатию на заголовок исследований), но красный.
 * Каждая плитка — исследование с недостатком: усиливает свою характеристику на 1% за уровень и ослабляет характеристику
 * парного исследования на 2% за уровень (доля 2 к 1). Пары взаимно исключают друг друга: выбрав одну плитку, вторая сбрасывается.
 */
const GIFT_PER_LEVEL = 1; // % дара за уровень
const CURSE_PER_LEVEL = 2; // % недостатка за уровень (2 к 1)
const MAX_LEVEL = 5;
const STATS = {
    attack: { label: "Приумножение атаки", effect: 16, mult: 1, good: 1 },
    hp: { label: "Максимальное здоровье", effect: 20, penalty: 21, mult: 1, good: 1 },
    cooldown: { label: "Перезарядка заклинаний", effect: 6, mult: 1, good: -1 },
    crit: { label: "Шанс критического удара", effect: 14, mult: 1, good: 1 },
    taken: { label: "Получаемый урон", effect: 23, mult: 1, good: -1 },
    evasion: { label: "Уклонение", effect: 25, mult: 1, good: 1 },
    speed: { label: "Скорость движения", effect: 30, mult: 1, good: 1 },
    regen: { label: "Исцеление в секунду", effect: 24, mult: 100, good: 1 },
    size: { label: "Размер заклинаний", effect: 8, penalty: 9, mult: 1, good: 1 },
    duration: { label: "Длительность заклинаний", effect: 10, mult: 1, good: 1 },
    mana: { label: "Получение маны", effect: 38, mult: 1, good: 1 },
    orbheal: { label: "Исцеление от крупицы жизни", effect: 36, mult: 1, good: 1 },
    rune: { label: "Время действия рун", effect: 135, mult: 1, good: 1 },
    items: { label: "Частота появления предметов", effect: 71, mult: 1, good: 1 },
    chests: { label: "Частота сундуков", effect: 127, mult: 1, good: -1, inverted: true },
    radius: { label: "Радиус сбора предметов", effect: 40, mult: 1, good: 1 },
};
const TILES = [
    { key: "attack", icon: 261, name: "Интеллект", stat: "attack", partner: "hp" },
    { key: "hp", icon: 262, name: "Живучесть", stat: "hp", partner: "attack" },
    { key: "cooldown", icon: 264, name: "Быстрый запуск", stat: "cooldown", partner: "crit" },
    { key: "crit", icon: 265, name: "Меткость", stat: "crit", partner: "cooldown" },
    { key: "taken", icon: 266, name: "Сопротивляемость", stat: "taken", partner: "evasion" },
    { key: "evasion", icon: 267, name: "Ловкость", stat: "evasion", partner: "taken" },
    { key: "speed", icon: 268, name: "Ускорение", stat: "speed", partner: "regen" },
    { key: "regen", icon: 269, name: "Оживление", stat: "regen", partner: "speed" },
    { key: "size", icon: 271, name: "Рассеивание", stat: "size", partner: "duration" },
    { key: "duration", icon: 272, name: "Концентрация", stat: "duration", partner: "size" },
    { key: "mana", icon: 263, name: "Очистка маны", stat: "mana", partner: "orbheal" },
    { key: "orbheal", icon: 273, name: "Лечение крупиц", stat: "orbheal", partner: "mana" },
    { key: "rune", icon: 275, name: "Благословение", stat: "rune", partner: "items" },
    { key: "items", icon: 279, name: "Удача", stat: "items", partner: "rune" },
    { key: "chests", icon: 280, name: "Добыча", stat: "chests", partner: "radius" },
    { key: "radius", icon: 270, name: "Поиск", stat: "radius", partner: "chests" },
];
const tile = (key) => TILES.find(t => t.key === key);
/** Итоговые изменения характеристик в процентах (положительное — «в сторону роста поля»). */
function deltas(levels) {
    const d = {};
    for (const t of TILES) {
        const lv = levels[t.key] ?? 0;
        if (!lv)
            continue;
        const gift = STATS[t.stat];
        d[t.stat] = (d[t.stat] ?? 0) + gift.good * GIFT_PER_LEVEL * lv;
        const pen = STATS[tile(t.partner).stat];
        d[tile(t.partner).stat] = (d[tile(t.partner).stat] ?? 0) - pen.good * CURSE_PER_LEVEL * lv;
    }
    return d;
}
const levelOf = (ctx, key) => Math.round(ctx.settings["lvl_" + key] ?? 0);
const allLevels = (ctx) => Object.fromEntries(TILES.map(t => [t.key, levelOf(ctx, t.key)]));
function describe(ctx, t) {
    const lv = levelOf(ctx, t.key);
    const gift = STATS[t.stat], partner = tile(t.partner), pen = STATS[partner.stat];
    const g = GIFT_PER_LEVEL * lv, c = CURSE_PER_LEVEL * lv;
    const gSign = gift.good === 1 ? "+" : "−", cSign = pen.good === 1 ? "−" : "+";
    return `${t.name}, уровень ${lv}/${MAX_LEVEL}\nДар: ${gift.label} ${gSign}${g}%\nПроклятие: ${pen.label} ${cSign}${c}%`;
}
// ---------------------------------------------------------------- применение к герою в бою
/** Итоговые изменения (в процентах, «в сторону роста характеристики») -> пары [ID эффекта игры, значение]. */
function toEffects(d) {
    const out = [];
    for (const [key, p] of Object.entries(d)) {
        const st = STATS[key];
        if (!st || !p)
            continue;
        const benefit = p * st.good; // >0 — игроку лучше
        if (benefit < 0 && st.penalty)
            out.push([st.penalty, -benefit * st.mult]);
        else
            out.push([st.effect, benefit * st.mult * (st.inverted ? -1 : 1)]);
    }
    return out;
}
/** Передаёт текущие дары и проклятия игре как глобальные эффекты. */
function pushEffects(ctx) {
    ctx.data.setEffects(toEffects(deltas(allLevels(ctx))));
}
// ---------------------------------------------------------------- экран
let windowGo = null;
let entryGo = null;
function destroy(go) {
    if (!go)
        return;
    try {
        cls(CORE, "UnityEngine.Object").method("Destroy", 1).invoke(go);
    }
    catch (_) { /* уже уничтожен */ }
}
function buildScreen(ctx, menu) {
    destroy(windowGo);
    destroy(entryGo);
    const screenH = kit.screenH;
    const RED = color(1, 0.45, 0.4, 1);
    // --- окно
    const win = node("ModKitCurses", kit.canvasTf);
    windowGo = win.go;
    win.rt.method("set_anchorMin").invoke(v2(0, 0));
    win.rt.method("set_anchorMax").invoke(v2(1, 1));
    win.rt.method("set_offsetMin").invoke(v2(0, 0));
    win.rt.method("set_offsetMax").invoke(v2(0, 0));
    image(win.go, 0.06, 0, 0, 1); // почти чёрный с красным оттенком
    const title = label(win.rt, "Проклятия и дары", 0, 100, LOGICAL_W, 80, 46, 4);
    tint(title, color(1, 0.55, 0.45, 1));
    const sub = label(win.rt, "Выбор проклятия", 0, 195, LOGICAL_W, 40, 22, 4);
    tint(sub, color(0.9, 0.7, 0.7, 1));
    button(win.rt, "X", LOGICAL_W - 100, 70, 80, 80, 54, [0, 0, 0, 0.01], () => win.go.method("SetActive").invoke(false));
    const info = label(win.rt, "Нажмите на плитку: уровень растёт, после максимума — сброс.", 40, screenH - 250, LOGICAL_W - 80, 150, 26, 4);
    tint(info, gold());
    const grid = node("grid", win.rt);
    place(grid.rt, 0, 0, LOGICAL_W, screenH);
    const sprites = cls("Assembly-CSharp", "Z_Database").field("SpriteList_AbilityPortrait").value;
    const XS = [162, 294, 426, 558];
    const draw = () => {
        const kids = grid.rt.method("get_childCount").invoke();
        for (let i = kids - 1; i >= 0; i--)
            destroy(grid.rt.method("GetChild").invoke(i).method("get_gameObject").invoke());
        TILES.forEach((t, i) => {
            const cx = XS[i % 4], cy = 330 + Math.floor(i / 4) * 200;
            const lv = levelOf(ctx, t.key);
            const n = node("tile", grid.rt);
            place(n.rt, cx - 60, cy - 60, 120, 120);
            const img = image(n.go, 1, 1, 1, 1);
            try {
                img.method("set_sprite").invoke(sprites.get(t.icon));
                img.method("set_preserveAspect").invoke(true);
            }
            catch (e) {
                ctx.log("иконка: " + e);
            }
            img.method("set_color").invoke(lv > 0 ? RED : color(0.45, 0.25, 0.25, 1));
            const btn = add(n.go, UI, "UnityEngine.UI.Button");
            btn.method("set_targetGraphic").invoke(img);
            const act = Il2Cpp.delegate(cls(CORE, "UnityEngine.Events.UnityAction"), () => {
                try {
                    const next = (levelOf(ctx, t.key) + 1) % (MAX_LEVEL + 1);
                    ctx.setSetting("lvl_" + t.key, next);
                    if (next > 0)
                        ctx.setSetting("lvl_" + t.partner, 0); // взаимное исключение
                    pushEffects(ctx);
                    info.method("set_text").invoke(Il2Cpp.string(describe(ctx, t)));
                    draw();
                }
                catch (e) {
                    ctx.log("клик по плитке: " + e);
                }
            });
            btn.method("get_onClick").invoke().method("AddListener").invoke(act);
            // точки уровня под плиткой
            for (let p = 0; p < MAX_LEVEL; p++) {
                const dot = node("dot", grid.rt);
                place(dot.rt, cx - 38 + p * 16, cy + 72, 9, 9);
                image(dot.go, ...(p < lv ? [1, 0.35, 0.3, 1] : [0.35, 0.2, 0.2, 1]));
            }
            const name = label(grid.rt, t.name, cx - 75, cy + 86, 150, 34, 16, 4);
            tint(name, color(0.95, 0.8, 0.8, 1));
        });
    };
    draw();
    win.go.method("SetActive").invoke(false);
    // --- невидимая кнопка поверх заголовка экрана исследований
    const field = menu.field("Obj_FieldResearch").value;
    const entry = node("ModKitCursesEntry", field.method("get_transform").invoke());
    entryGo = entry.go;
    // поле исследований смещено относительно центра холста — вычитаем это смещение
    const fpos = field.method("get_transform").invoke().method("get_anchoredPosition").invoke();
    placeAtScreenCenter(entry.rt, 360, 175 * screenH / 1640, 440, 90, [fpos.field("x").value, fpos.field("y").value]);
    const eImg = image(entry.go, 0, 0, 0, 0.01);
    const eBtn = add(entry.go, UI, "UnityEngine.UI.Button");
    eBtn.method("set_targetGraphic").invoke(eImg);
    const open = Il2Cpp.delegate(cls(CORE, "UnityEngine.Events.UnityAction"), () => {
        try {
            draw();
            win.go.method("SetActive").invoke(true);
        }
        catch (e) {
            ctx.log("открытие: " + e);
        }
    });
    eBtn.method("get_onClick").invoke().method("AddListener").invoke(open);
    ctx.onCleanup(() => { destroy(windowGo); destroy(entryGo); windowGo = entryGo = null; });
    ctx.log("экран «Проклятия и дары» создан");
}
ModKit.define({
    id: "curses",
    name: "Проклятия и дары",
    description: "Нажмите на заголовок «Исследование» (ДНК): плитки с даром и проклятием 1 к 2.",
    options: TILES.map(t => ({ key: "lvl_" + t.key, label: t.name, default: 0, min: 0, max: MAX_LEVEL, step: 1, hidden: true })),
    onEnable(ctx) {
        ctx.log("уровни при старте: " + JSON.stringify(allLevels(ctx)));
        // Дары и проклятия — глобальные эффекты игры: пересчитываются самой игрой (старт боя, выбор способности).
        pushEffects(ctx);
        ctx.after("V_Ability_Manager", "Start_Ability", 0, () => pushEffects(ctx));
    },
    onMainMenu(ctx, menu) {
        buildScreen(ctx, menu);
    },
});
