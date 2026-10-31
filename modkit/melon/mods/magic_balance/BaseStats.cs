using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using Il2Cpp;
using ModKit;

namespace MagicBalance
{
    /// <summary>
    /// Базовые цифры заклинаний (balance.json → spells.N.baseStats: урон, размер, количество, КД). В таблицах их нет: игра
    /// задаёт поля героя UnitBase_* константами в C_Battle_AllyUnit.StartInformation_A, из них считает бой (StartInformation_B)
    /// и показывает энциклопедия (Display_AbilityDetail). После StartInformation_A записываем свои значения.
    /// Единицы — как на странице заклинания: размер там = поле × 0.01 (у луча × 0.005), остальное как есть.
    /// </summary>
    internal static class BaseStats
    {
        /// <summary>Поле и множитель показа; Rel — показ всегда 1 (спутник), значение сайта — множитель к полю игры.</summary>
        private struct F { public string Name; public double Scale; public bool Rel; }
        private static F A(string n, double k = 1) => new F { Name = "UnitBase_" + n, Scale = k };
        private static F R(string n) => new F { Name = "UnitBase_" + n, Scale = 1, Rel = true };

        // ID заклинания → ключ сайта → поле (по Display_AbilityDetail)
        private static readonly Dictionary<int, Dictionary<string, F>> Map = new Dictionary<int, Dictionary<string, F>>
        {
            [1] = S(A("MagicBulletDamage"), A("MagicBulletSize", 0.01), A("MagicBulletNum"), A("MagicBulletPSpeed")),
            [2] = S(A("FireballDamage"), A("FireballArea", 0.01), A("FireballNum"), A("FireballPSpeed")),
            [3] = S(A("SpiritDamage"), null, A("SpiritNum"), A("SpiritPSpeed")),
            [4] = S(A("SatelliteDamage"), R("SatelliteSize"), A("SatelliteNum"), null),
            [5] = S(A("IceNovaDamage"), A("IceNovaArea", 0.01), null, A("IceNovaPSpeed")),
            [6] = S(null, null, A("ShieldMaxNum"), A("ShieldPSpeed")),
            [7] = S(A("ThunderDamage"), A("ThunderArea", 0.01), A("ThunderNum"), A("ThunderPSpeed")),
            [8] = S(A("ElectricZoneDamage"), A("ElectricZoneArea", 0.01), null, A("ElectricZoneADelay")),
            [9] = S(A("TsunamiDamage"), A("TsunamiSize", 0.01), A("TsunamiNum"), A("TsunamiPSpeed")),
            [10] = S(A("MeteorDamage"), A("MeteorArea", 0.01), A("MeteorNum"), A("MeteorPSpeed")),
            [11] = S(null, null, null, A("GhostPSpeed")),
            [12] = S(A("TornadoDamage"), A("TornadoSize", 0.01), A("TornadoNum"), A("TornadoPSpeed")),
            [13] = S(A("ElectricDamage"), A("ElectricSize", 0.01), A("ElectricNum"), A("ElectricPSpeed")),
            [14] = S(null, null, null, A("ArmageddonPSpeed")),
            [15] = S(A("IncinerateDamage"), A("IncinerateSize", 0.01), A("IncinerateFireNum"), A("IncineratePSpeed")),
            [16] = S(A("EnergyBulletDamage"), A("EnergyBulletSize", 0.01), A("EnergyBulletFireNum"), A("EnergyBulletPSpeed")),
            [17] = S(A("BlizzardDamage"), A("BlizzardArea", 0.01), A("BlizzardNum"), A("BlizzardPSpeed")),
            [18] = S(A("ArcaneRayDamage"), A("ArcaneRaySize", 0.005), A("ArcaneRayNum"), A("ArcaneRayPSpeed")),
            [19] = S(null, null, null, A("MagicCirclePSpeed")),
            [20] = S(A("LavaFieldDamage"), A("LavaFieldSize", 0.01), A("LavaFieldNum"), A("LavaFieldPSpeed")),
            [21] = S(A("FlashShockDamage"), A("FlashShockSize", 0.01), A("FlashShockNum"), A("FlashShockPSpeed")),
        };

        private static Dictionary<string, F> S(F? dmg, F? size, F? num, F? cd)
        {
            var d = new Dictionary<string, F>();
            if (dmg != null) d["урон"] = dmg.Value;
            if (size != null) d["размер"] = size.Value;
            if (num != null) d["количество"] = num.Value;
            if (cd != null) d["КД"] = cd.Value;
            return d;
        }

        private struct Want { public int Spell; public string Key; public F F; public double Value; public PropertyInfo P; }
        private static readonly List<Want> Wants = new List<Want>();
        private static readonly HashSet<string> Logged = new HashSet<string>();

        /// <summary>Читает baseStats из balance.json и ставит хук. Ключи без поля в игре — предупреждение.</summary>
        public static void Enable(ModContext ctx, JsonElement root)
        {
            Wants.Clear(); Logged.Clear();
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("spells", out var spells)) return;
            var skipped = new List<string>();
            foreach (var p in spells.EnumerateObject())
            {
                if (!int.TryParse(p.Name, out int id) || p.Value.ValueKind != JsonValueKind.Object || !p.Value.TryGetProperty("baseStats", out var bs) || bs.ValueKind != JsonValueKind.Object) continue;
                Map.TryGetValue(id, out var fields);
                foreach (var kv in bs.EnumerateObject())
                {
                    if (kv.Value.ValueKind != JsonValueKind.Number) continue;
                    if (fields == null || !fields.TryGetValue(kv.Name, out var f)) { skipped.Add($"{id}.{kv.Name}"); continue; }
                    var prop = typeof(C_Battle_AllyUnit).GetProperty(f.Name, BindingFlags.Public | BindingFlags.Instance);
                    if (prop == null) { skipped.Add($"{id}.{kv.Name} (нет {f.Name})"); continue; }
                    Wants.Add(new Want { Spell = id, Key = kv.Name, F = f, Value = kv.Value.GetDouble(), P = prop });
                }
            }
            if (skipped.Count > 0) ctx.Warn("baseStats без поля в игре, пропущены: " + string.Join(", ", skipped));
            if (Wants.Count == 0) return;
            ctx.After("C_Battle_AllyUnit", "StartInformation_A", 0, (self, a) => ctx.Guard("базовые цифры заклинаний", () => Apply(ctx, (C_Battle_AllyUnit)self)));
        }

        private static void Apply(ModContext ctx, C_Battle_AllyUnit hero)
        {
            if (hero == null) return;
            var changes = new List<string>();
            foreach (var w in Wants)
            {
                double cur = Convert.ToDouble(w.P.GetValue(hero));
                double v = w.F.Rel ? cur * w.Value : w.Value / w.F.Scale;
                if (Math.Abs(cur - v) < 1e-4) continue;
                w.P.SetValue(hero, (float)v);
                string key = $"{w.Spell}.{w.Key}";
                if (Logged.Add(key)) changes.Add($"{w.Spell} {w.Key}: {Show(cur, w.F)} → {Show(v, w.F)} ({w.F.Name})");
            }
            if (changes.Count > 0) ctx.Log("базовые цифры заклинаний: " + string.Join("; ", changes));
        }

        private static string Show(double field, F f) => (f.Rel ? field : field * f.Scale).ToString("0.###");
    }
}
