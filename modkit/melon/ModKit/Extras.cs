using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Il2Cpp;
using UnityEngine;

namespace ModKit
{
    // ------------------------------------------------------------ Таймеры по игровому времени

    internal class GameTimer : IGameTimer
    {
        private readonly System.Diagnostics.Stopwatch _real = System.Diagnostics.Stopwatch.StartNew();
        private double _lastReal, _now;
        private readonly List<(double at, double every, Action fn)> _jobs = new List<(double, double, Action)>();
        private readonly Action<string, Action> _guard;
        public double Delta { get; private set; }
        public double Now => _now;

        public GameTimer(Action<double, Action> every, Action<string, Action> guard, List<Action> cleanups)
        {
            _guard = guard;
            every(100, Step);
            cleanups.Add(() => _jobs.Clear());
        }

        private void Step()
        {
            double r = _real.Elapsed.TotalSeconds;
            Delta = r - _lastReal; _lastReal = r;
            if (BattleState.Paused) { Delta = 0; return; }
            _now += Delta;
            for (int i = 0; i < _jobs.Count; i++)
            {
                var j = _jobs[i];
                if (_now < j.at) continue;
                var fn = j.fn;
                if (j.every > 0) _jobs[i] = (_now + j.every, j.every, fn); else { _jobs.RemoveAt(i); i--; }
                _guard("таймер", fn);
            }
        }

        public void After(double seconds, Action fn) => _jobs.Add((_now + seconds, 0, fn));
        public void Every(double seconds, Action fn) => _jobs.Add((_now + seconds, seconds, fn));
    }

    /// <summary>Таймеры по игровому времени: на паузе боя стоят.</summary>
    public interface IGameTimer
    {
        /// <summary>Игровое время с запуска мода, с.</summary>
        double Now { get; }
        void After(double seconds, Action fn);
        void Every(double seconds, Action fn);
        /// <summary>Сколько игрового времени прошло с прошлого шага таймера (0 на паузе).</summary>
        double Delta { get; }
    }

    // ------------------------------------------------------------ Хранилище

    /// <summary>Постоянное хранилище мода (JSON в UserData/ModKit/store/&lt;id&gt;.json). Значения: числа, строки, bool, списки и объекты, которые понимает System.Text.Json.</summary>
    public interface IStore
    {
        T Get<T>(string key, T def = default);
        void Set<T>(string key, T value);
        bool Has(string key);
        void Remove(string key);
    }

    internal class StoreImpl : IStore
    {
        private readonly string _path;
        private readonly Dictionary<string, JsonElement> _d = new Dictionary<string, JsonElement>();
        public StoreImpl(string modId)
        {
            var dir = Path.Combine(Paths.Dir, "store");
            try { Directory.CreateDirectory(dir); } catch { }
            _path = Path.Combine(dir, modId + ".json");
            try { if (File.Exists(_path)) foreach (var kv in JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(_path))) _d[kv.Key] = kv.Value; }
            catch (Exception e) { Log.Write($"store {modId}: чтение: {e.Message}"); }
        }
        public T Get<T>(string key, T def = default)
        {
            try { return _d.TryGetValue(key, out var v) ? v.Deserialize<T>() : def; } catch { return def; }
        }
        public void Set<T>(string key, T value)
        {
            _d[key] = JsonSerializer.SerializeToElement(value);
            try { File.WriteAllText(_path, JsonSerializer.Serialize(_d)); } catch (Exception e) { Log.Write("store: запись: " + e.Message); }
        }
        public bool Has(string key) => _d.ContainsKey(key);
        public void Remove(string key) { if (_d.Remove(key)) try { File.WriteAllText(_path, JsonSerializer.Serialize(_d)); } catch { } }
    }

    // ------------------------------------------------------------ Теги

    /// <summary>Общие теги («аспекты») на заклинаниях, артефактах, врагах: kind — "spell"/"artifact"/"enemy" или свой, id — число в строке.</summary>
    public interface ITags
    {
        void Set(string kind, int id, params string[] tags);
        IReadOnlyCollection<string> Of(string kind, int id);
        IReadOnlyList<int> With(string kind, string tag);
    }

    internal class TagsImpl : ITags
    {
        private static readonly Dictionary<(string, int), HashSet<string>> All = new Dictionary<(string, int), HashSet<string>>();
        public void Set(string kind, int id, params string[] tags) { All[(kind, id)] = new HashSet<string>(tags); }
        public IReadOnlyCollection<string> Of(string kind, int id) => All.TryGetValue((kind, id), out var s) ? s : (IReadOnlyCollection<string>)new string[0];
        public IReadOnlyList<int> With(string kind, string tag) => All.Where(kv => kv.Key.Item1 == kind && kv.Value.Contains(tag)).Select(kv => kv.Key.Item2).ToList();
    }

    // ------------------------------------------------------------ UI: подпись и полоса

    public static partial class UiKit
    {
        public class HudBar
        {
            internal GameObject Root; internal RectTransform Fill; internal UnityEngine.UI.Text Label; internal double W;
            public bool Alive => Root != null;
            public void Set(double fraction, string text)
            {
                if (Fill == null) return;
                var sd = Fill.sizeDelta; Fill.sizeDelta = new Vector2((float)(W * K * Math.Max(0, Math.Min(1, fraction))), sd.y);
                if (Label != null && Label.text != text) Label.text = text;
            }
            public void Destroy() { SafeDestroy(Root); Root = null; Fill = null; Label = null; }
        }

        /// <summary>Подпись на экране, не перехватывающая касания.</summary>
        public static HudBar AddHudLabel(string name, string text, double x, double y, double w, double h, double size, double[] color = null)
        {
            EnsureHudCanvas();
            var root = Node("ModKitHud_" + name, CanvasTf); Stretch(root.rt);
            var t = Label(root.rt, text, x, y, w, h, size, 4);
            t.raycastTarget = false;
            if (color != null) t.color = Col(color[0], color[1], color[2], color.Length > 3 ? color[3] : 1);
            return new HudBar { Root = root.go, Label = t, W = w };
        }

        /// <summary>Полоса заполнения с подписью (не перехватывает касания).</summary>
        public static HudBar AddHudBar(string name, double x, double y, double w, double h, double[] color)
        {
            EnsureHudCanvas();
            var root = Node("ModKitHud_bar_" + name, CanvasTf); Stretch(root.rt);
            var back = Node("back", root.rt); Place(back.rt, x, y, w, h);
            AddImage(back.go, 0, 0, 0, 0.55).raycastTarget = false;
            var fill = Node("fill", root.rt); Place(fill.rt, x, y, w, h);
            AddImage(fill.go, color[0], color[1], color[2], color.Length > 3 ? color[3] : 0.9).raycastTarget = false;
            var t = Label(root.rt, "", x, y, w, h, Math.Max(14, h * 0.7), 4); t.raycastTarget = false;
            return new HudBar { Root = root.go, Fill = fill.rt, Label = t, W = w };
        }

        private static void EnsureHudCanvas()
        {
            if (CanvasTf != null && CanvasTf.gameObject.activeInHierarchy) return;
            var c = MenuUi.FindGameCanvas();
            float scale = c.scaleFactor;
            K = Screen.width / scale / LogicalW;
            ScreenH = Screen.height / scale / K;
            CanvasTf = c.transform;
        }
    }
}
