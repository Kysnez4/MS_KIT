using System.Collections.Generic;
using ModKit;
using ModKitData;

namespace EffectsDemo
{
    public class DemoMod : IKitMod
    {
        public ModDef Define() => new ModDef
        {
            Id = "effects_demo", Name = "Демо: свои эффекты", Version = "1.0",
            Description = "Артефакт «Руна ModKit»: +5 снарядов пули и молний (10003), +30% урона пули (10001).",
            Depends = { ["modkit_data"] = ">=1.5", ["modkit_artifacts"] = ">=1.0", ["modkit_effects"] = ">=1.0" },
            OnEnable = ctx =>
            {
                ctx.Use<IEffects>("modkit_effects").UseBuiltin();
                var data = ctx.Use<IData>("modkit_data");
                var arts = ctx.Use<IArtifacts>("modkit_artifacts");
                data.WhenReady(() =>
                {
                    int art = arts.Add(new ArtifactDef
                    {
                        Name = "Руна ModKit", Rarity = "S", Guaranteed = true,
                        Effects = { new Effect(10003, 5), new Effect(10001, 30) },
                        Lines = new List<string> { "Снарядов пули и молний +5", "Урон пули +30%" },
                    });
                    ctx.Log($"артефакт {art}");
                });
            },
        };
    }
}
