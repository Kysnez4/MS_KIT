/**
 * Свои эффекты артефактов и комбинаций (customEffects).
 *
 * Игра обрабатывает ID эффектов (`효과ID_NN` / `효과ID_NN값` в таблицах Ability и MagicCom) своим кодом; неизвестный ID ничего не делает.
 * Здесь мы регистрируем обработчики для своих ID (>= 10000). После каждого пересчёта характеристик игрой
 * (`V_Ability_Manager.AbilityStatApply`) и раз в ~0,7 с мы смотрим, какие артефакты получены (`Z_Database.List_ArtifactObtain`)
 * и какие комбинации применены (`V_Ability_Manager.List_ApplyMagicComPossible`), берём значения эффектов из строк таблиц
 * и вызываем обработчики, которые правят поля героя (`UnitApply_*`) и переменные `Z_Database.AllyUnitVar_*`.
 *
 *
 * Эффекты бывают двух видов:
 *  1) `apply` — правка полей героя после пересчёта игры (для того, чего в игре нет);
 *  2) `expand` / глобальные эффекты (`setGlobalEffects`) — «настоящие» эффекты игры. На время вызова `AbilityStatApply` мы
 *     подставляем в колонки `효과ID_NN`/`효과ID_NN값` строк-источников (артефакты, класс, комбинации, синергии) пары [ID игры, значение],
 *     игра сама считает их своим кодом (все производные поля героя — тоже), а после вызова колонки возвращаются. Это точно
 *     повторяет штатную обработку, потому что обрабатывает её сама игра.
 *
 * Чтобы эффекты не накапливались, обработчики не пишут в поля напрямую: `hero.field(name).value` отдаёт БАЗУ (значение,
 * которое посчитала игра) и накапливает цель; запись в игру делает движок после всех обработчиков. Если игра пересчитала поле
 * (значение не равно тому, что записали мы), берётся новая база. Поля, которые больше не затрагиваются (артефакт потерян,
 * мод выключен), возвращаются к базе.
 */
import type { ModContext } from "./api";
import { Table, type Cell } from "./data";
import { log } from "./log";

/** Ссылка на числовое поле: чтение даёт базу (или уже накопленную цель), запись задаёт цель. */
export interface FieldRef {
    value: number;
}

/** Герой в бою (C_Battle_AllyUnit) глазами обработчика эффекта. */
export interface HeroView {
    /** Поле героя (instance или static) по имени, например `UnitApply_MagicBulletDamage`. */
    field(name: string): FieldRef;
    /** Статическая переменная Z_Database, например `AllyUnitVar_ItemObtainAreaU`. */
    db(name: string): FieldRef;
    /** Поле *= (1 + percent/100). */
    mulPercent(name: string, percent: number): void;
    /** Правка только на время показа окна характеристик (поле/статик героя или Z_Database); потом значение возвращается. */
    display(name: string, fn: (cur: number) => number): void;
    /** Поле += n. */
    add(name: string, n: number): void;
    /** Сам объект C_Battle_AllyUnit (для нестандартных вызовов). */
    readonly raw: Il2Cpp.Object;
}

export interface EffectDef {
    /** Числовой ID: >= 10000 (меньшие заняты игрой). В таблице пишется в `효과ID_NN`. */
    id: number;
    /** Название (для журнала). */
    name: string;
    /** Вызывается после каждого пересчёта для каждого источника героя (артефакт, класс, комбинация, синергия) с этим эффектом. `value` — число из `효과ID_NN값`. */
    apply?(hero: HeroView, value: number, ctx: ModContext): void;
    /**
     * Раскрывает эффект в пары [ID игры, значение], которые игра обработает сама при пересчёте (см. шапку файла).
     * Первая пара занимает слот эффекта в строке, остальные — свободные слоты (ID 0/1) строк-источников героя.
     */
    expand?(value: number): Array<[number, number]>;
}

interface Registered {
    def: EffectDef;
    ctx: ModContext;
}

const registry = new Map<number, Registered>();
let hooks: InvocationListener[] = [];
let lastMgr: NativePointer | null = null;     // V_Ability_Manager, увиденный в AbilityStatApply
let lastHero: Il2Cpp.Object | null = null;
let dirty = false;
/** Игра вызвала AbilityStatApply с прошлого прохода: все значения пересчитаны с нуля, база = текущее значение. */
let recomputed = false;
let tick = 0;
let inDisplay = false;
const tempRestore: Array<() => void> = [];
let lastSig = "";
let logBudget = 80;

const pad = (n: number) => String(n).padStart(2, "0");
const close = (a: number, b: number) => Math.abs(a - b) <= 1e-4 * Math.max(1, Math.abs(a), Math.abs(b));

interface Mem { f: Il2Cpp.Field<number>; base: number; set: number; inst: boolean; heroKey: string }
/** Что мы записали в игру: ключ поля -> (база, записанное значение). */
const mem = new Map<string, Mem>();

// ------------------------------------------------------------------ герой

function findHero(mgr: Il2Cpp.Object): Il2Cpp.Object | null {
    const field = mgr.field<Il2Cpp.Object>("Obj_FieldAllyUnit").value;
    if (field.isNull()) return null;
    const tf = field.method<Il2Cpp.Object>("get_transform").invoke();
    const n = tf.method<number>("get_childCount").invoke();
    const type = Il2Cpp.domain.assembly("Assembly-CSharp").image.class("C_Battle_AllyUnit").type.object;
    for (let i = 0; i < n; i++) {
        const go = tf.method<Il2Cpp.Object>("GetChild").invoke(i).method<Il2Cpp.Object>("get_gameObject").invoke();
        const comp = go.method<Il2Cpp.Object>("GetComponent", 1).invoke(type);
        if (comp && !comp.isNull()) return comp;
    }
    return null;
}

function zdb(): Il2Cpp.Class {
    return Il2Cpp.domain.assembly("Assembly-CSharp").image.class("Z_Database");
}

function readIntList(l: Il2Cpp.Object): number[] {
    if (l.isNull()) return [];
    const size = l.field<number>("_size").value;
    const items = l.field<Il2Cpp.Array<number>>("_items").value;
    const out: number[] = [];
    for (let i = 0; i < size; i++) out.push(Number(items.get(i)));
    return out;
}

// ------------------------------------------------------------------ один проход

interface Slot { f: Il2Cpp.Field<number>; base: number; target: number; isInt: boolean; inst: boolean; key: string }

function makeView(hero: Il2Cpp.Object, slots: Map<string, Slot>, fullRecompute: boolean): HeroView {
    const heroKey = hero.handle.toString();
    const slotFor = (key: string, get: () => Il2Cpp.Field<number>, inst: boolean): Slot => {
        let s = slots.get(key);
        if (s) return s;
        const f = get();
        const cur = Number(f.value);
        const m = mem.get(key);
        // значение всё ещё наше -> прежняя база. Иначе:
        //  * поле героя (inst): игра пересчитала его целиком -> база = текущее значение;
        //  * статическая переменная (AllyUnitVar_*): игра сама сдвигает её на ±10 и пересчитывает с нуля только в AbilityStatApply ->
        //    сдвиг переносим в базу (иначе наша добавка копится), а после полного пересчёта база = текущее.
        let base = cur;
        if (m && m.heroKey === heroKey) {
            if (close(cur, m.set)) base = m.base;
            else if (!inst && !fullRecompute) base = m.base + (cur - m.set);
        }
        s = { f, base, target: base, isInt: f.type.name === "System.Int32", inst, key };
        slots.set(key, s);
        return s;
    };
    const ref = (s: Slot): FieldRef => ({
        get value() { return s.target; },
        set value(v: number) { s.target = s.isInt ? Math.round(v) : v; },
    });
    const view: HeroView = {
        raw: hero,
        field(name) {
            const cf = hero.class.tryField<number>(name);
            if (!cf) throw new Error(`у героя нет поля ${name}`);
            if (cf.isStatic) return ref(slotFor("S:" + name, () => hero.class.field<number>(name), false));
            return ref(slotFor("H" + heroKey + ":" + name, () => hero.field<number>(name), true));
        },
        db(name) {
            if (!zdb().tryField(name)) throw new Error(`в Z_Database нет поля ${name}`);
            return ref(slotFor("D:" + name, () => zdb().field<number>(name), false));
        },
        mulPercent(name, p) { const r = view.field(name); r.value *= 1 + p / 100; },
        display(name, fn) {
            if (!inDisplay) return;
            let f: Il2Cpp.Field<number> | null = hero.class.tryField<number>(name);
            if (f) f = f.isStatic ? hero.class.field<number>(name) : hero.field<number>(name);
            else f = zdb().tryField<number>(name);
            if (!f) return;
            const old = Number(f.value), ff = f;
            ff.value = fn(old);
            tempRestore.push(() => { ff.value = old; });
        },
        add(name, n) { const r = view.field(name); r.value += n; },
    };
    return view;
}

// ------------------------------------------------------------------ источники эффектов героя

const tables = new Map<string, Table>();
function table(name: string): Table {
    let t = tables.get(name);
    if (!t) tables.set(name, t = new Table(name));
    return t;
}

interface GameSource { table: string; id: number; label: string; spill?: boolean }

/** Индекс строки по ID (кеш на таблицу: полный перебор через IL2CPP на каждый пересчёт тормозит меню выбора заклинаний). */
const rowCache = new Map<string, { len: number; map: Map<number, number> }>();
function rowOf(t: Table, id: number): number {
    const len = t.length;
    let c = rowCache.get(t.name);
    if (!c || c.len !== len) {
        const map = new Map<number, number>();
        const col = t.idColumn;
        for (let i = 0; i < len; i++) { const v = t.get(i, col); if (typeof v === "number" && !map.has(v)) map.set(v, i); }
        rowCache.set(t.name, c = { len, map });
    }
    return c.map.get(id) ?? -1;
}
let synLog = "";

interface SynRow { id: number; need: number; ids: number[] }
let synCache: { len: number; rows: SynRow[] } | null = null;
/** Состав синергий из таблицы Synergy (кеш по длине таблицы). */
function synergyRows(): SynRow[] {
    const t = table("Synergy");
    if (synCache && synCache.len === t.length) return synCache.rows;
    const rows: SynRow[] = [];
    for (let i = 0; i < t.length; i++) {
        const id = t.get(i, t.idColumn);
        if (typeof id !== "number") continue;
        const ids: number[] = [];
        for (let k = 1; k <= 5; k++) { const v = Number(t.get(i, `ID_${pad(k)}`)); if (v > 0) ids.push(v); }
        rows.push({ id, need: Number(t.get(i, "개수")), ids });
    }
    synCache = { len: t.length, rows };
    return rows;
}

/**
 * Все строки таблиц, чьи эффекты сейчас действуют на героя: полученные артефакты (Ability), класс (Class), применённые комбинации (MagicCom),
 * включённые синергии (Synergy: `List_ApplySynergyON[ID] != 0`) и изученные исследования (Ability; нужны как запасные слоты).
 */
function gameSources(): GameSource[] {
    const res: GameSource[] = [];
    const guard = (what: string, fn: () => void) => { try { fn(); } catch (e) { if (logBudget-- > 0) log(`[effects] ${what}: ${e}`); } };
    guard("класс", () => {
        const id = Number(zdb().field<number>("Game_BattleClassID").value);
        if (id > 0) res.push({ table: "Class", id, label: `класс ${id}` });
    });
    // синергия действует, когда у героя есть не меньше `개수` её артефактов (считаем сами: игра ведёт Array_SynergyObtain только при штатном получении артефакта)
    guard("синергии", () => {
        const have = new Set(readIntList(zdb().field<Il2Cpp.Object>("List_ArtifactObtain").value));
        const act: number[] = [];
        if (have.size) for (const syn of synergyRows()) {
            const n = syn.ids.filter(a => have.has(a)).length;
            if (syn.ids.length && syn.need > 0 && n >= syn.need) { act.push(syn.id); res.push({ table: "Synergy", id: syn.id, label: `синергия ${syn.id}` }); }
        }
        const sig = act.join(",");
        if (sig !== synLog) { synLog = sig; if (logBudget-- > 0) log(`[effects] включённые синергии: ${sig || "нет"}`); }
    });
    if (lastMgr) guard("комбинации", () => {
        const mgr = new Il2Cpp.Object(lastMgr!);
        if (mgr.class.name !== "V_Ability_Manager") { lastMgr = null; throw new Error("устаревший указатель менеджера"); }   // объект уже уничтожен, память занята другим
        for (const c of readIntList(mgr.field<Il2Cpp.Object>("List_ApplyMagicComPossible").value)) res.push({ table: "MagicCom", id: c, label: `комбинация ${c}` });
    });
    guard("артефакты", () => {
        for (const a of readIntList(zdb().field<Il2Cpp.Object>("List_ArtifactObtain").value)) res.push({ table: "Ability", id: a, label: `артефакт ${a}` });
    });
    guard("исследования", () => {
        for (const a of readIntList(zdb().field<Il2Cpp.Object>("List_AllyAbilityLearn").value)) if (a >= 261 && a <= 282) res.push({ table: "Ability", id: a, label: `исследование ${a}`, spill: true });
    });
    return res;
}

// ------------------------------------------------------------------ подстановка настоящих эффектов игры на время пересчёта

/** Глобальные эффекты модов (не привязаны к артефакту): владелец -> пары [ID игры, значение]. Одинаковые ID суммируются. */
const globals = new Map<string, Array<[number, number]>>();
interface Patch { t: Table; row: number; col: string; old: NativePointer; mine: NativePointer; list: NativePointer; pin: Il2Cpp.GCHandle | null }
let patches: Patch[] = [];
let lastPatchSig = "";

/**
 * План подстановки: что и куда писать. Зависит только от набора источников героя и от реестра эффектов, поэтому строится один раз
 * и остаётся в таблицах, пока ключ не изменится (получен артефакт, применена комбинация, изменён реестр). Подстановка не откатывается
 * после каждого `AbilityStatApply` — откат только при смене плана, пропаже героя, снятии хуков или чужой правке ячеек.
 */
interface Plan {
    key: string;
    /** Эффекты с `apply` и `expand` одновременно: из-за подстановки их ID в ячейках не видно, `collect` берёт их отсюда. */
    applied: Array<[number, number, string]>;
}
let plan: Plan | null = null;
let planVersion = 0;        // растёт при любом изменении реестра эффектов / глобальных эффектов

/** Сбросить план и вернуть таблицы к исходным значениям (изменился реестр или глобальные эффекты). */
function invalidatePlan(): void {
    planVersion++;
    try { unpatchRows(); } catch (_) { /* таблицы нет */ }
}

function putCell(t: Table, row: number, col: string, v: number, lists: Map<Table, NativePointer>): void {
    // прежний боксированный объект возвращаем указателем (точно тот же); пока он вне таблицы, держим его от сборщика мусора
    const old = t.getRaw(row, col);
    let list = lists.get(t);
    if (!list) lists.set(t, list = t.list.handle);
    t.set(row, col, v);
    patches.push({ t, row, col, old, mine: t.getRaw(row, col), list, pin: old.isNull() ? null : new Il2Cpp.Object(old).ref(true) });
}

/** Наши ячейки на месте: игра/другой мод их не подменяли, таблица не перезагружалась. */
function planIntact(): boolean {
    try {
        for (const p of patches) if (!p.t.getRaw(p.row, p.col).equals(p.mine)) return false;
        return true;
    } catch (_) { return false; }
}

function buildPlan(srcs: GameSource[], key: string): void {
    const t0 = Date.now();
    const writes: Array<[Table, number, string, number]> = [];
    const applied: Array<[number, number, string]> = [];
    const free: Array<[Table, number, number]> = [];       // свободные слоты прочих источников
    const safe: Array<[Table, number, number]> = [];       // свободные слоты изученных исследований: их игра читает при каждом пересчёте
    const extra: Array<[number, number, string]> = [];
    const sum = new Map<number, number>();
    for (const list of globals.values()) for (const [id, v] of list) sum.set(id, (sum.get(id) ?? 0) + v);
    const scanSource = (s: GameSource) => {
        const bucket = s.spill ? safe : free;
        const t = table(s.table), row = rowOf(t, s.id);
        if (row < 0) return;
        for (let n = 1; n <= 9; n++) {
            let id: number, val: number;
            try { id = Number(t.get(row, `효과ID_${pad(n)}`)); val = Number(t.get(row, `효과ID_${pad(n)}값`)); } catch (_) { break; }
            if (id < 2) { if (val === 0) bucket.push([t, row, n]); continue; }
            const def = registry.get(id)?.def;
            if (!def?.expand) continue;
            if (def.apply) applied.push([id, val, s.label]);
            const pairs = def.expand(val);
            if (!pairs.length) { writes.push([t, row, `효과ID_${pad(n)}`, 1], [t, row, `효과ID_${pad(n)}값`, 0]); continue; }
            writes.push([t, row, `효과ID_${pad(n)}`, pairs[0][0]], [t, row, `효과ID_${pad(n)}값`, pairs[0][1]]);
            for (let k = 1; k < pairs.length; k++) extra.push([pairs[k][0], pairs[k][1], `${s.label}:${id}`]);
        }
    };
    for (const s of srcs) if (!s.spill) scanSource(s);
    const need = () => extra.length + [...sum.values()].filter(v => v !== 0).length;
    // слоты исследований надёжнее всего (в бою класс/артефакт не всегда читается игрой так, как ожидалось): сначала они, читаем по мере надобности
    for (const s of srcs) { if (!s.spill) continue; if (safe.length >= need()) break; scanSource(s); }
    for (const [id, v] of sum) if (v !== 0) extra.push([id, v, "глобальный"]);
    for (const [id, v, from] of extra) {
        const slot = safe.shift() ?? free.shift();
        if (!slot) { if (logBudget-- > 0) log(`[effects] нет свободного слота эффекта для ${id}=${v} (${from}) — пропущено`); continue; }
        const [t, row, n] = slot;
        writes.push([t, row, `효과ID_${pad(n)}`, id], [t, row, `효과ID_${pad(n)}값`, v]);
    }
    const lists = new Map<Table, NativePointer>();
    for (const [t, row, col, v] of writes) putCell(t, row, col, v, lists);
    plan = { key, applied };
    const dt = Date.now() - t0;
    if (dt > 15 && logBudget-- > 0) log(`[effects] подстановка заняла ${dt} мс`);
    const sig = extra.map(e => `${e[0]}=${e[1]}(${e[2]})`).join(", ");
    if (sig !== lastPatchSig) { lastPatchSig = sig; if (logBudget-- > 0) log(`[effects] подстановка перед пересчётом: ${sig || "-"} (свободных слотов осталось ${free.length + safe.length})`); }
}

function patchRows(): void {
    if (!registry.size && !globals.size) return;
    const srcs = gameSources();
    const key = `${planVersion}|${srcs.map(s => `${s.table}:${s.id}`).join(",")}`;
    if (plan && plan.key === key && planIntact()) return;      // план не менялся, ячейки уже подставлены
    unpatchRows();
    buildPlan(srcs, key);
}

function unpatchRows(): void {
    plan = null;
    while (patches.length) {
        const p = patches.pop()!;
        try { if (p.t.list.handle.equals(p.list)) p.t.setRaw(p.row, p.col, p.old); }      // таблицу перезагрузили — писать в новую нечего
        catch (e) { if (logBudget-- > 0) log(`[effects] откат ${p.t.name}#${p.row}.${p.col}: ${e}`); }
        finally { try { p.pin?.free(); } catch (_) { /* */ } }
    }
}

/** Эффекты героя: [ID эффекта, значение, откуда]. Пустые слоты (ID 0 и 1) и неизвестные ID пропускаются. */
function collect(): Array<[number, number, string]> {
    const res: Array<[number, number, string]> = [];
    const scan = (t: Table, rowId: number, label: string) => {
        const row = rowOf(t, rowId);
        if (row < 0) return;
        for (let n = 1; n <= 9; n++) {
            let id: number;
            try { id = Number(t.get(row, `효과ID_${pad(n)}`)); } catch (_) { break; }
            if (!(id >= 2) || !registry.get(id)?.def.apply) continue;
            res.push([id, Number(t.get(row, `효과ID_${pad(n)}값`)), label]);
        }
    };
    for (const s of gameSources()) scan(table(s.table), s.id, s.label);
    if (plan) res.push(...plan.applied.filter(([id]) => registry.get(id)?.def.apply));     // их ID в подставленных ячейках не видны
    return res;
}

/** Возвращает поля, которых эффекты больше не касаются, к базе. */
function revertUntouched(touched: Map<string, Slot>, heroKey: string): void {
    for (const [key, m] of [...mem]) {
        if (touched.has(key)) continue;
        mem.delete(key);
        if (m.inst && m.heroKey !== heroKey) continue;           // старый герой — объекта уже может не быть
        try { if (close(Number(m.f.value), m.set)) m.f.value = m.base; } catch (_) { /* объект уничтожен */ }
    }
}

function runPass(hero: Il2Cpp.Object | null): void {
    if (!hero) {
        unpatchRows();                                  // боя нет: таблицы возвращаем к исходным
        for (const [k, m] of [...mem]) if (m.inst) mem.delete(k);       // герой исчез (конец боя) — записи по нему устарели
        return;
    }
    const heroKey = hero.handle.toString();
    const slots = new Map<string, Slot>();
    const fullRecompute = recomputed;
    recomputed = false;
    const view = makeView(hero, slots, fullRecompute);
    const found = registry.size ? collect() : [];
    const sig = found.map(([id, v, src]) => `${src}:${id}=${v}`).join(", ");
    if (sig !== lastSig) {
        lastSig = sig;
        if (logBudget-- > 0) log(`[effects] активные эффекты героя: ${sig || "нет"}`);
    }
    for (const [id, value, src] of found) {
        const r = registry.get(id)!;
        try { r.def.apply!(view, value, r.ctx); }
        catch (e) { if (logBudget-- > 0) log(`[effects] ${id} «${r.def.name}» (${src}): ${e}`); }
    }
    for (const s of slots.values()) {
        const cur = Number(s.f.value);
        if (!close(cur, s.target)) {
            s.f.value = s.target;
            if (logBudget-- > 0) log(`[effects] ${s.key.replace(/^H\d+:/, "hero.")}: ${cur} -> ${s.target} (база ${s.base})`);
        }
        mem.set(s.key, { f: s.f, base: s.base, set: Number(s.f.value), inst: s.inst, heroKey });
    }
    revertUntouched(slots, heroKey);
}

// ------------------------------------------------------------------ хуки (один набор на всех)

function attachHooks(): void {
    if (hooks.length) return;
    const img = Il2Cpp.domain.assembly("Assembly-CSharp").image;
    const upd = img.class("C_Battle_Manager").method("Update", 0);
    const stat = img.class("V_Ability_Manager").method("AbilityStatApply", 1);
    hooks.push(Interceptor.attach(upd.virtualAddress, {
        onEnter(args) {
            if (!registry.size && !mem.size && !patches.length) return;
            if (!dirty && ++tick % 45 !== 0) return;                  // раз в ~0,7 с или сразу после пересчёта игрой
            dirty = false;
            try {
                const hero = findHero(new Il2Cpp.Object(args[0]));
                if (hero && (!lastHero || !lastHero.handle.equals(hero.handle))) log(`[effects] герой найден (${hero.handle})`);
                if (!hero) lastMgr = null;
                lastHero = hero;
                runPass(hero);
            } catch (e) { if (logBudget-- > 0) log(`[effects] проход: ${e}`); }
        },
    }));
    // менеджер способностей берём из его Update: в AbilityStatApply указатель this оказывался чужим (ошибка «устаревший указатель менеджера»)
    const mUpd = img.class("V_Ability_Manager").tryMethod("Update", 0);
    if (mUpd) hooks.push(Interceptor.attach(mUpd.virtualAddress, { onEnter(args) { lastMgr = args[0]; } }));
    // окно характеристик (пауза) читает поля героя в момент вызова Display_BattleStatus: применяем эффекты прямо перед ним,
    // иначе оно показывает значения игры, которые она успела записать после нашего прохода
    const disp = img.class("C_Battle_AllyUnit").tryMethod("Display_BattleStatus", 0);
    if (disp) hooks.push(Interceptor.attach(disp.virtualAddress, {
        onEnter(args) {
            if (!registry.size) return;
            inDisplay = true;
            try { runPass(new Il2Cpp.Object(args[0])); } catch (e) { if (logBudget-- > 0) log(`[effects] перед окном характеристик: ${e}`); }
            inDisplay = false;
        },
        onLeave() { while (tempRestore.length) { try { tempRestore.pop()!(); } catch (_) { /* */ } } },
    }));
    // игра периодически пересчитывает характеристики героя (Unit_StatBuff): сразу после этого возвращаем наши значения
    const buff = img.class("C_Battle_AllyUnit").tryMethod("Unit_StatBuff", 0);
    if (buff) hooks.push(Interceptor.attach(buff.virtualAddress, { onLeave() { dirty = true; } }));
    hooks.push(Interceptor.attach(stat.virtualAddress, {
        onEnter() { try { patchRows(); } catch (e) { unpatchRows(); if (logBudget-- > 0) log(`[effects] подстановка: ${e}`); } },
        onLeave() { dirty = true; recomputed = true; },       // игра пересчитала характеристики: применяем наши эффекты на ближайшем кадре
    }));
    log("[effects] хуки установлены");
}

function detachHooks(): void {
    for (const h of hooks) h.detach();
    hooks = [];
    unpatchRows();
    lastMgr = null; lastHero = null; lastSig = "";
    log("[effects] хуки сняты");
}

// ------------------------------------------------------------------ API для core.ts

export function defineEffect(def: EffectDef, ctx: ModContext, undo: Array<() => void>): void {
    if (!Number.isInteger(def.id) || def.id < 10000) throw new Error(`defineEffect: ID должен быть целым >= 10000 (получено ${def.id}); меньшие ID занимает игра`);
    if (typeof def.apply !== "function" && typeof def.expand !== "function") throw new Error(`defineEffect ${def.id}: нужна функция apply или expand`);
    const ex = registry.get(def.id);
    if (ex) throw new Error(`defineEffect: ID ${def.id} уже зарегистрирован («${ex.def.name}», мод ${ex.ctx.id})`);
    registry.set(def.id, { def, ctx });
    invalidatePlan();
    attachHooks();
    dirty = true;
    log(`[effects] эффект ${def.id} «${def.name}» зарегистрирован (мод ${ctx.id})`);
    undo.push(() => Il2Cpp.perform(() => {
        registry.delete(def.id);
        invalidatePlan();
        try { runPass(lastHero); } catch (_) { /* героя нет */ }      // вернуть поля к базе
        if (!registry.size && !globals.size) detachHooks();
        log(`[effects] эффект ${def.id} снят`);
    }));
}

/**
 * Глобальные эффекты мода: пары [ID эффекта игры, значение], которые действуют на героя, как если бы они стояли в строке артефакта.
 * Игра считает их сама при пересчёте характеристик (точно как штатные); одинаковые ID разных модов суммируются.
 * Пустой список снимает эффекты. Вступают в силу при следующем пересчёте игры (старт боя, выбор способности).
 */
export function setGlobalEffects(owner: string, pairs: Array<[number, number]>, undo: Array<() => void>): void {
    const had = globals.has(owner);
    if (pairs.length) globals.set(owner, pairs.map(([i, v]) => [i, v] as [number, number])); else globals.delete(owner);
    invalidatePlan();
    if (pairs.length) attachHooks();
    if (!had && pairs.length) undo.push(() => Il2Cpp.perform(() => { globals.delete(owner); invalidatePlan(); if (!registry.size && !globals.size) detachHooks(); }));
    log(`[effects] глобальные эффекты ${owner}: ${pairs.map(p => `${p[0]}=${p[1]}`).join(", ") || "нет"}`);
}

// ------------------------------------------------------------------ встроенные эффекты

/** Значения в процентах, если не сказано иное. Положительное число всегда «лучше для игрока». */
export const BUILTIN_EFFECTS: EffectDef[] = [
    {
        id: 10001, name: "Урон магической пули %",
        apply: (h, v) => { h.mulPercent("UnitApply_MagicBulletDamage", v); h.display("AllyUnitVar_MagicDamageRatioU", x => x + v); },
    },
    {
        id: 10002, name: "Перезарядка заклинаний −%",
        // UnitApply_MagicCooltimeRatio — множитель времени перезарядки (0.88 = быстрее на 12%), меньше — лучше
        apply: (h, v) => { const r = h.field("UnitApply_MagicCooltimeRatio"); r.value = Math.max(0.05, r.value * (1 - v / 100)); h.display("UnitPrint_MagicCooltimeRatio", x => x * (1 - v / 100)); },
    },
    {
        id: 10003, name: "Снарядов магической пули +N, молний +N",
        apply: (h, v) => { h.add("UnitApply_MagicBulletNum", v); h.add("UnitApply_ThunderNum", v); },
    },
    {
        id: 10004, name: "Радиус сбора предметов %",
        apply(h, v) {
            // растим только кеш героя (UnitApply_ItemObtainArea); статик AllyUnitVar_ItemObtainAreaU игра сдвигает сама,
            // поэтому его правим лишь на время показа окна — иначе добавка копится
            // само поле UnitApply_ItemObtainArea не трогаем: игра сама масштабирует его по статику, наша правка шла в рост ×1.3 за проход (лаги)
            h.display("AllyUnitVar_ItemObtainAreaU", x => x + v);
        },
    },
];

export function useBuiltinEffects(ctx: ModContext, undo: Array<() => void>): void {
    for (const e of BUILTIN_EFFECTS) {
        if (registry.has(e.id)) continue;               // уже подключены другим модом
        defineEffect(e, ctx, undo);
    }
}
