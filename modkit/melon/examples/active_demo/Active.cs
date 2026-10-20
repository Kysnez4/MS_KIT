using ModKit;

namespace ActiveDemo
{
    /// <summary>Активные заклинания по кнопкам на экране боя через ctx.Use<ISpells>("modkit_spells").AddActive. Огненный шар проверен, Молния и Метеорит — нет.</summary>
    public class ActiveMod : IKitMod
    {
        public ModDef Define() => new ModDef
        {
            Id = "active_demo", Name = "Активные заклинания (демо)", Version = "2.0",
            Description = "Огненный шар / Молния / Метеорит только по кнопкам на экране боя, у каждой своя перезарядка.",
            Options =
            {
                new ModOption { Key = "fireball", Label = "Огненный шар по кнопке (1/0)", Default = 1, Min = 0, Max = 1, Step = 1 },
                new ModOption { Key = "thunder", Label = "Молния по кнопке (1/0, не проверено)", Default = 0, Min = 0, Max = 1, Step = 1 },
                new ModOption { Key = "meteor", Label = "Метеорит по кнопке (1/0, не проверено)", Default = 0, Min = 0, Max = 1, Step = 1 },
                new ModOption { Key = "cooldown", Label = "Перезарядка кнопки, с", Default = 8, Min = 1, Max = 60, Step = 1 },
                new ModOption { Key = "burst", Label = "Залпов за нажатие", Default = 3, Min = 1, Max = 10, Step = 1 },
            },
            OnEnable = ctx =>
            {
                int slot = 0;
                void Add(string key, int id, string label, double[] color)
                {
                    if (ctx.Setting(key) < 1) return;
                    ctx.Use<ISpells>("modkit_spells").AddActive(new ActiveSpellDef
                    {
                        SpellId = id, Label = label, Cooldown = ctx.Setting("cooldown"), Burst = (int)ctx.Setting("burst"), Slot = slot++, Color = color,
                        OnCast = n => ctx.Log($"{label}: залпов {n}"),
                    });
                }
                Add("fireball", 2, "Огонь", new[] { 0.75, 0.25, 0.05, 0.9 });
                Add("thunder", 7, "Гром", new[] { 0.25, 0.35, 0.85, 0.9 });
                Add("meteor", 10, "Метеор", new[] { 0.45, 0.2, 0.15, 0.9 });
            },
        };
    }
}
