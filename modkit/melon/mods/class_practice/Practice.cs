using System;


using ModKit;

namespace ClassPractice
{
    /// <summary>
    /// Игра выдаёт практику классов в End_Battle только в ветке «новый журнал» (первое прохождение зоны, PRACTICE.md, AREAS.md).
    /// Мод после End_Battle добирает недостающее: очков = min(лимит, floor(время выживания / минут на очко)), минус то, что игра выдала сама.
    /// Очко получает случайный класс 1..24 с практикой ≤ 45 (как в игре); максимум практики 46.
    /// </summary>
    public class PracticeMod : IKitMod
    {
        private const int Max = 46;

        public ModDef Define() => new ModDef
        {
            Id = "class_practice", Name = "Практика за время боя", Version = "1.1",
            Description = "Практика классов после каждого боя: 1 очко за каждые N минут выживания (до лимита), независимо от первого прохождения.",
            Options =
            {
                new ModOption { Key = "minutes", Label = "Минут выживания на очко", Default = 5, Min = 1, Max = 60, Step = 1 },
                new ModOption { Key = "limit", Label = "Максимум очков за бой", Default = 2, Min = 1, Max = 10, Step = 1 },
            },
            OnEnable = ctx =>
            {
                // итог боя приходит из ядра (ctx.Game, событие BattleResult): время, число очков, которые игра выдала сама
                ctx.Events.On<BattleResult>(r => ctx.Guard("практика после боя", () =>
                {
                    int want = (int)Math.Min(ctx.Setting("limit"), Math.Floor(r.Time / (ctx.Setting("minutes") * 60)));
                    int extra = want - r.PracticeGainedTotal;
                    ctx.Log($"бой {r.Time:0} с: положено {want}, игра выдала {r.PracticeGainedTotal}, добавляю {Math.Max(0, extra)}");
                    if (extra <= 0) return;
                    foreach (var id in ctx.Game.Practice.GrantRandom(extra)) ctx.Log($"+1 практика классу {id} (теперь {ctx.Game.Practice.Get(id)})");
                }));
                ctx.Game.ToString();   // обращение включает хук итога боя в ядре
            },
        };
    }
}
