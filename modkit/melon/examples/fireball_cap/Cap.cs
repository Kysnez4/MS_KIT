using Il2Cpp;
using ModKit;

namespace FireballCap
{
    /// <summary>Проба: Harmony на MoveNext итератора Play_Fireball. Перед шагом корутины число шаров героя режется до предела, после — возвращается настоящее.</summary>
    public class CapMod : IKitMod
    {
        static float _real;
        static bool _cut;
        static int _calls;

        static C_Battle_AllyUnit Hero(object it) => (C_Battle_AllyUnit)it.GetType().GetProperty("__4__this").GetValue(it);

        public ModDef Define() => new ModDef
        {
            Id = "fireball_cap", Name = "Проба: лимит огненных шаров", Version = "1.0",
            Description = "Режет число огненных шаров за залп на время MoveNext, потом возвращает.",
            Options = { new ModOption { Key = "cap", Label = "Предел шаров", Default = 3, Min = 1, Max = 30, Step = 1 } },
            OnEnable = ctx =>
            {
                const string cls = "C_Battle_AllyUnit+_Play_Fireball_d__1134";
                ctx.Before(cls, "MoveNext", 0, (self, args) =>
                {
                    var h = Hero(self);
                    float cap = (float)ctx.Setting("cap");
                    _real = h.UnitApply_FireballNum;
                    _cut = _real > cap;
                    if (_cut) h.UnitApply_FireballNum = cap;
                    if (_calls++ < 12) ctx.Log($"MoveNext: число {_real}, предел {cap}, режу={_cut}");
                });
                ctx.After(cls, "MoveNext", 0, (self, args) =>
                {
                    if (_cut) Hero(self).UnitApply_FireballNum = _real;
                    _cut = false;
                });
                ctx.Log("fireball_cap включён");
            },
        };
    }
}
