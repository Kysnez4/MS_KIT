import { log } from "./log";

/** Игровые события шины. Свои события мод может слать через `ctx.events.emit("мой_мод:событие", данные)`. */
export interface GameEvents {
    /** Построено главное меню. */
    mainMenu: { menu: Il2Cpp.Object };
    /** Начался бой (C_Battle_Manager.Start). */
    battleStart: { manager: Il2Cpp.Object };
    /** Бой закончился (C_Battle_Manager.End_Battle). */
    battleEnd: { manager: Il2Cpp.Object };
    /** Смена зоны в бою. */
    areaChange: { manager: Il2Cpp.Object };
    /** Появился враг (C_Battle_EnemyUnit.Start_Unit). */
    enemySpawn: { enemy: Il2Cpp.Object };
    /** Враг умирает (C_Battle_EnemyUnit.Unit_Death), объект ещё жив. */
    enemyDeath: { enemy: Il2Cpp.Object };
    /** Создаётся босс: ID строки врага и количество. */
    bossSpawn: { manager: Il2Cpp.Object; unitId: number; count: number };
    /** Мод включён / выключен (в том числе загрузчиком из-за зависимостей). */
    modEnabled: { id: string };
    modDisabled: { id: string };
}

export interface ModEvents {
    /** Подписка. Снимается сама при выключении мода; пока мод выключен, обработчик не вызывается. */
    on<K extends keyof GameEvents>(name: K, fn: (e: GameEvents[K]) => void): void;
    on(name: string, fn: (payload: any) => void): void;
    /** Рассылает событие всем включённым подписчикам (в том числе других модов). */
    emit(name: string, payload?: any): void;
}

interface Sub { isActive: () => boolean; fn: (payload: any) => void; modId: string }
const subs = new Map<string, Sub[]>();

interface HookSpec {
    cls: string; method: string; params: number; when: "enter" | "leave";
    payload(self: Il2Cpp.Object, args: InvocationArguments): any;
}

const HOOKS: Record<string, HookSpec> = {
    battleStart: { cls: "C_Battle_Manager", method: "Start", params: 0, when: "leave", payload: manager => ({ manager }) },
    battleEnd: { cls: "C_Battle_Manager", method: "End_Battle", params: 0, when: "enter", payload: manager => ({ manager }) },
    areaChange: { cls: "C_Battle_Manager", method: "Start_AreaChange", params: 0, when: "leave", payload: manager => ({ manager }) },
    bossSpawn: {
        cls: "C_Battle_Manager", method: "Create_UnitEnemyBoss", params: 2, when: "enter",
        payload: (manager, a) => ({ manager, unitId: a[1].toInt32(), count: a[2].toInt32() }),
    },
    enemySpawn: { cls: "C_Battle_EnemyUnit", method: "Start_Unit", params: 3, when: "leave", payload: enemy => ({ enemy }) },
    enemyDeath: { cls: "C_Battle_EnemyUnit", method: "Unit_Death", params: 0, when: "enter", payload: enemy => ({ enemy }) },
};

const listeners = new Map<string, InvocationListener>();

function ensureHook(name: string): void {
    const spec = HOOKS[name];
    if (!spec || listeners.has(name)) return;
    const klass = Il2Cpp.domain.assembly("Assembly-CSharp").image.tryClass(spec.cls);
    const m = klass?.tryMethod(spec.method, spec.params);
    if (!m) { log(`events: метод ${spec.cls}.${spec.method} не найден, событие ${name} не будет приходить`); return; }
    const fire = (self: NativePointer, args: InvocationArguments) => {
        if (!subs.get(name)?.length) return;
        try { emit(name, spec.payload(new Il2Cpp.Object(self), args)); } catch (e) { log(`events: ${name}: ${e}`); }
    };
    const listener = spec.when === "enter"
        ? Interceptor.attach(m.virtualAddress, { onEnter(args) { fire(args[0], args); } })
        : Interceptor.attach(m.virtualAddress, {
            onEnter(args) { (this as any).self = args[0]; (this as any).args = args; },
            onLeave() { fire((this as any).self, (this as any).args); },
        });
    listeners.set(name, listener);
}

function dropHookIfUnused(name: string): void {
    if (subs.get(name)?.length) return;
    listeners.get(name)?.detach();
    listeners.delete(name);
}

export function emit(name: string, payload?: any): void {
    for (const s of (subs.get(name) ?? []).slice()) {
        if (!s.isActive()) continue;
        try { s.fn(payload); } catch (e) { log(`[${s.modId}] ошибка в обработчике события ${name}: ${e}`); }
    }
}

/** Шина событий для одного мода; подписки добавляют откат в `cleanups`. */
export function makeEvents(modId: string, isActive: () => boolean, cleanups: Array<() => void>): ModEvents {
    return {
        on(name: string, fn: (payload: any) => void) {
            const sub: Sub = { isActive, fn, modId };
            const list = subs.get(name) ?? [];
            list.push(sub);
            subs.set(name, list);
            ensureHook(name);
            cleanups.push(() => {
                const l = subs.get(name);
                if (l) { const i = l.indexOf(sub); if (i >= 0) l.splice(i, 1); }
                dropHookIfUnused(name);
            });
        },
        emit,
    };
}
