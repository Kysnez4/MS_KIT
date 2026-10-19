using System;
using System.Collections.Generic;
using Il2Cpp;
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
            Id = "class_practice", Name = "Практика за время боя", Version = "1.0",
            Description = "Практика классов после каждого боя: 1 очко за каждые N минут выживания (до лимита), независимо от первого прохождения.",
            Options =
            {
                new ModOption { Key = "minutes", Label = "Минут выживания на очко", Default = 5, Min = 1, Max = 60, Step = 1 },
                new ModOption { Key = "limit", Label = "Максимум очков за бой", Default = 2, Min = 1, Max = 10, Step = 1 },
            },
            OnEnable = ctx =>
            {
                int[] before = null; double time = 0;
                ctx.Before("C_Battle_Manager", "End_Battle", 0, (self, args) => ctx.Guard("практика до боя", () =>
                {
                    before = Snapshot();
                    time = Z_Database.Game_SurvivalTime.GetDecrypted();
                }));
                ctx.After("C_Battle_Manager", "End_Battle", 0, (self, args) => ctx.Guard("практика после боя", () =>
                {
                    var arr = Z_Database.Array_AbilityClassPractice;
                    if (before == null || arr == null) return;
                    int gaveByGame = 0;
                    for (int i = 0; i < 24 && i < arr.Length; i++) gaveByGame += Math.Max(0, arr[i] - before[i]);
                    int want = (int)Math.Min(ctx.Setting("limit"), Math.Floor(time / (ctx.Setting("minutes") * 60)));
                    int extra = want - gaveByGame;
                    ctx.Log($"бой {time:0} с: положено {want}, игра выдала {gaveByGame}, добавляю {Math.Max(0, extra)}");
                    var rnd = new Random();
                    for (; extra > 0; extra--)
                    {
                        var pool = new List<int>();
                        for (int i = 0; i < 24 && i < arr.Length; i++) if (arr[i] >= 0 && arr[i] < Max) pool.Add(i);
                        if (pool.Count == 0) { ctx.Log("все классы докачаны"); break; }
                        int k = pool[rnd.Next(pool.Count)];
                        arr[k] = arr[k] + 1;
                        ctx.Log($"+1 практика классу {k + 1} (теперь {arr[k]})");
                    }
                    before = null;
                }));
            },
        };

        private static int[] Snapshot()
        {
            var a = Z_Database.Array_AbilityClassPractice;
            var r = new int[24];
            for (int i = 0; i < 24 && i < a.Length; i++) r[i] = a[i];
            return r;
        }
    }
}
