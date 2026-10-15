/**
 * Команды правки артефактов, комбинаций и синергий поверх таблиц игры. Каждая команда записывает откат в `cleanups`
 * (выполняется при выключении мода). Вызывать из `ctx.data.whenReady`.
 */
import { findAddedArtifact, tickHooks } from "./artifacts";
import { Table, ensureSprite, intList, type Cell } from "./data";
import { log } from "./log";

export interface ArtifactChanges {
    name?: string;
    /** Пары [ID эффекта, значение] — заменяют все эффекты записи. */
    effects?: Array<[number, number]>;
    /** Строки описания (без них текст остаётся прежним). */
    lines?: string[];
    /** Перенос в пул другой редкости: C, B, A, P, S. */
    rarity?: "C" | "B" | "A" | "P" | "S";
}

export interface ComboDef {
    name: string;
    effects: Array<[number, number]>;
    lines?: string[];
    /** ID существующей комбинации — образец (иконка и остальные колонки). Обязателен, по умолчанию 1. */
    cloneFrom?: number;
    /** ID навыков, требуемых для комбинации (до 3). Без этого берутся требования образца. */
    requires?: number[];
    /** ID комбинации, чью иконку взять (по умолчанию иконка образца). */
    icon?: number;
}

export interface SynergyDef {
    name: string;
    /** До 3 эффектов — столько пар в таблице синергий. */
    effects: Array<[number, number]>;
    lines?: string[];
    /** ID артефактов в синергии (до 5). */
    members?: number[];
    cloneFrom?: number;
    icon?: number;
}

type Undo = Array<() => void>;
const GRADES = ["C", "B", "A", "P", "S"];
const pad = (n: number) => String(n).padStart(2, "0");

/** Артефакты, запрещённые модами: игра наполняет пулы позже запуска мода и при старте боя, поэтому вычищаем периодически. */
const banned = new Set<number>();
tickHooks.push(() => { for (const id of banned) takeFromPools(id); });

/** Что убрано командой removeArtifact: ID -> сколько копий было в каждом пуле. */
const removed = new Map<number, Array<[string, number]>>();

/** Меняет ячейки строки и регистрирует откат. */
function setCells(t: Table, id: number, changes: Record<string, Cell>, undo: Undo): void {
    const i = t.indexOf(id);
    if (i < 0) throw new Error(`${t.name}: нет строки с ID ${id}`);
    const old: Array<[string, Cell]> = [];
    for (const [col, v] of Object.entries(changes)) old.push([col, t.set(i, col, v)]);
    undo.push(() => Il2Cpp.perform(() => {
        const k = t.indexOf(id);
        if (k >= 0) for (const [col, v] of old) t.set(k, col, v);
    }));
}

/** Эффекты и строки описания -> изменения ячеек. */
function textChanges(t: Table, effects: Array<[number, number]> | undefined, lines: string[] | undefined, maxEffects: number, maxLines: number, countCol?: string): Record<string, Cell> {
    const cols = new Set(t.columns);
    const out: Record<string, Cell> = {};
    if (effects) {
        if (effects.length > maxEffects) throw new Error(`${t.name}: эффектов не больше ${maxEffects}`);
        for (let n = 1; n <= maxEffects; n++) {
            const e = effects[n - 1];
            if (cols.has(`효과ID_${pad(n)}`)) out[`효과ID_${pad(n)}`] = e ? e[0] : 1;
            if (cols.has(`효과ID_${pad(n)}값`)) out[`효과ID_${pad(n)}값`] = e ? e[1] : 0;
        }
    }
    if (lines) {
        if (lines.length > maxLines) throw new Error(`${t.name}: строк описания не больше ${maxLines}`);
        for (let n = 1; n <= maxLines; n++) if (cols.has(`효과 설명줄${pad(n)}`)) out[`효과 설명줄${pad(n)}`] = lines[n - 1] ?? "`";
        if (countCol && cols.has(countCol)) out[countCol] = lines.length;
    }
    return out;
}

// ------------------------------------------------------------ артефакты: пулы выпадения

/** Списки, где ID артефакта может встречаться несколько раз (вес). «Drop» — рабочий пул текущего боя. */
const POOLS = [...GRADES.map(g => "List_ArtifactPossibleGrade_" + g), "List_ArtifactDrop"];

function poolName(p: string): string {
    return p.startsWith("List_ArtifactPossibleGrade_") ? p.slice(-1) : "Drop";
}

/** Вынимает все копии ID из пулов, возвращает сколько было где. */
function takeFromPools(id: number): Array<[string, number]> {
    const res: Array<[string, number]> = [];
    for (const p of POOLS) {
        let l: Il2Cpp.Object;
        try { l = intList(p); } catch (_) { continue; }
        let n = 0;
        while (l.method<boolean>("Remove").invoke(id)) n++;
        if (n) res.push([poolName(p), n]);
    }
    return res;
}

function putToPools(id: number, parts: Array<[string, number]>): void {
    for (const [name, n] of parts) {
        const l = intList(name === "Drop" ? "List_ArtifactDrop" : "List_ArtifactPossibleGrade_" + name);
        for (let i = 0; i < n; i++) l.method("Add").invoke(id);
    }
}

export function editArtifact(id: number, ch: ArtifactChanges, undo: Undo, owner: string): void {
    const t = new Table("Ability");
    const changes = textChanges(t, ch.effects, ch.lines, 9, 6, "효과 설명줄 개수");
    if (ch.name !== undefined) changes["이름"] = ch.name;
    setCells(t, id, changes, undo);
    const added = findAddedArtifact(id);
    if (added) { if (ch.name) added.def.name = ch.name; if (ch.lines) added.def.lines = ch.lines; }

    if (ch.rarity) {
        const was = takeFromPools(id);
        const total = was.filter(([n]) => n !== "Drop").reduce((a, [, n]) => a + n, 0) || 5;
        const parts: Array<[string, number]> = [[ch.rarity, total]];
        if (was.some(([n]) => n === "Drop")) parts.push(["Drop", was.find(([n]) => n === "Drop")![1]]);
        putToPools(id, parts);
        undo.push(() => Il2Cpp.perform(() => { takeFromPools(id); putToPools(id, was); }));
        log(`[${owner}] артефакт ${id}: редкость -> ${ch.rarity} (копий ${total})`);
    }
    log(`[${owner}] артефакт ${id} изменён`);
}

export function removeArtifact(id: number, undo: Undo, owner: string): void {
    const was = takeFromPools(id);
    banned.add(id);
    if (was.length) removed.set(id, was);
    else if (!removed.has(id)) removed.set(id, [["B", 5]]);     // пулы ещё пусты: при возврате положим 5 копий (обычный вес) — редкость уточните через editArtifact
    undo.push(() => Il2Cpp.perform(() => { restoreArtifact(id, owner); }));
    log(`[${owner}] артефакт ${id} убран из выпадения (${was.length ? was.map(([n, c]) => `${n}×${c}`).join(", ") : "пулы пока пусты — будет вычищен позже"})`);
}

export function restoreArtifact(id: number, owner: string): void {
    banned.delete(id);
    const was = removed.get(id);
    if (!was) { log(`[${owner}] restoreArtifact ${id}: он не был убран`); return; }
    takeFromPools(id);                       // не плодим дубликаты, если вернули руками
    putToPools(id, was);
    removed.delete(id);
    log(`[${owner}] артефакт ${id} возвращён в выпадение`);
}

// ------------------------------------------------------------ комбинации

export function editCombo(id: number, ch: { name?: string; effects?: Array<[number, number]>; lines?: string[]; requires?: number[] }, undo: Undo, owner: string): void {
    const t = new Table("MagicCom");
    const changes = textChanges(t, ch.effects, ch.lines, 9, 6);
    if (ch.name !== undefined) changes["이름"] = ch.name;
    if (ch.requires) {
        ["특성A레벨", "특성B레벨", "특성C레벨"].forEach((c, k) => { changes[c] = ch.requires![k] ?? 0; });
    }
    setCells(t, id, changes, undo);
    log(`[${owner}] комбинация ${id} изменена`);
}

export function addCombo(def: ComboDef, undo: Undo, owner: string): number {
    const t = new Table("MagicCom");
    const from = def.cloneFrom ?? 1;
    const src = t.indexOf(from);
    if (src < 0) throw new Error(`MagicCom: нет образца ${from}`);
    const row = t.cloneRow(src);
    const id = t.maxId() + 1;
    t.set(row, t.idColumn, id);
    ensureSprite("SpriteList_MagicComPortrait", id, def.icon ?? from);
    t.set(row, "이름", def.name);
    const changes = textChanges(t, def.effects, def.lines ?? def.effects.map(([e, v]) => `Эффект ${e}: ${v}`), 9, 6);
    for (const [c, v] of Object.entries(changes)) t.set(row, c, v);
    if (def.requires) ["특성A레벨", "특성B레벨", "특성C레벨"].forEach((c, k) => t.set(row, c, def.requires![k] ?? 0));
    undo.push(() => Il2Cpp.perform(() => {
        const ti = t.indexOf(id);
        if (ti === t.length - 1) t.removeLast();
        else log(`[${owner}] комбинацию ${id} нельзя убрать: после неё добавлены другие`);
    }));
    log(`[${owner}] комбинация «${def.name}»: ID ${id}, образец ${from}`);
    return id;
}

// ------------------------------------------------------------ синергии

const SLOTS = ["ID_01", "ID_02", "ID_03", "ID_04", "ID_05"];

function members(t: Table, i: number): number[] {
    return SLOTS.map(c => Number(t.get(i, c))).filter(v => v > 0);
}

function setMembers(t: Table, id: number, list: number[], undo: Undo): void {
    if (list.length > SLOTS.length) throw new Error(`Synergy: в синергии не больше ${SLOTS.length} артефактов`);
    const changes: Record<string, Cell> = { "개수": list.length };
    SLOTS.forEach((c, k) => { changes[c] = list[k] ?? 0; });
    setCells(t, id, changes, undo);
}

export function addSynergy(def: SynergyDef, undo: Undo, owner: string): number {
    const t = new Table("Synergy");
    const from = def.cloneFrom ?? 3;
    const src = t.indexOf(from);
    if (src < 0) throw new Error(`Synergy: нет образца ${from}`);
    const row = t.cloneRow(src);
    // первая «пустая» строка таблицы (имя «`») — свободный ID, иначе в конец; ID 99/100 — пустые заготовки
    const id = t.maxId() + 1;
    t.set(row, t.idColumn, id);
    ensureSprite("SpriteList_SynergyPortrait", id, def.icon ?? from);
    t.set(row, "이름", def.name);
    const changes = textChanges(t, def.effects, def.lines ?? def.effects.map(([e, v]) => `Эффект ${e}: ${v}`), 3, 3);
    for (const [c, v] of Object.entries(changes)) t.set(row, c, v);
    const list = def.members ?? [];
    t.set(row, "개수", list.length);
    SLOTS.forEach((c, k) => t.set(row, c, list[k] ?? 0));
    undo.push(() => Il2Cpp.perform(() => {
        const ti = t.indexOf(id);
        if (ti === t.length - 1) t.removeLast();
        else log(`[${owner}] синергию ${id} нельзя убрать: после неё добавлены другие`);
    }));
    log(`[${owner}] синергия «${def.name}»: ID ${id}, образец ${from}, артефактов ${list.length}`);
    return id;
}

export function removeSynergy(id: number, undo: Undo, owner: string): void {
    const t = new Table("Synergy");
    setMembers(t, id, [], undo);
    log(`[${owner}] синергия ${id} очищена (артефактов нет — не срабатывает)`);
}

export function synergyAddArtifact(synergyId: number, artifactId: number, undo: Undo, owner: string): void {
    const t = new Table("Synergy");
    const i = t.indexOf(synergyId);
    if (i < 0) throw new Error(`Synergy: нет строки ${synergyId}`);
    const list = members(t, i);
    if (list.includes(artifactId)) { log(`[${owner}] синергия ${synergyId}: артефакт ${artifactId} уже есть`); return; }
    if (list.length >= SLOTS.length) throw new Error(`Synergy ${synergyId}: нет свободных мест (максимум ${SLOTS.length})`);
    setMembers(t, synergyId, [...list, artifactId], undo);
    log(`[${owner}] синергия ${synergyId}: добавлен артефакт ${artifactId}`);
}

export function synergyRemoveArtifact(synergyId: number, artifactId: number, undo: Undo, owner: string): void {
    const t = new Table("Synergy");
    const i = t.indexOf(synergyId);
    if (i < 0) throw new Error(`Synergy: нет строки ${synergyId}`);
    const list = members(t, i);
    if (!list.includes(artifactId)) { log(`[${owner}] синергия ${synergyId}: артефакта ${artifactId} нет`); return; }
    setMembers(t, synergyId, list.filter(v => v !== artifactId), undo);
    log(`[${owner}] синергия ${synergyId}: убран артефакт ${artifactId}`);
}

// ------------------------------------------------------------ пакетные команды

/** Несколько комбинаций за один вызов. Возвращает ID по порядку. */
export function addCombos(defs: ComboDef[], undo: Undo, owner: string): number[] {
    return defs.map(d => addCombo(d, undo, owner));
}

/** Несколько синергий за один вызов. Возвращает ID по порядку. */
export function addSynergies(defs: SynergyDef[], undo: Undo, owner: string): number[] {
    return defs.map(d => addSynergy(d, undo, owner));
}
