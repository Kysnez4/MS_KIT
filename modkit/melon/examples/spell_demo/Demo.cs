using ModKit;
using ModKitData;

namespace SpellDemo
{
    public class DemoMod : IKitMod
    {
        public ModDef Define() => new ModDef
        {
            Id = "spell_demo", Name = "Демо: правка заклинаний", Version = "1.0",
            Description = "Магическая пуля: урон x2, +2 пули на уровне 2, новое имя.",
            Depends = { ["modkit_data"] = ">=1.5", ["modkit_spells_data"] = ">=1.0" },
            Options = { new ModOption { Key = "mult", Label = "Множитель урона", Default = 2, Min = 1, Max = 10, Step = 1 } },
            OnEnable = ctx =>
            {
                var data = ctx.Use<IData>("modkit_data");
                var spells = ctx.Use<ISpellsData>("modkit_spells_data");
                data.WhenReady(() =>
                {
                    var before = spells.Get(1);
                    foreach (var kv in before.Slots) ctx.Log($"слот {kv.Key}: эффект {kv.Value.Id} = {kv.Value.Value}");
                    spells.Edit(1, new SpellChanges
                    {
                        Name = before.Name + " (мод)",
                        Scale = { [151] = ctx.Setting("mult") },                 // 151: урон магических пуль +%
                        Slots = { [2] = new Effect(153, 2) },                    // слот 2 (уровень 2): число пуль +2 вместо +1
                    });
                    ctx.Log("заклинание 1 изменено");
                });
            },
        };
    }
}
