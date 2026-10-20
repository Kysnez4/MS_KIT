using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Il2Cpp;
using UnityEngine;

namespace ModKit
{
    // ------------------------------------------------------------ Исследования

    /// <summary>Дерево исследований («Тауматикон»): узлы с ценой в очках и условиями, прогресс хранится в ctx.Store.</summary>
    public interface IResearch
    {
        /// <summary>Описывает узел. Повторное описание того же id заменяет его.</summary>
        void Define(string id, int cost, params string[] requires);
        IReadOnlyList<string> All { get; }
        int Points { get; }
        void AddPoints(int n);
        bool Unlocked(string id);
        /// <summary>Все условия открыты и хватает очков.</summary>
        bool CanUnlock(string id);
        /// <summary>Открывает узел, списывая очки. Событие "research:unlocked" (EventData: id).</summary>
        bool Unlock(string id);
    }

    internal class ResearchImpl : IResearch
    {
        private readonly IStore _s; private readonly ModContext _c;
        private readonly Dictionary<string, (int cost, string[] req)> _d = new Dictionary<string, (int, string[])>();
        public ResearchImpl(ModContext c) { _c = c; _s = c.Store; }
        public void Define(string id, int cost, params string[] requires) { _d[id] = (cost, requires ?? new string[0]); }
        public IReadOnlyList<string> All => _d.Keys.ToList();
        public int Points => _s.Get("research.points", 0);
        public void AddPoints(int n) { _s.Set("research.points", Math.Max(0, Points + n)); }
        public bool Unlocked(string id) => _s.Get("research.done", new List<string>()).Contains(id);
        public bool CanUnlock(string id) => _d.TryGetValue(id, out var d) && !Unlocked(id) && Points >= d.cost && d.req.All(Unlocked);
        public bool Unlock(string id)
        {
            if (!CanUnlock(id)) return false;
            var done = _s.Get("research.done", new List<string>()); done.Add(id);
            _s.Set("research.done", done);
            AddPoints(-_d[id].cost);
            _c.Events.Emit("research:unlocked", new EventData { ["id"] = id });
            return true;
        }
    }
}
