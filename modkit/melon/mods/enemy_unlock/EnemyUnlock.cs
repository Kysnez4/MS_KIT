using Il2Cpp;
using ModKit;

namespace EnemyUnlock
{
    /// <summary>
    /// После появления врага (Start_Unit): убирает особенности 10 (иммунитет к эффекту 100182) и 11 («босс»: иммунитет к уничтожению,
    /// коллайдер не включается) из List_UnitSpecialEffect и включает коллайдер — взрывы и артефакты бьют через OnCollisionEnter.
    /// HP боссов, уже умноженное в Start_Unit по особенности 11, остаётся. Боссам (41–46) особенность 11 оставляется —
    /// иначе её убивают эффекты мгновенного уничтожения (Unit_Destroy_Damage). Слугам (40) сопротивление дебаффам 100 → 75:
    /// коэффициенты стана/заморозки пересчитываются по формуле Start_Unit, иначе они 0 и Слуга не станится.
    /// </summary>
    public class EnemyUnlockMod : IKitMod
    {
        public ModDef Define() => new ModDef
        {
            Id = "enemy_unlock",
            Name = "Уязвимые враги",
            Description = "Всем врагам включается коллайдер, убираются особенности 10 и 11 (11 — кроме боссов 41–46), Слугам сопротивление 75.",
            Version = "1.2",
            OnEnable = ctx =>
            {
                int n = 0;
                ctx.After("C_Battle_EnemyUnit", "Start_Unit", 3, (self, args) =>
                {
                    var e = (C_Battle_EnemyUnit)self;
                    var list = e.List_UnitSpecialEffect;
                    if (list != null) { while (list.Remove(10)) { } if (!IsBoss(e.UnitID)) while (list.Remove(11)) { } }
                    if (e.UnitID == Servant) SetDebuffResistance(e, ServantResistance);
                    if (e.BoxCollider_ObjUnit != null) e.BoxCollider_ObjUnit.enabled = true;
                    if (n++ < 5 || e.UnitID == Servant || IsBoss(e.UnitID)) ctx.Log($"враг {e.UnitID}: коллайдер включён, убраны особенности {(IsBoss(e.UnitID) ? "10" : "10/11")}");
                });
            },
        };

        private const int Servant = 40;
        /// <summary>Боссы: Цитадель, Немезида, Апостол, Амадей, Наблюдатель, Ревенант.</summary>
        private static bool IsBoss(int id) => id >= 41 && id <= 46;
        private const float ServantResistance = 75;

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
