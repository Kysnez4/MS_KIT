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
    // ------------------------------------------------------------ Зоны

    public interface IZone { bool Alive { get; } void Remove(); }

    /// <summary>Зона с эффектом на время: каждые tick секунд вызывается onTick со списком врагов в радиусе.</summary>
    public interface IZones
    {
        IZone Create(double x, double y, double radius, double duration, double tick, Action<IReadOnlyList<IEnemy>> onTick);
    }

    internal class ZonesImpl : IZones
    {
        private readonly IGameTimer _t; private readonly IEnemies _e; private readonly Action<string, Action> _guard;
        private readonly List<ZoneItem> _all = new List<ZoneItem>();
        private class ZoneItem : IZone { public bool Dead; public bool Alive => !Dead; public void Remove() { Dead = true; } }
        public ZonesImpl(IGameTimer t, IEnemies e, Action<string, Action> guard, List<Action> cleanups) { _t = t; _e = e; _guard = guard; cleanups.Add(() => { foreach (var z in _all) z.Dead = true; }); }

        public IZone Create(double x, double y, double radius, double duration, double tick, Action<IReadOnlyList<IEnemy>> onTick)
        {
            var z = new ZoneItem(); _all.Add(z);
            double end = _t.Now + duration; tick = Math.Max(0.1, tick);
            void Step()
            {
                if (z.Dead || _t.Now > end) { z.Dead = true; _all.Remove(z); return; }
                _guard("зона", () => onTick(_e.Near(x, y, radius)));
                _t.After(tick, Step);
            }
            _t.After(tick, Step);
            return z;
        }
    }
}
