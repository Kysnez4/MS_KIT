using System;
using System.Collections.Generic;
using System.Linq;
using Il2Cpp;

namespace ModKit
{
    /// <summary>Итог боя (событие <see cref="BattleResult"/> / "battleResult" и ctx.Game.LastResult).</summary>
    public class BattleResult
    {
        /// <summary>Время выживания, секунды (Z_Database.Game_SurvivalTime).</summary>
        public double Time;
        public int Kills, AreaId, AreaLevel, ClassId;
        /// <summary>Сколько практики игра сама выдала каждому классу 1..24 за этот бой (индекс 0 = класс 1).</summary>
        public int[] PracticeGained = new int[24];
        public int PracticeGainedTotal => PracticeGained.Sum();
    }

    /// <summary>Практика классов (Z_Database.Array_AbilityClassPractice): чтение, выдача, кандидаты как у игры.</summary>
    public interface IPractice
    {
        /// <summary>Практика, при которой класс «докачан» (слот мастерства), 46.</summary>
        int Max { get; }
        /// <summary>Практика класса 1..24; -1 — массива нет.</summary>
        int Get(int classId);
        /// <summary>Добавляет практику, не выше Max. Возвращает, сколько реально добавлено. Сохраняется при ближайшем автосохранении игры.</summary>
        int Add(int classId, int amount = 1);
        /// <summary>Классы, которым игра ещё может выдать практику (меньше Max).</summary>
        IReadOnlyList<int> Candidates();
        /// <summary>Выдаёт n очков случайным кандидатам (как End_Battle). Возвращает классы, получившие очки.</summary>
        IReadOnlyList<int> GrantRandom(int n);
    }

    /// <summary>Артефакт героя: ID строки Ability и сколько раз взят.</summary>
    public class ArtifactInfo { public int Id, Count; }

    /// <summary>Герой и бой: чтение состояния игры.</summary>
    public interface IGame
    {
        /// <summary>Выбранный класс игры (Game_BattleClassID).</summary>
        int ClassId { get; }
        int AreaId { get; }
        int AreaLevel { get; }
        /// <summary>Число открытых зон (пересчитывается игрой при загрузке главного меню).</summary>
        int AreaMax { get; }
        /// <summary>Секунды выживания в текущем бою.</summary>
        double SurvivalTime { get; }
        int Kills { get; }
        /// <summary>Артефакты текущего боя (List_ArtifactObtain), с подсчётом повторов.</summary>
        IReadOnlyList<ArtifactInfo> Artifacts { get; }
        /// <summary>Заклинания героя: ID (Ability) → уровень (Z_Database.List_AllyMagicLevel), только с уровнем больше 0.</summary>
        IReadOnlyDictionary<int, int> SpellLevels { get; }
        /// <summary>Итог последнего боя или null.</summary>
        BattleResult LastResult { get; }
        IPractice Practice { get; }
    }

    internal static class GameApi
    {
        private const int PracticeMax = 46;
        private static BattleResult _last;
        private static int[] _before;
        private static double _time;
        private static bool _hooked;
        private static readonly List<Action> Cleanups = new List<Action>();

        /// <summary>Хук End_Battle: итог боя и событие battleResult. Ставится один раз при первом обращении к ctx.Game.</summary>
        internal static void EnsureHooks()
        {
            if (_hooked) return;
            _hooked = true;
            try
            {
                Hooks.AddBefore("gameapi", "C_Battle_Manager", "End_Battle", 0, (s, a) => { _before = Snapshot(); _time = Time(); }, Cleanups);
                Hooks.AddAfter("gameapi", "C_Battle_Manager", "End_Battle", 0, (s, a) =>
                {
                    var r = new BattleResult { Time = _time, Kills = Z_Database.Game_KillMonsterNum, AreaId = Z_Database.Game_AreaID, AreaLevel = Z_Database.Game_AreaLevel, ClassId = Z_Database.Game_BattleClassID };
                    var now = Snapshot();
                    if (_before != null && now != null) for (int i = 0; i < 24; i++) r.PracticeGained[i] = Math.Max(0, now[i] - _before[i]);
                    _last = r;
                    EventBus.Emit(EventBus.TypeKey(typeof(BattleResult)), r);
                    EventBus.Emit("battleResult", r);
                }, Cleanups);
            }
            catch (Exception e) { Log.Write("gameapi: хук End_Battle: " + e.Message); _hooked = false; }
        }

        private static double Time() { try { return Z_Database.Game_SurvivalTime.GetDecrypted(); } catch { return 0; } }

        private static int[] Snapshot()
        {
            try { var a = Z_Database.Array_AbilityClassPractice; var r = new int[24]; for (int i = 0; i < 24 && i < a.Length; i++) r[i] = a[i]; return r; }
            catch { return null; }
        }

        private class Practice : IPractice
        {
            private readonly Random _rnd = new Random();
            public int Max => PracticeMax;
            public int Get(int id) { try { var a = Z_Database.Array_AbilityClassPractice; return id >= 1 && id <= 24 && id - 1 < a.Length ? a[id - 1] : -1; } catch { return -1; } }
            public int Add(int id, int amount = 1)
            {
                var a = Z_Database.Array_AbilityClassPractice;
                if (a == null || id < 1 || id > 24 || id - 1 >= a.Length || amount <= 0) return 0;
                int cur = a[id - 1], add = Math.Max(0, Math.Min(amount, PracticeMax - cur));
                a[id - 1] = cur + add;
                return add;
            }
            public IReadOnlyList<int> Candidates()
            {
                var l = new List<int>(); var a = Z_Database.Array_AbilityClassPractice;
                if (a != null) for (int i = 0; i < 24 && i < a.Length; i++) if (a[i] >= 0 && a[i] < PracticeMax) l.Add(i + 1);
                return l;
            }
            public IReadOnlyList<int> GrantRandom(int n)
            {
                var got = new List<int>();
                for (; n > 0; n--) { var c = Candidates(); if (c.Count == 0) break; int id = c[_rnd.Next(c.Count)]; if (Add(id) > 0) got.Add(id); }
                return got;
            }
        }

        private class Game : IGame
        {
            private readonly Practice _p = new Practice();
            public int ClassId => Z_Database.Game_BattleClassID;
            public int AreaId => Z_Database.Game_AreaID;
            public int AreaLevel => Z_Database.Game_AreaLevel;
            public int AreaMax => Z_Database.Game_AreaMaxNum;
            public double SurvivalTime => Time();
            public int Kills => Z_Database.Game_KillMonsterNum;
            public IReadOnlyList<ArtifactInfo> Artifacts
            {
                get
                {
                    var res = new List<ArtifactInfo>();
                    try
                    {
                        var l = Z_Database.List_ArtifactObtain; var cnt = new Dictionary<int, int>();
                        for (int i = 0; i < l.Count; i++) { int id = l[i]; cnt[id] = cnt.TryGetValue(id, out var c) ? c + 1 : 1; }
                        foreach (var kv in cnt) res.Add(new ArtifactInfo { Id = kv.Key, Count = kv.Value });
                    }
                    catch { /* списка нет */ }
                    return res;
                }
            }
            public IReadOnlyDictionary<int, int> SpellLevels
            {
                get
                {
                    var d = new Dictionary<int, int>();
                    try { var l = Z_Database.List_AllyMagicLevel; for (int i = 0; i < l.Count; i++) if (l[i] > 0) d[i] = l[i]; } catch { /* списка нет */ }
                    return d;
                }
            }
            public BattleResult LastResult => _last;
            public IPractice Practice => _p;
        }

        internal static IGame Instance() { EnsureHooks(); return new Game(); }
    }
}
