using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Il2Cpp;
using ModKit;

namespace EnemyUnlock
{
    /// <summary>
    /// После появления врага (Start_Unit) правит его по файлу UserData/ModKit/enemy_unlock.json (при первом запуске копируется
    /// из архива мода; перечитывается при включении мода):
    ///   colliderOn — включить коллайдер (взрывы и артефакты бьют через OnCollisionEnter; у Слуг и боссов он выключен);
    ///   removeAbilities — особенности, которые убрать всем (10 — иммунитет к эффекту 100182, 11 — «босс»: иммунитет к уничтожению);
    ///   keepAbilities — ID врага → особенности, которые ему оставить (по умолчанию 11 боссам 41–46);
    ///   debuffResistance — ID врага → сопротивление дебаффам; коэффициенты стана/заморозки пересчитываются по формуле Start_Unit;
    /// Врагов в журнал не пишет: толпы Слуг давали сотни строк в секунду, запись журнала на /sdcard грузила телефон.
    /// HP боссов, уже умноженное в Start_Unit по особенности 11, остаётся.
    /// </summary>
    public class EnemyUnlockMod : IKitMod
    {
        public ModDef Define() => new ModDef
        {
            Id = "enemy_unlock",
            Name = "Уязвимые враги",
            Description = "Коллайдеры, особенности и сопротивление врагов из файла UserData/ModKit/enemy_unlock.json.",
            Version = "1.5",
            OnEnable = Enable,
        };

        private class Config
        {
            public bool colliderOn { get; set; } = true;
            public List<int> removeAbilities { get; set; } = new List<int>();
            public Dictionary<string, List<int>> keepAbilities { get; set; } = new Dictionary<string, List<int>>();
            public Dictionary<string, float> debuffResistance { get; set; } = new Dictionary<string, float>();
        }

        private static void Enable(ModContext ctx)
        {
            var cfg = Load(ctx);
            if (cfg == null) return;
            var keep = new Dictionary<int, HashSet<int>>();
            foreach (var kv in cfg.keepAbilities) if (int.TryParse(kv.Key, out var id)) keep[id] = new HashSet<int>(kv.Value);
            var res = new Dictionary<int, float>();
            foreach (var kv in cfg.debuffResistance) if (int.TryParse(kv.Key, out var id)) res[id] = kv.Value;
            ctx.Log($"настройки: коллайдер {(cfg.colliderOn ? "вкл" : "выкл")}, убрать [{string.Join(", ", cfg.removeAbilities)}], оставить у {keep.Count} врагов, сопротивление у {res.Count}");

            ctx.After("C_Battle_EnemyUnit", "Start_Unit", 3, (self, args) =>
            {
                var e = (C_Battle_EnemyUnit)self;
                var list = e.List_UnitSpecialEffect;
                if (list != null)
                    foreach (var a in cfg.removeAbilities)
                    {
                        if (keep.TryGetValue(e.UnitID, out var k) && k.Contains(a)) continue;
                        while (list.Remove(a)) { }
                    }
                if (cfg.colliderOn && e.BoxCollider_ObjUnit != null) e.BoxCollider_ObjUnit.enabled = true;
                if (res.TryGetValue(e.UnitID, out var value)) SetDebuffResistance(e, value);
            });
        }

        /// <summary>Файл настроек рядом с журналом ModKit; если его нет — копия config/enemy_unlock.json из архива.</summary>
        private static Config Load(ModContext ctx)
        {
            var path = Path.Combine(Paths.Dir, "enemy_unlock.json");
            try
            {
                if (!File.Exists(path))
                {
                    var def = ctx.ReadAsset("config/enemy_unlock.json");
                    if (def == null) { ctx.Warn("в архиве нет enemy_unlock.json"); return null; }
                    File.WriteAllBytes(path, def);
                    ctx.Log("создан " + path);
                }
                return JsonSerializer.Deserialize<Config>(File.ReadAllText(path), new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            }
            catch (System.Exception ex) { ctx.Warn("ошибка в enemy_unlock.json: " + ex.Message); return null; }
        }

        /// <summary>Сопротивление дебаффам врага и коэффициенты стана/заморозки, как их считает Start_Unit (с бонусами EnemyUnitVar_*).</summary>
        private static void SetDebuffResistance(C_Battle_EnemyUnit e, float value)
        {
            float d = System.Math.Clamp((100 - Z_Database.EnemyUnitVar_DebuffResistanceRatioD) * 0.01f, 0, 1);
            float k = 1 - (value + Z_Database.EnemyUnitVar_DebuffResistanceNum) * 0.01f * (Z_Database.EnemyUnitVar_DebuffResistanceRatioU + 100) * 0.01f * d;
            k = System.Math.Clamp(k, 0, 1);
            e.UnitBase_DebuffResistance = value;
            e.UnitApply_DebuffResistance = e.UnitApply_StunResistance = e.UnitApply_FreezeResistance = k;
        }
    }
}
