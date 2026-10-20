import { defineMod } from "../api";

/**
 * «Лимит снарядов»: у каждой магии свой предел числа снарядов (UnitApply_*Num). Всё, что игра насчитала сверх предела, мод
 * обрезает и превращает в бонус: урон ×(1 + лишние·%) и один стат магии (размер/область +% или пробитие).
 *
 * Зачем обрезать раньше всех: число игра записывает внутри V_Ability_Manager.AbilityStatApply и C_Battle_AllyUnit.StartInformation_B,
 * а раскладку спутников/духов по кругу считает дальше в тех же функциях по уже записанному числу. Обрезка «после пересчёта» оставляла
 * круг, поделённый на сотни спутников (все в одной точке). Поэтому хуки ставятся ПРЯМО ПОСЛЕ каждой инструкции записи этого поля
 * (адреса найдены поиском `str s/w, [xN, #смещение]` по libil2cpp.so игры версии 612), и число режется в памяти до того, как его
 * прочтёт следующий код. Настоящее число запоминается (`pending`), бонус урона/стата применяется в конце пересчёта.
 * Для статических полей (метеорит) хука нет: они режутся после пересчёта.
 */
interface Spell {
    key: string; name: string; num: string; dmg: string; stat: string;
    /** "pct": стат растёт на statPer % за 1 лишний; "add": на statPer штук за 1 лишний (вниз до целого). */
    kind: "pct" | "add"; statPer: number; dmgPer: number;
    /** Число в поле остаётся настоящим (статы и бонусы за количество считаются по нему); режется только раскладка круга. */
    keepReal?: boolean;
}
/** ID заклинания в таблице Ability (он же индекс в Z_Database.List_AllyMagicLevel). */
const SPELL_ID: Record<string, number> = {
    bullet: 1, fireball: 2, spirit: 3, satellite: 4, shield: 6, thunder: 7, tsunami: 9, meteor: 10, tornado: 12, electric: 13,
    incinerate: 15, energy: 16, blizzard: 17, arcane: 18, lava: 20, flash: 21,
};
const SPELLS: Spell[] = [
    { key: "bullet", name: "Магическая пуля", num: "UnitApply_MagicBulletNum", dmg: "UnitApply_MagicBulletDamage", stat: "UnitApply_MagicBulletPierceNum", kind: "add", statPer: 0.1, dmgPer: 2 },
    { key: "fireball", name: "Огненный шар", num: "UnitApply_FireballNum", dmg: "UnitApply_FireballDamage", stat: "UnitApply_FireballPierceNum", kind: "add", statPer: 0.1, dmgPer: 3 },
    { key: "thunder", name: "Молния", num: "UnitApply_ThunderNum", dmg: "UnitApply_ThunderDamage", stat: "UnitApply_ThunderArea", kind: "pct", statPer: 1, dmgPer: 3 },
    { key: "tsunami", name: "Цунами", num: "UnitApply_TsunamiNum", dmg: "UnitApply_TsunamiDamage", stat: "UnitApply_TsunamiSize", kind: "pct", statPer: 1.5, dmgPer: 5 },
    { key: "spirit", name: "Дух", num: "UnitApply_SpiritNum", dmg: "UnitApply_SpiritDamage", stat: "UnitApply_SpiritBulletSize", kind: "pct", statPer: 1, dmgPer: 3 },
    { key: "satellite", name: "Спутник", num: "UnitApply_SatelliteNum", dmg: "UnitApply_SatelliteDamage", stat: "UnitApply_SatelliteSize", kind: "pct", statPer: 1, dmgPer: 3 },
    { key: "meteor", name: "Метеорит", num: "UnitApply_MeteorNum", dmg: "UnitApply_MeteorDamage", stat: "UnitApply_MeteorArea", kind: "pct", statPer: 1, dmgPer: 3 },
    { key: "tornado", name: "Вихрь", num: "UnitApply_TornadoNum", dmg: "UnitApply_TornadoDamage", stat: "UnitApply_TornadoSize", kind: "pct", statPer: 1.5, dmgPer: 5 },
    { key: "electric", name: "Электрический удар", num: "UnitApply_ElectricNum", dmg: "UnitApply_ElectricDamage", stat: "UnitApply_ElectricSize", kind: "pct", statPer: 1, dmgPer: 4 },
    { key: "incinerate", name: "Испепеление", num: "UnitApply_IncinerateFireNum", dmg: "UnitApply_IncinerateDamage", stat: "UnitApply_IncinerateSize", kind: "pct", statPer: 1, dmgPer: 4 },
    { key: "energy", name: "Энергетическая пуля", num: "UnitApply_EnergyBulletFireNum", dmg: "UnitApply_EnergyBulletDamage", stat: "UnitApply_EnergyBulletSize", kind: "pct", statPer: 1, dmgPer: 3 },
    { key: "blizzard", name: "Метель", num: "UnitApply_BlizzardNum", dmg: "UnitApply_BlizzardDamage", stat: "UnitApply_BlizzardArea", kind: "pct", statPer: 1, dmgPer: 4 },
    { key: "arcane", name: "Таинственный луч", num: "UnitApply_ArcaneRayNum", dmg: "UnitApply_ArcaneRayDamage", stat: "UnitApply_ArcaneRaySize", kind: "pct", statPer: 2, dmgPer: 8 },
    { key: "lava", name: "Лавовая область", num: "UnitApply_LavaFieldNum", dmg: "UnitApply_LavaFieldDamage", stat: "UnitApply_LavaFieldSize", kind: "pct", statPer: 1.5, dmgPer: 5 },
    { key: "flash", name: "Световая вспышка", num: "UnitApply_FlashShockNum", dmg: "UnitApply_FlashShockDamage", stat: "UnitApply_FlashShockSize", kind: "pct", statPer: 1, dmgPer: 4 },
    { key: "shield", name: "Щит", num: "UnitApply_ShieldMaxNum", dmg: "UnitApply_ShieldDamage", stat: "UnitApply_ShieldArea", kind: "pct", statPer: 2, dmgPer: 5 },
];

/** Места записи числа: смещение поля в C_Battle_AllyUnit и [RVA инструкции записи, номер регистра с героем]. Версия игры 612. */
const STORES: Record<string, { off: number; int: boolean; sites: Array<[number, number]> }> = {
    bullet: { off: 0xbac, int: false, sites: [[0x110ab80, 8], [0x11c70a8, 19], [0x11cb6d0, 19], [0x11ce4ec, 19]] },
    fireball: { off: 0xbe0, int: false, sites: [[0x110ad88, 8], [0x11c76b8, 19], [0x11cb810, 19], [0x11da184, 19], [0x11de6d4, 19]] },
    thunder: { off: 0xc08, int: false, sites: [[0x110af18, 8], [0x11c7a44, 19], [0x11cb888, 19]] },
    tsunami: { off: 0xc34, int: false, sites: [[0x110b0e0, 8], [0x11c4354, 8], [0x11c7efc, 19], [0x11cba5c, 19]] },
    spirit: { off: 0xc50, int: false, sites: [[0x110b1f8, 8], [0x11c82f4, 19], [0x11cbacc, 19]] },
    satellite: { off: 0xc94, int: false, sites: [[0x110b4e8, 8], [0x11c1f5c, 8], [0x11c24e0, 8], [0x11c4704, 8], [0x11c87c8, 19], [0x11cbbec, 19], [0x11d498c, 8]] },
    tornado: { off: 0xcf0, int: false, sites: [[0x110b8c8, 8], [0x11c924c, 19], [0x11cbf48, 19]] },
    electric: { off: 0xd1c, int: true, sites: [[0x11c9878, 19], [0x11de804, 19]] },
    incinerate: { off: 0xd2c, int: false, sites: [[0x110bb88, 8], [0x11c9b08, 19], [0x11cc0a0, 19]] },
    energy: { off: 0xd48, int: false, sites: [[0x110bca0, 8], [0x11c9ee4, 19], [0x11cc13c, 19], [0x11d9ce0, 19], [0x11dc304, 19]] },
    blizzard: { off: 0xd5c, int: false, sites: [[0x110bd88, 8], [0x11ca280, 19], [0x11cc208, 19]] },
    arcane: { off: 0xd70, int: true, sites: [[0x11ca4bc, 19], [0x11cc3d0, 19], [0x11df8e0, 19]] },
    lava: { off: 0xda8, int: false, sites: [[0x110c0c8, 8], [0x11caadc, 19], [0x11cc3e4, 19], [0x11cfc04, 19]] },
    flash: { off: 0xdc4, int: false, sites: [[0x110c208, 8], [0x11cad7c, 19], [0x11cc468, 19], [0x11de548, 19], [0x11de9cc, 19]] },
    shield: { off: 0xc1c, int: true, sites: [[0x11c7ccc, 19], [0x11cb8e4, 19]] },
};

const DEFAULT_CAP: Record<string, number> = {
    bullet: 50, fireball: 30, thunder: 30, tsunami: 15, spirit: 14, satellite: 14, meteor: 30, tornado: 15, electric: 20,
    incinerate: 20, energy: 30, blizzard: 20, arcane: 10, lava: 15, flash: 20, shield: 10,
};

export default defineMod({
    id: "projectile_cap",
    name: "Лимит снарядов",
    description: "У каждой магии свой предел числа снарядов; сверх предела — бонус урона и стата. Спутники ×2 с кругом B, духи ×2 с Валькирией.",
    options: [
        { key: "dmgScale", label: "Бонус урона, % от таблицы", default: 100, min: 0, max: 500, step: 25 },
        { key: "statScale", label: "Бонус стата, % от таблицы", default: 100, min: 0, max: 500, step: 25 },
        { key: "native_test", label: "Эксперимент CModule: 0 выкл, 1 компиляция, 2 пустой на 1 месте, 3 рабочий на 1, 4 пустые на всех, 5 рабочие на всех", default: 0, min: 0, max: 5, step: 1 },
        ...SPELLS.map(s => ({
            key: "cap_" + s.key,
            label: `${s.name}: предел${s.key === "satellite" ? " (с кругом B ×2)" : s.key === "spirit" ? " (с Валькирией ×2)" : ""}`,
            default: DEFAULT_CAP[s.key], min: 1, max: 200, step: 1,
        })),
    ],
    onEnable(ctx) {
        const base = Il2Cpp.module.base;
        const heroCls = ctx.cls("C_Battle_AllyUnit");
        const heroes = new Map<string, Il2Cpp.Object>();     // проверенные указатели героя
        const real: Record<string, number> = {};
        const shown: Record<string, number> = {};             // бонус урона в % для окна характеристик
        const lastLog: Record<string, string> = {};
        let errs = 0, hookCount = 0;
        const err = (what: string, e: unknown) => { if (errs++ < 20) ctx.log(`[projectile_cap] ${what}: ${e}`); };

        /** Объект героя по указателю из регистра; null, если это не C_Battle_AllyUnit (защита от неверного регистра). */
        const heroAt = (p: NativePointer): Il2Cpp.Object | null => {
            const k = p.toString();
            const known = heroes.get(k);
            if (known) return known;
            try {
                if (p.isNull()) return null;
                const h = new Il2Cpp.Object(p);
                if (h.class.name !== "C_Battle_AllyUnit") return null;
                if (heroes.size > 8) heroes.clear();
                heroes.set(k, h);
                return h;
            } catch (_) { return null; }
        };
        // поиск поля по имени через Il2Cpp медленный, а вызывается на каждом пересчёте — кешируем по герою
        const fieldCache = new Map<string, Il2Cpp.Field<number> | null>();
        const fieldOf = (hero: Il2Cpp.Object, name: string): Il2Cpp.Field<number> | null => {
            const key = hero.handle.toString() + ":" + name;
            const hit = fieldCache.get(key);
            if (hit !== undefined) return hit;
            if (fieldCache.size > 400) fieldCache.clear();
            const cf = hero.class.tryField<number>(name);
            const f = !cf ? null : cf.isStatic ? hero.class.field<number>(name) : hero.field<number>(name);
            fieldCache.set(key, f);
            return f;
        };
        const write = (f: Il2Cpp.Field<number>, v: number) => { f.value = f.type.name === "System.Int32" ? Math.round(v) : v; };

        /** Включён ли второй круг спутников (группа B): первый объект спутника группы B активен. */
        const groupBOn = (hero: Il2Cpp.Object): boolean => {
            try {
                const sats = hero.field<Il2Cpp.Array<Il2Cpp.Object>>("Array_Obj_UnitSatellitesB").value;
                if (sats.isNull()) return false;
                const go = sats.get(0);
                return !!go && !go.isNull() && go.method<boolean>("get_activeSelf").invoke();
            } catch (_) { return false; }
        };
        /** Включён ли второй круг духов: ульт «Валькирия» включает родителя Plus-духов. */
        const valkyrieOn = (hero: Il2Cpp.Object): boolean => {
            try {
                const go = hero.field<Il2Cpp.Object>("Obj_UnitSpiritMid_Plus").value;
                return !go.isNull() && go.method<boolean>("get_activeSelf").invoke();
            } catch (_) { return false; }
        };
        const capOf = (s: Spell, hero: Il2Cpp.Object): { cap: number; x2: boolean } => {
            let cap = Math.max(1, Math.round(ctx.settings["cap_" + s.key] ?? DEFAULT_CAP[s.key]));
            const x2 = s.key === "satellite" ? groupBOn(hero) : s.key === "spirit" ? valkyrieOn(hero) : false;
            if (x2) cap *= 2;
            return { cap, x2 };
        };

        // ---- общая память с нативным хуком: [0] указатель героя, [8..] предел по магиям (float), [72..] насчитанное игрой сверх предела (float)
        const SPELL_IDX: Record<string, number> = Object.fromEntries(SPELLS.map((x, i) => [x.key, i]));
        const shared = Memory.alloc(8 + 16 * 4 + 16 * 4);
        shared.writeU64(0);
        let native = false;
        const pendingJs: Record<string, number> = {};          // запасной путь (если CModule недоступен)
        /** Насчитанное игрой число сверх предела (undefined, если лишних не было с прошлой обработки). */
        const peekPending = (key: string): number | undefined => {
            if (native) { const v = shared.add(72 + SPELL_IDX[key] * 4).readFloat(); return v > 0 ? v : undefined; }
            return pendingJs[key];
        };
        const takePending = (key: string): number | undefined => {
            const v = peekPending(key);
            if (native) shared.add(72 + SPELL_IDX[key] * 4).writeFloat(0); else delete pendingJs[key];
            return v;
        };
        /** Публикуем герою и пределы (с учётом кругов B / Валькирии) — нативный хук читает их без вызовов Il2Cpp. */
        const publish = (hero: Il2Cpp.Object) => {
            shared.writePointer(hero.handle);
            for (const sp of SPELLS) shared.add(8 + SPELL_IDX[sp.key] * 4).writeFloat(capOf(sp, hero).cap);
        };
        for (const sp of SPELLS) shared.add(8 + SPELL_IDX[sp.key] * 4).writeFloat(Math.max(1, Math.round(ctx.settings["cap_" + sp.key] ?? DEFAULT_CAP[sp.key])));

        // ---- хуки сразу после записи числа: режем до того, как его прочтёт раскладка
        const handles: InvocationListener[] = [];
        const onStore = (s: Spell, st: { off: number; int: boolean }, rn: number) => function (this: InvocationContext) {
            try {
                const p = (this.context as any)[`x${rn}`] as NativePointer;
                const hero = heroAt(p);
                if (!hero) return;
                const addr = p.add(st.off);
                const n = st.int ? addr.readS32() : addr.readFloat();
                // быстрый выход: не выше базового предела — значит, и не выше удвоенного (круги B/Валькирия только поднимают предел)
                if (n <= Math.max(1, Math.round(ctx.settings["cap_" + s.key] ?? DEFAULT_CAP[s.key])) + 1e-6) return;
                const { cap } = capOf(s, hero);
                if (n > cap + 1e-6) {
                    pendingJs[s.key] = Math.max(pendingJs[s.key] ?? 0, n);     // за пересчёт число пишется несколько раз: запоминаем наибольшее
                    real[s.key] = n;
                    if (!s.keepReal) { if (st.int) addr.writeS32(cap); else addr.writeFloat(cap); }
                }
            } catch (e) { err("хук записи " + s.key, e); }
        };
        // Нативный хук (CModule): работает без вызовов JS на каждой записи — раньше JS-хуки давали лаг при выборе заклинаний.
        const NATIVE_SRC = `
#include <gum/guminterceptor.h>
typedef struct { void * hero; float cap[16]; float pending[16]; } Shared;
typedef struct { int off; int rn; int is_int; int idx; } Site;
extern Shared sh;
void on_store (GumInvocationContext * ic)
{
  const Site * s = gum_invocation_context_get_listener_function_data (ic);
  char * p = (char *) ic->cpu_context->x[s->rn];
  float n, cap;
  if (p == 0 || p != (char *) sh.hero) return;
  cap = sh.cap[s->idx];
  if (s->is_int) n = (float) *(int *) (p + s->off); else n = *(float *) (p + s->off);
  if (n <= cap + 0.000001f) return;
  if (n > sh.pending[s->idx]) sh.pending[s->idx] = n;
  if (s->is_int) *(int *) (p + s->off) = (int) cap; else *(float *) (p + s->off) = cap;
}`;
        let cm: CModule | null = null;
        const siteData: NativePointer[] = [];                  // держим, чтобы память не освободилась
        // ВЫКЛЮЧЕНО: на устройстве нативные хуки роняли игру при запуске (SIGBUS BUS_ADRALN, переход по мусорному адресу) — причина не найдена
        const USE_NATIVE = false;
        try { if (USE_NATIVE) { cm = new CModule(NATIVE_SRC, { sh: shared }); native = true; } }
        catch (e) { err("CModule недоступен, JS-хуки", e); }
        for (const s of SPELLS) {
            const st = STORES[s.key];
            if (!st) continue;
            for (const [va, rn] of st.sites) {
                try {
                    // на месте должна быть запись нужного поля (у игры другой версии хук не ставим)
                    const w = base.add(va).readU32();
                    const want = st.int ? 0xb9000000 : 0xbd000000;
                    if (((w & 0xffc00000) >>> 0) !== want || ((w >>> 10) & 0xfff) !== st.off / 4) {
                        ctx.log(`[projectile_cap] ${s.name}: по адресу ${va.toString(16)} другая инструкция — хук пропущен`);
                        continue;
                    }
                    if (cm) {
                        const d = Memory.alloc(16);
                        d.writeS32(st.off); d.add(4).writeS32(rn); d.add(8).writeS32(st.int ? 1 : 0); d.add(12).writeS32(SPELL_IDX[s.key]);
                        siteData.push(d);
                        handles.push(Interceptor.attach(base.add(va + 4), { onEnter: cm.on_store }, d));
                    } else if (Math.round(ctx.settings.native_test ?? 0) < 4) handles.push(Interceptor.attach(base.add(va + 4), onStore(s, st, rn)));      // на ступенях 4–5 эксперимента JS-хуки не ставим
                    hookCount++;
                } catch (e) { err("установка хука " + s.key, e); }
            }
        }
        ctx.log(`[projectile_cap] хуки записи: ${native ? "нативные (CModule)" : "JS"}`);

        // ---- эксперимент: почему CModule-хуки роняли игру. Ступени включаются настройкой native_test (после смены — перезапуск игры)
        const level = Math.round(ctx.settings.native_test ?? 0);
        if (level > 0) {
            try {
                ctx.log(`[projectile_cap] эксперимент CModule, ступень ${level}`);
                const empty = new CModule("#include <gum/guminterceptor.h>\nvoid on_empty (GumInvocationContext * ic) { }");
                const real = level === 3 || level === 5 ? new CModule(NATIVE_SRC, { sh: shared }) : null;
                ctx.log("[projectile_cap] CModule скомпилирован");
                if (level >= 2) {
                    // ступени 2 и 3 — одно место (первая запись числа спутников), 4 и 5 — все места; чётные — пустой хук, нечётные от 3 — рабочий
                    const all = level >= 4;
                    let n = 0;
                    for (const sp of SPELLS) {
                        const st = STORES[sp.key];
                        if (!st) continue;
                        for (const [va, rn] of st.sites) {
                            if (!all && !(sp.key === "satellite" && va === st.sites[0][0])) continue;
                            const d = Memory.alloc(16);
                            d.writeS32(st.off); d.add(4).writeS32(rn); d.add(8).writeS32(st.int ? 1 : 0); d.add(12).writeS32(SPELL_IDX[sp.key]);
                            siteData.push(d);
                            const cbs = real ? { onEnter: real.on_store } : { onEnter: empty.on_empty };
                            handles.push(Interceptor.attach(base.add(va + 4), cbs, d));
                            n++;
                        }
                    }
                    ctx.log(`[projectile_cap] нативных хуков установлено: ${n} (${real ? "рабочие" : "пустые"})`);
                }
                ctx.onCleanup(() => { empty.dispose(); real?.dispose(); });
            } catch (e) { err("эксперимент CModule", e); }
        }

        ctx.onCleanup(() => { for (const h of handles) h.detach(); handles.length = 0; shared.writeU64(0); if (cm) cm.dispose(); });
        ctx.log(`[projectile_cap] хуков записи числа: ${hookCount}`);

        // ---- бонус сверх предела: применяем в конце пересчёта
        const process = (hero: Il2Cpp.Object, s: Spell) => {
            const nf = fieldOf(hero, s.num);
            if (!nf) return;
            const { cap, x2 } = capOf(s, hero);
            // число могло быть уже обрезано хуком записи (тогда настоящее — в pending) или ещё нет (статические поля, пропущенные записи)
            const n = takePending(s.key) ?? Number(nf.value);
            if (s.key === "spirit" || s.key === "satellite") {
                const sig = `${n}|${cap}|${x2}`;
                if (lastLog["d" + s.key] !== sig) { lastLog["d" + s.key] = sig; ctx.log(`[projectile_cap] диагностика ${s.name}: число ${n}, предел ${cap}${x2 ? " (×2 активен)" : ""}`); }
            }
            if (!(n > cap + 1e-6)) { delete shown[s.key]; return; }
            // число складывается из артефактов и без самого заклинания: бонус даём только за полученные заклинания (уровень > 0)
            const id = SPELL_ID[s.key];
            if (levels.length && id !== undefined && (levels[id] ?? 1) <= 0) { delete shown[s.key]; write(nf, cap); return; }
            real[s.key] = n;
            const surplus = Math.round(n - cap);
            shown[s.key] = surplus * s.dmgPer * ((ctx.settings.dmgScale ?? 100) / 100);
            const dmgK = (ctx.settings.dmgScale ?? 100) / 100, statK = (ctx.settings.statScale ?? 100) / 100;
            const df = fieldOf(hero, s.dmg), sf = fieldOf(hero, s.stat);
            let before = 0, after = 0;
            if (df) { before = Number(df.value); after = before * (1 + surplus * s.dmgPer * dmgK / 100); write(df, after); }
            if (sf) {
                const b = Number(sf.value);
                write(sf, s.kind === "pct" ? b * (1 + surplus * s.statPer * statK / 100) : b + Math.floor(surplus * s.statPer * statK));
            }
            if (!s.keepReal) write(nf, cap);
            if (s.key === "satellite" && !s.keepReal) {
                // у героя есть ещё целое SatelliteNum (копия числа для вращения) — тоже не больше предела
                const mf = fieldOf(hero, "SatelliteNum");
                if (mf && Number(mf.value) > cap) write(mf, cap);
            }
            const sig = `${n}|${surplus}`;
            if (lastLog[s.key] !== sig) {
                lastLog[s.key] = sig;
                ctx.log(`[projectile_cap] ${s.name}: ${n} -> ${cap}, лишних ${surplus}${x2 ? " (предел ×2)" : ""}, урон ${before.toFixed(2)} -> ${after.toFixed(2)}`);
            }
        };
        // после пересчёта обрабатываем только магии, у которых хук записи поймал лишнее; спутники/духи без хука (статические поля) — по таймеру
        const runPending = (hero: Il2Cpp.Object) => {
            if (!levels.length) logLevels();
            for (const s of SPELLS) if (peekPending(s.key) !== undefined) { try { process(hero, s); } catch (e) { err(s.name, e); } }
        };
        const runAll = (hero: Il2Cpp.Object) => {
            for (const s of SPELLS) if (peekPending(s.key) !== undefined || !STORES[s.key]) { try { process(hero, s); } catch (e) { err(s.name, e); } }
        };

        let hero: Il2Cpp.Object | null = null;
        // полный пересчёт игры: магии без лишних снарядов больше не показывают бонус в окне характеристик
        const dropStale = () => { for (const k of Object.keys(STORES)) if (peekPending(k) === undefined) delete shown[k]; };
        ctx.after("C_Battle_AllyUnit", "StartInformation_B", 0, self => { hero = self; publish(self); dropStale(); runPending(self); });
        ctx.after("V_Ability_Manager", "AbilityStatApply", 1, () => { dropStale(); if (hero) runPending(hero); });
        let calls = 0;
        ctx.before("C_Battle_Manager", "Update", 0, mgr => {
            if (++calls % 10 !== 0) return;
            try {
                const field = mgr.field<Il2Cpp.Object>("Obj_FieldAllyUnit").value;
                if (field.isNull()) return;
                const tf = field.method<Il2Cpp.Object>("get_transform").invoke();
                const type = heroCls.type.object;
                for (let i = 0; i < tf.method<number>("get_childCount").invoke(); i++) {
                    const c = tf.method<Il2Cpp.Object>("GetChild").invoke(i).method<Il2Cpp.Object>("get_gameObject").invoke()
                        .method<Il2Cpp.Object>("GetComponent", 1).invoke(type);
                    if (c && !c.isNull()) { hero = c; publish(c); runAll(c); logLevels(); return; }
                }
            } catch (e) { err("Update", e); }
        });

        // ---- окно характеристик: показываем наш бонус урона (игра берёт его из AllyUnitVar_*DamageRatioU, а мы правим только UnitApply_*)
        const VAR: Record<string, string> = {
            bullet: "MagicBullet", fireball: "Fireball", thunder: "Thunder", tsunami: "Tsunami", spirit: "Spirit", satellite: "Satellite",
            meteor: "Meteor", tornado: "Tornado", incinerate: "Incinerate", energy: "EnergyBullet", blizzard: "Blizzard",
            arcane: "ArcaneRay", lava: "LavaField", flash: "FlashShock", electric: "Electric",
        };
        const zdb = ctx.cls("Z_Database");
        let lastLevels = "";
        let levels: number[] = [];
        /** Уровни заклинаний героя (Z_Database.List_AllyMagicLevel): пишем в лог при изменении, чтобы сверить индексацию с номерами заклинаний. */
        const logLevels = () => {
            try {
                const l = zdb.field<Il2Cpp.Object>("List_AllyMagicLevel").value;
                if (l.isNull()) return;
                const size = l.field<number>("_size").value;
                const items = l.field<Il2Cpp.Array<number>>("_items").value;
                const a: number[] = [];
                for (let i = 0; i < size; i++) a.push(Number(items.get(i)));
                levels = a;
                const sig = a.join(",");
                if (sig !== lastLevels) { lastLevels = sig; ctx.log(`[projectile_cap] уровни заклинаний (индекс=ID?): [${sig}]`); }
            } catch (e) { err("уровни заклинаний", e); }
        };
        const restore: Array<() => void> = [];
        ctx.before("C_Battle_AllyUnit", "Display_BattleStatus", 0, () => {
            for (const [key, pct] of Object.entries(shown)) {
                const name = VAR[key] && `AllyUnitVar_${VAR[key]}DamageRatioU`;
                if (!name || !pct) continue;
                try {
                    const f = zdb.tryField<number>(name);
                    if (!f) continue;
                    const old = Number(f.value);
                    f.value = old + pct;
                    restore.push(() => { f.value = old; });
                } catch (e) { err("окно характеристик " + key, e); }
            }
        });
        ctx.after("C_Battle_AllyUnit", "Display_BattleStatus", 0, () => { while (restore.length) { try { restore.pop()!(); } catch (_) { /* */ } } });
        ctx.expose({ real: (key: string) => real[key] });
        ctx.log("[projectile_cap] включён");
    },
});
