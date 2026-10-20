using System.Collections.Generic;
using ModKit;
using ModKitData;

namespace DataDemo
{
    public class DemoMod : IKitMod
    {
        public ModDef Define() => new ModDef
        {
            Id = "data_demo", Name = "Демо: правка таблиц", Version = "1.1",
            Description = "Новый артефакт, комбинация и синергия, правка имени артефакта 41. Выключение откатывает.",
            Depends = { ["modkit_data"] = ">=1.5", ["modkit_artifacts"] = ">=1.0", ["modkit_combos"] = ">=1.0", ["modkit_synergies"] = ">=1.0" },
            Options =
            {
                new ModOption { Key = "synergy", Label = "Добавить синергию", Default = 0, Min = 0, Max = 1, Step = 1 },
                new ModOption { Key = "combo", Label = "Добавить комбинацию", Default = 0, Min = 0, Max = 1, Step = 1 },
            },
            OnEnable = ctx =>
            {
                var data = ctx.Use<IData>("modkit_data");
                var arts = ctx.Use<IArtifacts>("modkit_artifacts");
                data.WhenReady(() =>
                {
                    data.Edit("Ability", 41, new Dictionary<string, object> { ["이름"] = "ModKit тест" });
                    int art = arts.Add(new ArtifactDef
                    {
                        Name = "Меч ModKit", Rarity = "S", Guaranteed = true,
                        Effects = { new Effect(4, 50) },
                        Lines = new List<string> { "Атака +50%", "Добавлен модом на C#" },
                    });
                    int syn = ctx.Setting("synergy") > 0 ? ctx.Use<ISynergies>("modkit_synergies").Add(new SynergyDef { Name = "Синергия ModKit", Effects = { new Effect(4, 10) }, Members = new[] { art, 42 } }) : -1;
                    int combo = ctx.Setting("combo") > 0 ? ctx.Use<ICombos>("modkit_combos").Add(new ComboDef { Name = "Комбинация ModKit", Effects = { new Effect(4, 5) } }) : -1;
                    ctx.Log($"артефакт {art}, синергия {syn}, комбинация {combo}");
                });
            },
        };
    }
}
