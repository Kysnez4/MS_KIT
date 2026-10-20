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
    // ------------------------------------------------------------ Resource + HUD

    /// <summary>Собственный ресурс мода («Надежда», «Вис»): значение 0..Max, восстановление в секунду по игровому времени, полоса на экране.</summary>
    public interface IResource
    {
        double Value { get; }
        double Max { get; set; }
        /// <summary>Восстановление в секунду.</summary>
        double Regen { get; set; }
        /// <summary>Списывает amount, если хватает. false — не хватило, ничего не списано.</summary>
        bool TrySpend(double amount);
        void Add(double amount);
        /// <summary>Показать полосу на экране (x, y, w, h в логических пикселях). Убирается при выключении мода.</summary>
        void ShowBar(string label, double x, double y, double w, double h, double[] color);
    }

    internal class ResourceImpl : IResource
    {
        private readonly string _name;
        private UiKit.HudBar _bar;
        private string _label, _lastTxt;
        private double[] _col; private double _x, _y, _w, _h;
        private bool _show;
        public double Value { get; private set; }
        public double Max { get; set; }
        public double Regen { get; set; }

        public ResourceImpl(string name, double max, double regen, Action<double, Action> every, IGameTimer timer, ModEvents ev, List<Action> cleanups)
        {
            _name = name; Max = max; Regen = regen; Value = max;
            double last = 0;
            every(500, () =>
            {
                double dt = timer.Delta; last += dt;
                if (Regen > 0 && Value < Max) Value = Math.Min(Max, Value + Regen * dt);
                Tick();
            });
            cleanups.Add(() => { _bar?.Destroy(); _bar = null; });
            ev.On("battleStart", _ => Value = Max);
        }

        private void Tick()
        {
            if (!_show) return;
            bool inBattle = BattleState.InBattle;
            if (inBattle && (_bar == null || !_bar.Alive)) _bar = UiKit.AddHudBar(_name, _x, _y, _w, _h, _col);
            else if (!inBattle && _bar != null) { _bar.Destroy(); _bar = null; }
            string txt = $"{_label} {Math.Floor(Value)}/{Math.Floor(Max)}";
            if (txt == _lastTxt && _bar != null) return;                      // ничего не изменилось — интерфейс не трогаем
            _lastTxt = txt;
            _bar?.Set(Max > 0 ? Value / Max : 0, txt);
        }

        public bool TrySpend(double a) { if (a > Value + 1e-9) return false; Value -= a; return true; }
        public void Add(double a) { Value = Math.Max(0, Math.Min(Max, Value + a)); }
        public void ShowBar(string label, double x, double y, double w, double h, double[] color)
        { _label = label; _x = x; _y = y; _w = w; _h = h; _col = color ?? new[] { 0.2, 0.5, 0.95, 0.9 }; _show = true; }
    }
}
