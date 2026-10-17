/** Публичный API для модов. Всё, что нужно автору мода, импортируется отсюда. */

import type { Row, Table } from "./data";
export type { Row, Table } from "./data";
import type { ArtifactDef } from "./artifacts";
export type { ArtifactDef } from "./artifacts";
import type { ArtifactChanges, ComboDef, SynergyDef } from "./commands";
export type { ArtifactChanges, ComboDef, SynergyDef } from "./commands";
import type { EffectDef } from "./effects";
export type { EffectDef, HeroView, FieldRef } from "./effects";
import type { ModEvents } from "./events";
export type { GameEvents, ModEvents } from "./events";

export interface ModOption {
    key: string;
    label: string;
    default: number;
    min: number;
    max: number;
    step: number;
    /** Не показывать в списке модов (значение всё равно сохраняется) — для модов с собственным экраном. */
    hidden?: boolean;
}

/** Правка игровых таблиц (комбинации, артефакты, ульты, синергии, школы...). Всё откатывается при выключении мода. */
export interface ModData {
    /** Вызывает fn, когда игра загрузила таблицы (обычно через несколько секунд после старта). */
    whenReady(fn: () => void): void;
    /** Сырой доступ к таблице: `Ability`, `MagicCom`, `Synergy`, `Class`, `AllStat`, `UnitStat`, `BattleUnitEnemy`, `Explain`, `Name`. */
    table(name: string): Table;
    /** Меняет ячейки строки с данным ID. Пример: `edit("Ability", 41, { "이름": "Новое имя", "효과ID_02값": 99 })`. */
    edit(table: string, id: number, changes: Row): void;
    /** Добавляет копию строки `cloneId` с новым ID (возвращается) и применяет изменения. */
    add(table: string, cloneId: number, changes: Row): number;
    /**
     * Добавляет новый артефакт целиком: строка в таблице, иконка, пулы выпадения, запись для окна описания и плитка в энциклопедии.
     * Вызывать из `whenReady`. Возвращает ID. Пример: `addArtifact({ name: "Меч", effects: [[4, 50]], lines: ["Атака +50%"] })`.
     */
    addArtifact(def: ArtifactDef): number;
    /** Добавляет сразу несколько артефактов одним запросом (быстрее, чем по одному). Возвращает ID по порядку. */
    addArtifacts(defs: ArtifactDef[]): number[];
    /** Добавляет несколько комбинаций одним запросом. Возвращает ID по порядку. */
    addCombos(defs: ComboDef[]): number[];
    /** Добавляет несколько синергий одним запросом. Возвращает ID по порядку. */
    addSynergies(defs: SynergyDef[]): number[];
    /** Изменяет артефакт: имя, эффекты, описание, редкость (перенос в другой пул выпадения). Откатывается при выключении мода. */
    editArtifact(id: number, changes: ArtifactChanges): void;
    /** Убирает артефакт из выпадения (сундуки, торговец). Получить его в бою больше нельзя. */
    removeArtifact(id: number): void;
    /** Возвращает в выпадение артефакт, убранный `removeArtifact`. */
    restoreArtifact(id: number): void;
    /** Изменяет комбинацию: имя, эффекты, описание, требуемые навыки. */
    editCombo(id: number, changes: { name?: string; effects?: Array<[number, number]>; lines?: string[]; requires?: number[] }): void;
    /** Добавляет комбинацию (клон образца с новыми эффектами). Возвращает ID. */
    addCombo(def: ComboDef): number;
    /** Добавляет синергию. Возвращает ID. */
    addSynergy(def: SynergyDef): number;
    /** Очищает синергию: в ней не остаётся артефактов, она не срабатывает. */
    removeSynergy(id: number): void;
    /** Добавляет артефакт в синергию (максимум 5). */
    synergyAddArtifact(synergyId: number, artifactId: number): void;
    /** Убирает артефакт из синергии. */
    synergyRemoveArtifact(synergyId: number, artifactId: number): void;
    /** Кладёт в массив иконок `Z_Database.<arrayField>` по индексу `index` спрайт из индекса `copyFrom` (массив растёт при необходимости). */
    setSprite(arrayField: string, index: number, copyFrom: number): void;
    /**
     * Регистрирует СВОЙ эффект (ID >= 10000), который потом можно писать в `effects: [[ID, значение]]` артефактов и комбинаций.
     * После каждого пересчёта характеристик игрой обработчик `apply(hero, value, ctx)` вызывается для каждого артефакта/комбинации героя
     * с этим эффектом; `hero.field("UnitApply_...").value` отдаёт базу и не накапливается. Откатывается при выключении мода.
     */
    defineEffect(def: EffectDef): void;
    /**
     * Глобальные эффекты мода: пары [ID эффекта игры, значение] (ID — из docs/EFFECTS.md), действующие на героя как эффекты артефакта.
     * Считает их сама игра при пересчёте характеристик, поэтому применяются точно. Новый вызов заменяет прежний список мода; `[]` снимает.
     * Вступают в силу при следующем пересчёте игры (старт боя, выбор способности).
     */
    setEffects(pairs: Array<[number, number]>): void;
    /** Подключает встроенные эффекты: 10001 урон магической пули %, 10002 перезарядка −%, 10003 снарядов/молний +N, 10004 радиус сбора %. */
    useBuiltinEffects(): void;
    /** Добавляет число в статический List<int> Z_Database (например `List_ArtifactPossibleGrade_C`). */
    addToList(listField: string, value: number): void;
}

export interface ModContext {
    readonly id: string;
    /** Текущие значения настроек мода (сохраняются между запусками). */
    readonly settings: Record<string, number>;
    /** Правка игровых таблиц. */
    readonly data: ModData;
    /** Меняет значение настройки и сохраняет в config.json, не перезапуская мод. */
    setSetting(key: string, value: number): void;
    /** Пишет строку в modkit.log. */
    log(msg: string): void;
    /** Класс из Assembly-CSharp (или `Сборка:Класс`, например `UnityEngine.CoreModule:UnityEngine.Time`). */
    cls(name: string): Il2Cpp.Class;
    /** Шина событий: игровые события (`battleStart`, `enemyDeath`...) и свои события между модами. */
    readonly events: ModEvents;
    /** Публикует API мода для других модов (читается через `ctx.mod(id)`). Убирается при выключении мода. */
    expose(api: unknown): void;
    /** API другого включённого мода (то, что он передал в `expose`) или undefined. Зависимости включаются раньше, поэтому из `onEnable` зависимого мода оно уже доступно. */
    mod<T = any>(id: string): T | undefined;
    /**
     * Полностью заменяет метод. Откатывается автоматически при выключении мода.
     * Если этот метод уже заменил другой включённый мод, включение мода отклоняется с сообщением о конфликте.
     */
    replace(
        className: string, method: string, paramCount: number,
        impl: (this: Il2Cpp.Object, ...args: any[]) => any,
    ): void;
    /** Вызывает fn после метода (оригинал выполняется как обычно). `self` — this-объект метода. */
    after(className: string, method: string, paramCount: number, fn: (self: Il2Cpp.Object) => void): void;
    /** Вызывает fn перед методом. */
    before(className: string, method: string, paramCount: number, fn: (self: Il2Cpp.Object) => void): void;
    /** Периодический вызов, пока мод включён. Выполняется внутри Il2Cpp.perform. */
    every(ms: number, fn: () => void): void;
    /** Выполнить при выключении мода (для отката своих изменений). */
    onCleanup(fn: () => void): void;
}

/** Данные из mod.json внешнего мода (картинки — сырые байты PNG/JPG). */
export interface ModMeta {
    version?: string;
    author?: string;
    icon?: Uint8Array | null;
    screenshots?: Uint8Array[];
}

export interface ModDef {
    /** Уникальный id (латиница, без пробелов). */
    id: string;
    name: string;
    description?: string;
    /** Версия мода (для zip-модов берётся из mod.json). Формат: числа через точку, например "1.2.0". */
    version?: string;
    /**
     * Зависимости: id мода -> допустимые версии (`"*"`, `">=1.2"`, `">=1.0 <2.0"`, `"1.2"` = не ниже).
     * Зависимости включаются автоматически и раньше мода; выключение зависимости выключает зависимые моды.
     * Если зависимости нет или версия не подходит, мод не запускается, а в окне «Моды» видна причина.
     */
    depends?: Record<string, string>;
    /** Мягкий порядок: если перечисленные моды включены, этот мод запускается после них (без обязательности). */
    loadAfter?: string[];
    /** Путь к установленному .zip (заполняет загрузчик; нужен для удаления мода). */
    source?: string;
    /** Заполняется загрузчиком для модов из .zip. */
    meta?: ModMeta;
    /** Включён ли мод по умолчанию при первом запуске. */
    defaultEnabled?: boolean;
    /** Мод правит таблицы данных игры: изменения полностью заработают только после перезапуска, окно «Моды» покажет плашку «Выйти и применить». */
    restartRequired?: boolean;
    /** Числовые настройки, отображаются в меню кнопками − / +. */
    options?: ModOption[];
    onEnable(ctx: ModContext): void;
    /**
     * Вызывается на главном потоке Unity каждый раз, когда построено главное меню (и сразу при включении мода, если меню уже есть).
     * Здесь мод может добавить свои кнопки и экраны, используя `./uikit`. Объекты, созданные здесь, мод убирает сам через `ctx.onCleanup`.
     */
    onMainMenu?(ctx: ModContext, menu: Il2Cpp.Object): void;
    /** Если задан, в карточке включённого мода в окне «Моды» появляется кнопка «Настройки»; по нажатию вызывается это (на главном потоке) — обычно открывает свой экран. */
    openSettings?(ctx: ModContext): void;
    /** Необязательно: дополнительный откат. Хуки, every и onCleanup откатываются сами. */
    onDisable?(ctx: ModContext): void;
}

export function defineMod(def: ModDef): ModDef {
    return def;
}
