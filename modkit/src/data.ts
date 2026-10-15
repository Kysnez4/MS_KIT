/**
 * Доступ к игровым таблицам Z_Database.Dictionary_* (List<Dictionary<string, object>>).
 * Читает и пишет структуры IL2CPP напрямую через поля, не полагаясь на generic-методы,
 * которые компилятор мог вырезать из сборки.
 *
 * Все строки одной таблицы создаются CSV-ридером с одинаковым порядком колонок, поэтому
 * номер записи Dictionary для колонки одинаков во всех строках — он вычисляется один раз.
 */

export type Cell = number | boolean | string | null;
export type Row = Record<string, Cell>;

const ENTRY_NEXT = 4;
const ENTRY_KEY = 8;
const ENTRY_VALUE = 16;
const ENTRY_SIZE = 24;
const ARRAY_DATA = 0x20;

function db(): Il2Cpp.Class {
    return Il2Cpp.domain.assembly("Assembly-CSharp").image.class("Z_Database");
}

function str(o: Il2Cpp.Object): string {
    return o.method<Il2Cpp.String>("ToString").invoke().content ?? "";
}

/** Имя класса по указателю на klass (кеш: узнать имя через IL2CPP дорого, а вызывается на каждую ячейку). */
const typeNames = new Map<string, string>();
function typeOf(obj: NativePointer): string {
    const k = obj.readPointer().toString();
    let n = typeNames.get(k);
    if (n === undefined) typeNames.set(k, n = new Il2Cpp.Object(obj).class.name);
    return n;
}

/** Читает значение из боксированного объекта прямо из памяти (данные лежат за заголовком объекта, со смещения 0x10). */
function unbox(ptr: NativePointer): Cell {
    if (ptr.isNull()) return null;
    const v = ptr.add(16);
    switch (typeOf(ptr)) {
        case "Int32": return v.readS32();
        case "Int64": return Number(v.readS64());
        case "Int16": return v.readS16();
        case "Byte": return v.readU8();
        case "UInt32": return v.readU32();
        case "Single": return Number(v.readFloat().toPrecision(9));   // как ToString("R"): без хвоста float -> double
        case "Double": return v.readDouble();
        case "Boolean": return v.readU8() !== 0;
        case "String": return new Il2Cpp.String(ptr).content;
        default: return `<${typeOf(ptr)}>`;
    }
}

/** Упаковывает значение; числовой тип повторяет тип прежнего значения, чтобы игра не споткнулась о приведение. */
function box(value: Cell, like: NativePointer): NativePointer {
    const type = like.isNull() ? "" : typeOf(like);
    if (value === null) return NULL;
    if (typeof value === "string") return Il2Cpp.string(value).handle;
    if (typeof value === "boolean") return Il2Cpp.boxed(value).handle;
    switch (type) {
        case "Int32": return Il2Cpp.boxed(Math.round(value), "int32").handle;
        case "Int64": return Il2Cpp.boxed(Math.round(value), "int64").handle;
        case "String": return Il2Cpp.string(String(value)).handle;
        default: return Il2Cpp.boxed(value).handle;
    }
}

interface Offsets { items: number; size: number; entries: number; count: number }
let offs: Offsets | null = null;

export class Table {
    private cols: Map<string, number> | null = null;
    private fld: Il2Cpp.Field<Il2Cpp.Object> | null = null;
    /** Кеш: номер строки -> начало массива записей её Dictionary (сбрасывается при смене списка и изменении таблицы). */
    private bases = new Map<number, { dict: NativePointer; base: NativePointer }>();
    private basesList: NativePointer = NULL;

    constructor(readonly name: string) {}

    private listPtr(): NativePointer {
        const f = this.fld ??= db().field<Il2Cpp.Object>("Dictionary_" + this.name);
        const h = f.value.handle;
        if (h.isNull()) throw new Error(`таблица ${this.name} ещё не загружена`);
        if (!offs) {
            const lc = new Il2Cpp.Object(h).class;
            offs = { items: lc.field("_items").offset, size: lc.field("_size").offset, entries: 0, count: 0 };
        }
        return h;
    }

    get list(): Il2Cpp.Object {
        return new Il2Cpp.Object(this.listPtr());
    }

    get length(): number {
        return this.listPtr().add(offs!.size).readS32();
    }

    private invalidate(): void {
        this.bases.clear();
    }

    /** Начало массива записей Dictionary строки i (прямые указатели вместо цепочки вызовов IL2CPP). */
    private entries(i: number): NativePointer {
        const l = this.listPtr();
        if (!l.equals(this.basesList)) { this.bases.clear(); this.basesList = l; }
        const size = l.add(offs!.size).readS32();
        if (i < 0 || i >= size) throw new Error(`${this.name}: нет строки ${i}`);
        const dict = l.add(offs!.items).readPointer().add(ARRAY_DATA + i * Process.pointerSize).readPointer();
        const hit = this.bases.get(i);
        if (hit && hit.dict.equals(dict)) return hit.base;         // строку не подменяли (cloneRow/removeLast в другом экземпляре Table)
        if (!offs!.entries) {
            const dc = new Il2Cpp.Object(dict).class;
            offs!.entries = dc.field("_entries").offset;
            offs!.count = dc.field("_count").offset;
        }
        const base = dict.add(offs!.entries).readPointer().add(ARRAY_DATA);
        this.bases.set(i, { dict, base });
        return base;
    }

    private dict(i: number): Il2Cpp.Object {
        const l = this.listPtr();
        if (i < 0 || i >= this.length) throw new Error(`${this.name}: нет строки ${i}`);
        return new Il2Cpp.Object(l.add(offs!.items).readPointer().add(ARRAY_DATA + i * Process.pointerSize).readPointer());
    }

    /** Имена колонок (по порядку). */
    get columns(): string[] {
        return [...this.columnMap().keys()];
    }

    private columnMap(): Map<string, number> {
        if (this.cols) return this.cols;
        const d = this.dict(0);
        const count = d.field<number>("_count").value;
        const base = d.field<Il2Cpp.Array<any>>("_entries").value.handle.add(ARRAY_DATA);
        const map = new Map<string, number>();
        for (let i = 0; i < count; i++) {
            const e = base.add(i * ENTRY_SIZE);
            if (e.add(ENTRY_NEXT).readS32() < -1) continue;
            const k = e.add(ENTRY_KEY).readPointer();
            if (!k.isNull()) map.set(new Il2Cpp.String(k).content ?? "", i);
        }
        return (this.cols = map);
    }

    private valueAt(i: number, column: string): NativePointer {
        const idx = this.columnMap().get(column);
        if (idx === undefined) throw new Error(`${this.name}: нет колонки «${column}»`);
        return this.entries(i).add(idx * ENTRY_SIZE + ENTRY_VALUE);
    }

    get(i: number, column: string): Cell {
        return unbox(this.valueAt(i, column).readPointer());
    }

    /** Указатель на боксированное значение ячейки (без распаковки). Для отката: см. setRaw. */
    getRaw(i: number, column: string): NativePointer {
        return this.valueAt(i, column).readPointer();
    }

    /** Кладёт в ячейку готовый боксированный объект. */
    setRaw(i: number, column: string, ptr: NativePointer): void {
        this.valueAt(i, column).writePointer(ptr);
    }

    /** Меняет значение существующей колонки. Возвращает прежнее значение (для отката). */
    set(i: number, column: string, value: Cell): Cell {
        const at = this.valueAt(i, column);
        const old = unbox(at.readPointer());
        at.writePointer(box(value, at.readPointer()));
        return old;
    }

    row(i: number): Row {
        const r: Row = {};
        for (const c of this.columns) r[c] = this.get(i, c);
        return r;
    }

    all(): Row[] {
        const out: Row[] = [];
        for (let i = 0; i < this.length; i++) out.push(this.row(i));
        return out;
    }

    /** Первая колонка таблицы — идентификатор (ID / ID001 / ...). */
    get idColumn(): string {
        return this.columns[0];
    }

    /** Индекс строки по значению ID, либо -1. */
    indexOf(id: number): number {
        const col = this.idColumn;
        for (let i = 0; i < this.length; i++) if (this.get(i, col) === id) return i;
        return -1;
    }

    maxId(): number {
        const col = this.idColumn;
        let m = 0;
        for (let i = 0; i < this.length; i++) {
            const v = this.get(i, col);
            if (typeof v === "number") m = Math.max(m, v); // в таблицах бывают строки-заголовки с текстовым ID
        }
        return m;
    }

    /** Копирует строку в конец таблицы. Возвращает индекс новой строки. */
    cloneRow(from: number): number {
        const list = this.list;
        const src = this.dict(from);
        const copy = src.class.new();
        const count = src.field<number>("_count").value;
        const base = src.field<Il2Cpp.Array<any>>("_entries").value.handle.add(ARRAY_DATA);
        for (let i = 0; i < count; i++) {
            const e = base.add(i * ENTRY_SIZE);
            if (e.add(ENTRY_NEXT).readS32() < -1) continue;
            const k = e.add(ENTRY_KEY).readPointer();
            if (k.isNull()) continue;
            copy.method("set_Item").invoke(k, e.add(ENTRY_VALUE).readPointer());
        }
        list.method("Add").invoke(copy);
        this.invalidate();
        return this.length - 1;
    }

    removeLast(): void {
        this.list.method("RemoveAt").invoke(this.length - 1);
        this.invalidate();
    }
}

/** Имена всех таблиц Dictionary_*, составленных из строк-словарей. */
export function tableNames(): string[] {
    return db().fields
        .filter(f => f.isStatic && f.name.startsWith("Dictionary_") && f.type.name.includes("List<System.Collections.Generic.Dictionary<System.String,System.Object>>"))
        .map(f => f.name.substring("Dictionary_".length));
}

/** Статический List<int> из Z_Database (например `List_ArtifactPossibleGrade_C`). */
export function intList(fieldName: string): Il2Cpp.Object {
    const l = db().field<Il2Cpp.Object>(fieldName).value;
    if (l.isNull()) throw new Error(`список ${fieldName} ещё не создан`);
    return l;
}

/** Таблица -> статический массив иконок Z_Database, индексируемый по ID строки. */
export const PORTRAIT_ARRAY: Record<string, string> = {
    Ability: "SpriteList_AbilityPortrait",
    MagicCom: "SpriteList_MagicComPortrait",
    Synergy: "SpriteList_SynergyPortrait",
    Class: "SpriteList_ClassPortrait",
};

/**
 * Гарантирует, что в массиве спрайтов есть элемент `index` (при необходимости массив увеличивается)
 * и кладёт в него спрайт из `copyFrom`. Без этого новый ID выходит за границы массива иконок и игра падает.
 * Возвращает длину массива до изменения.
 */
export function ensureSprite(fieldName: string, index: number, copyFrom: number): number {
    const f = db().field<Il2Cpp.Array<Il2Cpp.Object>>(fieldName);
    let arr = f.value;
    const before = arr.length;
    if (before <= index) {
        const bigger = Il2Cpp.array<Il2Cpp.Object>(arr.elementType.class, index + 1);
        for (let i = 0; i < before; i++) bigger.set(i, arr.get(i));
        f.value = bigger;
        arr = bigger;
    }
    arr.set(index, arr.get(copyFrom));
    return before;
}
