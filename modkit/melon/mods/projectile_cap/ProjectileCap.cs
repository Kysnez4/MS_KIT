using System;
using System.Collections.Generic;
using System.Linq;
using Il2Cpp;
using ModKit;

namespace ProjectileCap
{
    /// <summary>
    /// «Лимит снарядов»: у каждой магии свой предел числа снарядов (UnitApply_*Num). Всё, что игра насчитала сверх предела, мод обрезает
    /// и превращает в бонус: урон ×(1 + лишние·%) и один стат магии (размер/область +% или пробитие).
    /// Работает через проход по герою ядра (ctx.OnHeroPass): после каждого пересчёта игры и раз в ~0,7 с. Число читается как «база»
    /// (то, что насчитала игра), поэтому обрезка не накапливается и откатывается при выключении мода.
    /// Ограничение Harmony: срез происходит после пересчёта, а не посреди него, поэтому раскладка кругов спутников/духов в самом
    /// пересчёте видит полное число (в Frida-версии хуки стояли в середине функций по адресам libil2cpp).
    /// </summary>
    public class ProjectileCapMod : IKitMod
    {
        private class Spell
        {
            public string Key, Name, Num, Dmg, Stat, Var;
            /// <summary>true: стат растёт на StatPer % за 1 лишний; false: на StatPer штук за 1 лишний (вниз до целого).</summary>
            public bool Pct; public double StatPer, DmgPer; public int Cap, Id;
        }

        // Id — ID заклинания в таблице Ability (он же индекс в Z_Database.List_AllyMagicLevel); Var — имя в AllyUnitVar_*DamageRatioU
        private static readonly Spell[] Spells =
        {
            new Spell { Key = "bullet", Name = "Магическая пуля", Num = "UnitApply_MagicBulletNum", Dmg = "UnitApply_MagicBulletDamage", Stat = "UnitApply_MagicBulletPierceNum", Pct = false, StatPer = 0.1, DmgPer = 2, Cap = 50, Id = 1, Var = "MagicBullet" },
            new Spell { Key = "fireball", Name = "Огненный шар", Num = "UnitApply_FireballNum", Dmg = "UnitApply_FireballDamage", Stat = "UnitApply_FireballPierceNum", Pct = false, StatPer = 0.1, DmgPer = 3, Cap = 30, Id = 2, Var = "Fireball" },
            new Spell { Key = "thunder", Name = "Молния", Num = "UnitApply_ThunderNum", Dmg = "UnitApply_ThunderDamage", Stat = "UnitApply_ThunderArea", Pct = true, StatPer = 1, DmgPer = 3, Cap = 30, Id = 7, Var = "Thunder" },
            new Spell { Key = "tsunami", Name = "Цунами", Num = "UnitApply_TsunamiNum", Dmg = "UnitApply_TsunamiDamage", Stat = "UnitApply_TsunamiSize", Pct = true, StatPer = 1.5, DmgPer = 5, Cap = 15, Id = 9, Var = "Tsunami" },
            new Spell { Key = "spirit", Name = "Дух", Num = "UnitApply_SpiritNum", Dmg = "UnitApply_SpiritDamage", Stat = "UnitApply_SpiritBulletSize", Pct = true, StatPer = 1, DmgPer = 3, Cap = 14, Id = 3, Var = "Spirit" },
            new Spell { Key = "satellite", Name = "Спутник", Num = "UnitApply_SatelliteNum", Dmg = "UnitApply_SatelliteDamage", Stat = "UnitApply_SatelliteSize", Pct = true, StatPer = 1, DmgPer = 3, Cap = 14, Id = 4, Var = "Satellite" },
            new Spell { Key = "meteor", Name = "Метеорит", Num = "UnitApply_MeteorNum", Dmg = "UnitApply_MeteorDamage", Stat = "UnitApply_MeteorArea", Pct = true, StatPer = 1, DmgPer = 3, Cap = 30, Id = 10, Var = "Meteor" },
            new Spell { Key = "tornado", Name = "Вихрь", Num = "UnitApply_TornadoNum", Dmg = "UnitApply_TornadoDamage", Stat = "UnitApply_TornadoSize", Pct = true, StatPer = 1.5, DmgPer = 5, Cap = 15, Id = 12, Var = "Tornado" },
            new Spell { Key = "electric", Name = "Электрический удар", Num = "UnitApply_ElectricNum", Dmg = "UnitApply_ElectricDamage", Stat = "UnitApply_ElectricSize", Pct = true, StatPer = 1, DmgPer = 4, Cap = 20, Id = 13, Var = "Electric" },
            new Spell { Key = "incinerate", Name = "Испепеление", Num = "UnitApply_IncinerateFireNum", Dmg = "UnitApply_IncinerateDamage", Stat = "UnitApply_IncinerateSize", Pct = true, StatPer = 1, DmgPer = 4, Cap = 20, Id = 15, Var = "Incinerate" },
            new Spell { Key = "energy", Name = "Энергетическая пуля", Num = "UnitApply_EnergyBulletFireNum", Dmg = "UnitApply_EnergyBulletDamage", Stat = "UnitApply_EnergyBulletSize", Pct = true, StatPer = 1, DmgPer = 3, Cap = 30, Id = 16, Var = "EnergyBullet" },
            new Spell { Key = "blizzard", Name = "Метель", Num = "UnitApply_BlizzardNum", Dmg = "UnitApply_BlizzardDamage", Stat = "UnitApply_BlizzardArea", Pct = true, StatPer = 1, DmgPer = 4, Cap = 20, Id = 17, Var = "Blizzard" },
            new Spell { Key = "arcane", Name = "Таинственный луч", Num = "UnitApply_ArcaneRayNum", Dmg = "UnitApply_ArcaneRayDamage", Stat = "UnitApply_ArcaneRaySize", Pct = true, StatPer = 2, DmgPer = 8, Cap = 10, Id = 18, Var = "ArcaneRay" },
            new Spell { Key = "lava", Name = "Лавовая область", Num = "UnitApply_LavaFieldNum", Dmg = "UnitApply_LavaFieldDamage", Stat = "UnitApply_LavaFieldSize", Pct = true, StatPer = 1.5, DmgPer = 5, Cap = 15, Id = 20, Var = "LavaField" },
            new Spell { Key = "flash", Name = "Световая вспышка", Num = "UnitApply_FlashShockNum", Dmg = "UnitApply_FlashShockDamage", Stat = "UnitApply_FlashShockSize", Pct = true, StatPer = 1, DmgPer = 4, Cap = 20, Id = 21, Var = "FlashShock" },
            new Spell { Key = "shield", Name = "Щит", Num = "UnitApply_ShieldMaxNum", Dmg = "UnitApply_ShieldDamage", Stat = "UnitApply_ShieldArea", Pct = true, StatPer = 2, DmgPer = 5, Cap = 10, Id = 6, Var = null },
        };

        public ModDef Define()
        {
            var def = new ModDef
            {
                Id = "projectile_cap", Name = "Лимит снарядов", Version = "2.0",
                Description = "У каждой магии свой предел числа снарядов; сверх предела — бонус урона и стата. Спутники ×2 с кругом B, духи ×2 с Валькирией.",
                OnEnable = OnEnable,
            };
            def.Options.Add(new ModOption { Key = "dmgScale", Label = "Бонус урона, % от таблицы", Default = 100, Min = 0, Max = 500, Step = 25 });
            def.Options.Add(new ModOption { Key = "statScale", Label = "Бонус стата, % от таблицы", Default = 100, Min = 0, Max = 500, Step = 25 });
            foreach (var s in Spells)
                def.Options.Add(new ModOption
                {
                    Key = "cap_" + s.Key, Default = s.Cap, Min = 1, Max = 200, Step = 1,
                    Label = $"{s.Name}: предел{(s.Key == "satellite" ? " (с кругом B ×2)" : s.Key == "spirit" ? " (с Валькирией ×2)" : "")}",
                });
            return def;
        }

        /// <summary>Включён ли второй круг спутников (группа B): первый объект спутника группы B активен.</summary>
        private static bool GroupBOn(C_Battle_AllyUnit hero)
        {
            try { var a = hero.Array_Obj_UnitSatellitesB; return a != null && a.Length > 0 && a[0] != null && a[0].activeSelf; }
            catch { return false; }
        }

        /// <summary>Включён ли второй круг духов: ульт «Валькирия» включает родителя Plus-духов.</summary>
        private static bool ValkyrieOn(C_Battle_AllyUnit hero)
        {
            try { var go = hero.Obj_UnitSpiritMid_Plus; return go != null && go.activeSelf; }
            catch { return false; }
        }

        private static void OnEnable(ModContext ctx)
        {
            var last = new Dictionary<string, string>();
            var shown = new Dictionary<string, double>();         // бонус урона в % для окна характеристик
            int errs = 0;

            ctx.Use<IEffects>("modkit_effects").OnHeroPass(h =>
            {
                shown.Clear();
                double dmgK = ctx.Setting("dmgScale") / 100, statK = ctx.Setting("statScale") / 100;
                var hero = h.Raw;
                List<int> levels = null;
                try { var l = Z_Database.List_AllyMagicLevel; levels = new List<int>(); for (int i = 0; i < l.Count; i++) levels.Add(l[i]); } catch { /* списка нет */ }
                foreach (var s in Spells)
                {
                    try
                    {
                        var nf = h.Field(s.Num);
                        int cap = Math.Max(1, (int)Math.Round(ctx.Setting("cap_" + s.Key)));
                        bool x2 = s.Key == "satellite" ? GroupBOn(hero) : s.Key == "spirit" && ValkyrieOn(hero);
                        if (x2) cap *= 2;
                        double n = nf.Value;
                        if (!(n > cap + 1e-6)) continue;
                        // число складывается из артефактов и без самого заклинания: бонус даём только за полученные заклинания (уровень > 0)
                        bool owned = levels == null || s.Id >= levels.Count || levels[s.Id] > 0;
                        nf.Value = cap;
                        if (s.Key == "satellite") { var mf = h.Field("SatelliteNum"); if (mf.Value > cap) mf.Value = cap; }
                        if (!owned) continue;
                        double surplus = Math.Round(n - cap);
                        shown[s.Key] = surplus * s.DmgPer * dmgK;
                        var df = h.Field(s.Dmg);
                        double before = df.Value;
                        df.Value = before * (1 + surplus * s.DmgPer * dmgK / 100);
                        var sf = h.Field(s.Stat);
                        sf.Value = s.Pct ? sf.Value * (1 + surplus * s.StatPer * statK / 100) : sf.Value + Math.Floor(surplus * s.StatPer * statK);
                        var sig = $"{n}|{surplus}";
                        if (!last.TryGetValue(s.Key, out var o) || o != sig)
                        {
                            last[s.Key] = sig;
                            ctx.Log($"{s.Name}: {n} -> {cap}, лишних {surplus}{(x2 ? " (предел ×2)" : "")}, урон {before:0.##} -> {df.Value:0.##}");
                        }
                    }
                    catch (Exception e) { if (errs++ < 20) ctx.Log($"{s.Name}: {e.Message}"); }
                }
                // окно характеристик берёт бонус урона из AllyUnitVar_*DamageRatioU, а мы правим только UnitApply_*
                foreach (var s in Spells)
                    if (s.Var != null && shown.TryGetValue(s.Key, out var pct) && pct != 0) h.Display($"AllyUnitVar_{s.Var}DamageRatioU", x => x + pct);
            });
            ctx.Log("включён");
        }
    }
}
