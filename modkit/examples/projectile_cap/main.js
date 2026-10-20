"use strict";
/**
 * «Лимит снарядов»: у каждой магии свой предел числа снарядов (UnitApply_*Num). Всё, что игра насчитала сверх предела, мод
 * обрезает и превращает в бонус: урон ×(1 + лишние·%) и один стат магии (размер/область +% или пробитие).
 *
 * Как мод понимает, что пора применять бонус: игра пересчитывает характеристики героя с нуля (AbilityStatApply, Unit_StatBuff) —
 * после этого счётчик снова выше предела, а урон и размер свежие (без нашего бонуса). Мы в этот момент один раз множим их и
 * ставим счётчик в предел; до следующего пересчёта счётчик равен пределу, и повторно бонус не применяется (без накопления).
 */
// key — ключ настройки cap_<key>; num — поле количества; dmg — поле урона; stat — поле стата; dmgPer — % урона за 1 лишний;
// kind "pct" — стат растёт на statPer % за 1 лишний, "add" — на statPer штук за 1 лишний (с округлением вниз).
const SPELLS = [
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
ModKit.define({
    id: "projectile_cap",
    name: "Лимит снарядов",
    description: "У каждой магии свой предел числа снарядов; всё сверх предела превращается в урон и один стат магии (размер/область/пробитие).",
    options: [],
    onEnable(ctx) {
        let hero = null, calls = 0, dirty = true, errs = 0;
        const missing = new Set();
        const lastSurplus = {}, diag = {};
        /** Настоящие числа снарядов, которые насчитала игра (до обрезки), по ключам магий. */
        const real = {};
        /** Поле героя: instance через объект, static через класс. null, если поля нет в этой версии игры. */
        const fieldOf = (name) => {
            const cf = hero.class.tryField(name);
            if (!cf)
                return null;
            return cf.isStatic ? hero.class.field(name) : hero.field(name);
        };
        const write = (f, v) => { f.value = f.type.name === "System.Int32" ? Math.round(v) : v; };
        /** Включён ли второй круг спутников (группа B): первый объект спутника группы B активен. */
        const groupBOn = () => {
            try {
                const sats = hero.field("Array_Obj_UnitSatellitesB").value;
                if (sats.isNull()) return false;
                const go = sats.get(0);
                return !!go && !go.isNull() && go.method("get_activeSelf").invoke();
            }
            catch (_) { return false; }
        };
        /** Включён ли второй круг духов: ульт «Валькирия» включает родителя Plus-духов (Obj_UnitSpiritMid_Plus). */
        const valkyrieOn = () => {
            try {
                const go = hero.field("Obj_UnitSpiritMid_Plus").value;
                return !go.isNull() && go.method("get_activeSelf").invoke();
            }
            catch (_) { return false; }
        };
        const doubled = (key) => key === "satellite" ? groupBOn() : key === "spirit" ? valkyrieOn() : false;
        const process = (s) => {
            if (missing.has(s.key))
                return;
            const nf = fieldOf(s.num);
            if (!nf) {
                missing.add(s.key);
                ctx.log(`[projectile_cap] ${s.name}: нет поля ${s.num} — пропущено`);
                return;
            }
            let cap = Math.max(1, Math.round(ctx.settings["cap_" + s.key] ?? 1));
            const x2 = doubled(s.key);
            if (x2)
                cap *= 2;       // второй круг (спутники B / ульт «Валькирия» у духов) удваивает число — предел тоже
            const n = Number(nf.value);
            if ((s.key === "spirit" || s.key === "satellite") && n > 0) {
                const sig = `${n}|${cap}|${x2}`;
                if (diag[s.key] !== sig) { diag[s.key] = sig; ctx.log(`[projectile_cap] диагностика ${s.name}: число ${n}, предел ${cap}${x2 ? " (×2 активен)" : ""}`); }
            }
            if (!(n > cap + 1e-6)) {
                lastSurplus[s.key] = 0;
                return;
            }
            real[s.key] = n;
            const surplus = Math.round(n - cap);
            const dmgK = (ctx.settings.dmgScale ?? 100) / 100, statK = (ctx.settings.statScale ?? 100) / 100;
            const df = fieldOf(s.dmg), sf = fieldOf(s.stat);
            let dBefore = 0, dAfter = 0;
            if (df) {
                dBefore = Number(df.value);
                dAfter = dBefore * (1 + surplus * s.dmgPer * dmgK / 100);
                write(df, dAfter);
            }
            if (sf) {
                const b = Number(sf.value);
                write(sf, s.kind === "pct" ? b * (1 + surplus * s.statPer * statK / 100) : b + Math.floor(surplus * s.statPer * statK));
            }
            write(nf, cap);
            if (s.key === "satellite") {
                // у героя есть ещё целое SatelliteNum (копия числа для вращения) — тоже не больше предела
                const mf = hero.class.tryField("SatelliteNum");
                if (mf && !mf.isStatic && Number(hero.field("SatelliteNum").value) > cap) hero.field("SatelliteNum").value = cap;
            }
            if (lastSurplus[s.key] !== surplus) {
                lastSurplus[s.key] = surplus;
                ctx.log(`[projectile_cap] ${s.name}: ${n} -> ${cap}, лишних ${surplus}${x2 ? " (предел ×2)" : ""}, урон ${dBefore.toFixed(2)} -> ${dAfter.toFixed(2)}`);
            }
        };
        const findHero = (mgr) => {
            const field = mgr.field("Obj_FieldAllyUnit").value;
            if (field.isNull())
                return null;
            const tf = field.method("get_transform").invoke();
            const type = ctx.cls("C_Battle_AllyUnit").type.object;
            for (let i = 0; i < tf.method("get_childCount").invoke(); i++) {
                const c = tf.method("GetChild").invoke(i).method("get_gameObject").invoke().method("GetComponent", 1).invoke(type);
                if (c && !c.isNull())
                    return c;
            }
            return null;
        };
        // Игра пересчитала характеристики героя: применяем сразу же в хуке, до того как другие моды (satellite_more) успеют
        // обрезать счётчик в своём Update и мы увидим уже урезанное число.
        const run = () => { for (const s of SPELLS) process(s); };
        // Раскладка спутников по кругу считается по числу ВНУТРИ пересчёта: режем счётчик прямо перед ней, иначе круг делится на
        // сотни спутников и 14 оставшихся стоят почти в одной точке.
        const satSpell = SPELLS.find(x => x.key === "satellite");
        let layoutLogged = 0;
        ctx.before("C_Battle_AllyUnit", "ApplySatelliteGroup", 4, self => {
            try {
                hero = self;
                if (layoutLogged++ < 6) ctx.log(`[projectile_cap] ApplySatelliteGroup: число ${Number(fieldOf(satSpell.num).value)}`);
                process(satSpell);
            }
            catch (e) { if (errs++ < 20) ctx.log(`[projectile_cap] ApplySatelliteGroup: ${e}`); }
        });
        ctx.expose({ real: (key) => real[key] });
        ctx.after("C_Battle_AllyUnit", "Unit_StatBuff", 0, self => {
            dirty = true;
            try { hero = self; run(); }
            catch (e) { if (errs++ < 20) ctx.log(`[projectile_cap] ${e}`); }
        });
        ctx.after("V_Ability_Manager", "AbilityStatApply", 1, () => {
            dirty = true;
            if (!hero) return;
            try { run(); }
            catch (e) { if (errs++ < 20) ctx.log(`[projectile_cap] ${e}`); }
        });
        ctx.before("C_Battle_Manager", "Update", 0, mgr => {
            if (!dirty && ++calls % 10 !== 0)
                return;
            dirty = false;
            try {
                hero = findHero(mgr);
                if (!hero)
                    return;
                run();
            }
            catch (e) {
                if (errs++ < 20)
                    ctx.log(`[projectile_cap] ${e}`);
            }
        });
        ctx.log("[projectile_cap] включён");
    },
});
