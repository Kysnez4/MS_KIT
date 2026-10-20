using ModKit;

namespace CastProbe
{
    /// <summary>Пример API ctx.Use<ISpells>("modkit_spells"): авто-каст Огненного шара отключён, залп вручную раз в N секунд.</summary>
    public class ProbeMod : IKitMod
    {
        const int Fireball = 2;

        public ModDef Define() => new ModDef
        {
            Id = "cast_probe", Name = "Проба: ручной каст огненного шара", Version = "1.2",
            Description = "Отключает авто-каст Огненного шара и кастует его вручную каждые N секунд (API ISpells из modkit_spells).",
            Options =
            {
                new ModOption { Key = "every", Label = "Каст раз в, с (0 = как в игре)", Default = 0, Min = 0, Max = 30, Step = 1 },
                new ModOption { Key = "steps", Label = "Шагов MoveNext за каст", Default = 2, Min = 1, Max = 6, Step = 1 },
            },
            OnEnable = ctx =>
            {
                ctx.Use<ISpells>("modkit_spells").SetAuto(Fireball, false);               // вернётся само при выключении мода
                var clock = System.Diagnostics.Stopwatch.StartNew();
                double next = 0;
                ctx.Every(100, () =>
                {
                    if (clock.Elapsed.TotalSeconds < next) return;
                    double every = ctx.Setting("every");                         // 0 = перезарядка как в игре
                    double cd = every > 0 ? every : ctx.Use<ISpells>("modkit_spells").Cooldown(Fireball);
                    if (cd <= 0) cd = 1;
                    if (!ctx.Use<ISpells>("modkit_spells").Cast(Fireball, (int)ctx.Setting("steps"))) return;   // пауза/нет цели: повторим на следующем тике
                    next = clock.Elapsed.TotalSeconds + cd;
                    ctx.Log($"ручной каст, следующий через {cd:0.##} с");
                });
            },
        };
    }
}
