using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ModKit
{
    public class ModState
    {
        public ModDef Def;
        public bool Enabled;
        public Dictionary<string, double> Settings = new Dictionary<string, double>();
        public List<Action> Cleanups = new List<Action>();
        public ModContext Ctx;
        /// <summary>Мод сейчас запущен (OnEnable выполнен). Enabled — желание пользователя: мод может быть включён, но заблокирован.</summary>
        public bool Active;
        /// <summary>Постоянная причина, по которой мод не может запуститься: нет зависимости, не та версия, цикл.</summary>
        public string Problem;
        /// <summary>Причина последнего отказа при запуске: конфликт с другим модом, зависимость не запустилась.</summary>
        public string Fault;
        public List<string> Warnings = new List<string>();
        public object Api;
    }

    public class SessionDiff
    {
        /// <summary>Названия модов, включённых в прошлой сессии.</summary>
        public List<string> Before;
        /// <summary>Названия модов, включённых сейчас.</summary>
        public List<string> Now;
    }

    internal class Saved
    {
        public Dictionary<string, bool> enabled { get; set; } = new Dictionary<string, bool>();
        public Dictionary<string, Dictionary<string, double>> settings { get; set; } = new Dictionary<string, Dictionary<string, double>>();
        /// <summary>Набор модов, с которым игра запускалась и применялась в прошлый раз.</summary>
        public Dictionary<string, bool> session { get; set; }
        /// <summary>Запуск ещё не подтверждён как стабильный: если флаг найден на старте, прошлая игра упала.</summary>
        public bool booting { get; set; }
    }

    /// <summary>Реестр модов: включение/выключение, зависимости, конфликты, откат при сбое, config.json.</summary>
    public static class Core
    {
        public static readonly List<ModState> Mods = new List<ModState>();
        private static Saved _saved = new Saved();
        private static bool _ready;
        /// <summary>Порядок запуска: зависимости раньше зависимых (считается в ResolveAll).</summary>
        private static List<ModState> _startOrder = new List<ModState>();
        private static SessionDiff _startupDiff;
        private const double StableAfterSec = 30;
        /// <summary>Через столько секунд после ручного включения/выключения мода набор считается стабильным (принудительное закрытие раньше откатит изменение).</summary>
        private const double ToggleStableSec = 20;
        private static bool _toggled;
        private static double _stableAt = -1;
        private static bool _reverting;

        public static string ModVersion(ModState st) => st.Def.Version ?? st.Def.Meta?.Version;

        // ---------------------------------------------------------------- версии

        private static int[] ParseVer(string v) => Regex.Split(v, @"[.\-+]").Select(p => int.TryParse(p, out var n) ? n : 0).ToArray();

        public static int CompareVersions(string a, string b)
        {
            var x = ParseVer(a); var y = ParseVer(b);
            for (int i = 0; i < Math.Max(x.Length, y.Length); i++)
            {
                int d = (i < x.Length ? x[i] : 0) - (i < y.Length ? y[i] : 0);
                if (d != 0) return d < 0 ? -1 : 1;
            }
            return 0;
        }

        /// <summary>Версия подходит под диапазон: «*», «1.2» (не ниже), «&gt;=1.0 &lt;2.0», «=1.5».</summary>
        public static bool Satisfies(string version, string range)
        {
            foreach (Match m in Regex.Matches(range, @"(>=|<=|>|<|=)?\s*(\d[\w.\-+]*|\*)"))
            {
                if (m.Groups[2].Value == "*") continue;
                if (version == null) return false;
                int c = CompareVersions(version, m.Groups[2].Value);
                bool ok;
                switch (m.Groups[1].Success ? m.Groups[1].Value : ">=")
                {
                    case ">=": ok = c >= 0; break;
                    case "<=": ok = c <= 0; break;
                    case ">": ok = c > 0; break;
                    case "<": ok = c < 0; break;
                    default: ok = c == 0; break;
                }
                if (!ok) return false;
            }
            return true;
        }

        // ---------------------------------------------------------------- зависимости

        /// <summary>Пересчитывает постоянные проблемы зависимостей и порядок запуска. Вызывать при любом изменении набора модов.</summary>
        private static void ResolveAll()
        {
            var byId = Mods.ToDictionary(m => m.Def.Id);
            foreach (var m in Mods) m.Problem = null;
            foreach (var m in Mods)
            {
                foreach (var kv in m.Def.Depends)
                {
                    var need = kv.Value == "*" ? "" : " " + kv.Value;
                    if (!byId.TryGetValue(kv.Key, out var d)) { m.Problem ??= $"нужен мод «{kv.Key}»{need}"; continue; }
                    if (!Satisfies(ModVersion(d), kv.Value))
                        m.Problem ??= $"нужен «{d.Def.Name}»{need}, установлена {ModVersion(d) ?? "без версии"}";
                }
            }
            var state = new Dictionary<ModState, int>();
            var order = new List<ModState>();
            var stack = new List<ModState>();
            void Visit(ModState m)
            {
                if (state.TryGetValue(m, out var s) && s == 2) return;
                state[m] = 1;
                stack.Add(m);
                var edges = new List<(ModState, bool)>();
                foreach (var id in m.Def.Depends.Keys) if (byId.TryGetValue(id, out var d)) edges.Add((d, true));
                foreach (var id in m.Def.LoadAfter) if (byId.TryGetValue(id, out var d)) edges.Add((d, false));
                foreach (var (d, hard) in edges)
                {
                    if (state.TryGetValue(d, out var ds) && ds == 1)
                    {
                        if (!hard) continue;
                        var cycle = stack.Skip(stack.IndexOf(d)).ToList();
                        var text = "циклическая зависимость: " + string.Join(" → ", cycle.Concat(new[] { d }).Select(x => x.Def.Id));
                        foreach (var c in cycle) c.Problem ??= text;
                        continue;
                    }
                    Visit(d);
                    if (hard && d.Problem != null) m.Problem ??= $"зависимость «{d.Def.Name}» недоступна: {d.Problem}";
                }
                stack.RemoveAt(stack.Count - 1);
                state[m] = 2;
                order.Add(m);
            }
            foreach (var m in Mods) Visit(m);
            _startOrder = order;
        }

        /// <summary>Включённые, но не запущенные моды, у которых препятствие исчезло (новая зависимость, освободился метод).</summary>
        private static void RetryBlocked()
        {
            if (!_ready) return;
            foreach (var m in _startOrder.ToList()) if (m.Enabled && !m.Active && m.Problem == null) RunEnable(m);
        }

        private static List<ModState> DependentsOf(ModState st) => Mods.Where(m => m.Enabled && m.Def.Depends.ContainsKey(st.Def.Id)).ToList();

        /// <summary>Моды, которые сломаются без этого (включённые зависимые, в том числе через цепочку).</summary>
        public static List<string> DependentNames(ModState st) => DependentSet(st).Select(m => m.Def.Name).ToList();

        private static HashSet<ModState> DependentSet(ModState st)
        {
            var set = new HashSet<ModState>();
            void Walk(ModState m) { foreach (var d in DependentsOf(m)) if (set.Add(d)) Walk(d); }
            Walk(st);
            return set;
        }

        // ---------------------------------------------------------------- config.json и сессии

        private static void Load()
        {
            try
            {
                if (!File.Exists(Paths.ConfigPath)) return;
                var s = JsonSerializer.Deserialize<Saved>(File.ReadAllText(Paths.ConfigPath));
                if (s != null) _saved = s;
                _saved.enabled ??= new Dictionary<string, bool>();
                _saved.settings ??= new Dictionary<string, Dictionary<string, double>>();
            }
            catch (Exception e) { Log.Write("config.json повреждён, игнорирую: " + e.Message); }
        }

        private static void Save()
        {
            _saved.enabled = Mods.ToDictionary(m => m.Def.Id, m => m.Enabled);
            _saved.settings = Mods.ToDictionary(m => m.Def.Id, m => new Dictionary<string, double>(m.Settings));
            try { File.WriteAllText(Paths.ConfigPath, JsonSerializer.Serialize(_saved, new JsonSerializerOptions { WriteIndented = true })); }
            catch (Exception e) { Log.Write("не удалось сохранить config.json: " + e.Message); }
        }

        private static Dictionary<string, bool> CurrentEnabled() => Mods.ToDictionary(m => m.Def.Id, m => m.Enabled);

        /// <summary>Набор модов расходится с прошлой применённой сессией? Определяется один раз при старте.</summary>
        public static SessionDiff SessionDiff() => _startupDiff;

        private static SessionDiff ComputeDiff()
        {
            var prev = _saved.session;
            if (prev == null) return null;
            var cur = CurrentEnabled();
            var ids = new HashSet<string>(prev.Keys.Concat(cur.Keys));
            bool differs = ids.Any(id => (prev.TryGetValue(id, out var a) && a) != (cur.TryGetValue(id, out var b) && b));
            if (!differs) return null;
            List<string> Names(Dictionary<string, bool> set) => Mods.Where(m => set.TryGetValue(m.Def.Id, out var v) && v).Select(m => m.Def.Name).ToList();
            return new SessionDiff { Before = Names(prev), Now = Names(cur) };
        }

        /// <summary>Запоминает текущий набор модов как «применённый» (при старте без расхождений, после выбора и перед выходом).</summary>
        public static void MarkSession()
        {
            _saved.session = CurrentEnabled();
            _saved.booting = false;
            _startupDiff = null;
            Save();
        }

        /// <summary>Вернуть моды к набору прошлой сессии.</summary>
        public static void RestoreSession()
        {
            var prev = _saved.session ?? new Dictionary<string, bool>();
            foreach (var st in _startOrder.ToList()) SetEnabled(st, prev.TryGetValue(st.Def.Id, out var v) && v);
            MarkSession();
        }

        private static void RevertToPrevious(string reason)
        {
            if (_reverting || _saved.session == null) return;
            _reverting = true;
            try
            {
                Log.Write($"ОШИБКА: {reason}. Моды сброшены к набору прошлого запуска");
                RestoreSession();
            }
            finally { _reverting = false; }
        }

        // ---------------------------------------------------------------- контекст мода

        private class Context : ModContext
        {
            private readonly ModState _st;
            public Context(ModState st) { _st = st; Events = EventBus.For(st.Def.Id, () => st.Enabled, st.Cleanups); }
            public string Id => _st.Def.Id;
            public IReadOnlyDictionary<string, double> Settings => _st.Settings;
            public double Setting(string key) => _st.Settings.TryGetValue(key, out var v) ? v : 0;
            public void SetSetting(string key, double value) { _st.Settings[key] = value; Save(); }
            public void Log(string msg) => ModKit.Log.Write($"[{Id}] {msg}");
            public void Warn(string msg) { _st.Warnings.Add(msg); ModKit.Log.Write($"[{Id}] предупреждение: {msg}"); }
            public ModEvents Events { get; }
            public void Expose(object api) { _st.Api = api; }
            public T Mod<T>(string id) where T : class => Mods.FirstOrDefault(m => m.Def.Id == id && m.Active)?.Api as T;

            public void Replace(string cls, string method, int n, Func<object, object[], object> impl) => Hooks.AddReplace(Id, cls, method, n, impl, _st.Cleanups);

            public void After(string cls, string method, int n, Action<object, object[]> fn) =>
                Hooks.AddAfter(Id, cls, method, n, (s, a) => { if (_st.Enabled) fn(s, a); }, _st.Cleanups);

            public void Before(string cls, string method, int n, Action<object, object[]> fn) =>
                Hooks.AddBefore(Id, cls, method, n, (s, a) => { if (_st.Enabled) fn(s, a); }, _st.Cleanups);

            public void Every(double ms, Action fn)
            {
                var t = Scheduler.Every(ms, () =>
                {
                    if (!_st.Enabled) return;
                    Crash.Enter(Id, "таймер Every");
                    try { fn(); } catch (Exception e) { Crash.Error(Id, "Every", e); }
                    Crash.Leave();
                });
                _st.Cleanups.Add(t);
            }

            public void OnCleanup(Action fn) => _st.Cleanups.Add(fn);

            public IRegistry<T> Registry<T>(string name) => Registries.Get<T>(name, Id, _st.Cleanups);
            public IScreen Screen => ScreenApi.Instance();
            public void Toast(string text, double sec = 3) => Toasts.Show(this, text, sec);
            public byte[] ReadAsset(string path) => Assets.Read(_st.Def.Source, path);
            public string Lang => ModKit.Lang.Code;
            public string T(string key, string fallback = null) => ModKit.Lang.T(_st.Def.Source, key, fallback);
            private GameTimer _timer; private IStore _store;
            private GameTimer TimerImpl => _timer ?? (_timer = new GameTimer(Every, Guard, _st.Cleanups));
            public IGameTimer Timer => TimerImpl;
            public IStore Store => _store ?? (_store = new StoreImpl(Id));
            public ITags Tags => new TagsImpl();
            public void Guard(string what, Action fn) { Crash.Enter(Id, what); try { fn(); } catch (Exception e) { Crash.Error(Id, what, e); } Crash.Leave(); }
            public void Error(string msg) { _st.Warnings.Add("ОШИБКА: " + msg); Crash.Error(Id, "ctx.Error", new Exception(msg)); }
            public void Mark(string what) { Crash.Enter(Id, what); }
        }

        // ---------------------------------------------------------------- главное меню

        private static object _lastMenu;

        private static void CallMainMenu(ModState st)
        {
            if (_lastMenu == null || st.Ctx == null || st.Def.OnMainMenu == null) return;
            try { st.Def.OnMainMenu(st.Ctx, _lastMenu); }
            catch (Exception e) { Log.Write($"ошибка OnMainMenu {st.Def.Id}: {e}"); }
        }

        /// <summary>Вызывается из окна «Моды» после построения главного меню: моды могут добавить свои элементы.</summary>
        public static void NotifyMainMenu(object menu)
        {
            _lastMenu = menu;
            foreach (var st in Mods.ToList()) if (st.Active) CallMainMenu(st);
            EventBus.Emit("mainMenu", new EventData { ["menu"] = menu });
        }

        // ---------------------------------------------------------------- включение и выключение

        private static bool RunEnable(ModState st, bool deferRevert = false)
        {
            if (st.Active) return true;
            var id = st.Def.Id;
            st.Fault = null;
            if (st.Problem != null) { Log.Write($"[{id}] не запущен: {st.Problem}"); return true; }
            // зависимости включаются автоматически и запускаются раньше
            foreach (var depId in st.Def.Depends.Keys)
            {
                var d = Mods.First(m => m.Def.Id == depId);
                if (!d.Enabled) { d.Enabled = true; Log.Write($"[{id}] включает зависимость {depId}"); }
                RunEnable(d, deferRevert);
                if (!d.Active)
                {
                    st.Fault = $"зависимость «{d.Def.Name}» не запустилась" + (d.Fault != null ? ": " + d.Fault : "");
                    Log.Write($"[{id}] не запущен: {st.Fault}");
                    return true;
                }
            }
            st.Active = true;
            st.Warnings = new List<string>();
            try
            {
                st.Ctx = new Context(st);
                st.Def.OnEnable?.Invoke(st.Ctx);
                CallMainMenu(st);
                Log.Write($"мод включён: {id}");
                EventBus.Emit("modEnabled", new EventData { ["id"] = id });
                return true;
            }
            catch (Exception e)
            {
                bool conflict = e is ModConflictException;
                Log.Write($"{(conflict ? "конфликт модов" : "ошибка включения")} {id}: {e}");
                RunDisable(st);
                if (conflict)
                {
                    // конфликт — не сбой игры: мод остаётся включённым в настройках, но ждёт, пока освободится метод
                    st.Fault = e.Message;
                    return true;
                }
                st.Enabled = false;
                if (!deferRevert) RevertToPrevious($"{id}: {e.Message}");
                return false;
            }
        }

        private static void RunDisable(ModState st)
        {
            if (!st.Active) return;
            try { st.Def.OnDisable?.Invoke(new Context(st)); } catch (Exception e) { Log.Write($"ошибка OnDisable {st.Def.Id}: {e}"); }
            var cl = st.Cleanups.ToList();
            st.Cleanups.Clear();
            cl.Reverse();
            foreach (var c in cl)
            {
                try { c(); } catch (Exception e) { Log.Write($"ошибка отката {st.Def.Id}: {e}"); }
            }
            st.Active = false;
            st.Api = null;
            st.Warnings = new List<string>();
            Log.Write($"мод выключен: {st.Def.Id}");
            EventBus.Emit("modDisabled", new EventData { ["id"] = st.Def.Id });
        }

        private static ModState MakeState(ModDef def)
        {
            var settings = new Dictionary<string, double>();
            _saved.settings.TryGetValue(def.Id, out var savedSettings);
            foreach (var o in def.Options)
                settings[o.Key] = savedSettings != null && savedSettings.TryGetValue(o.Key, out var v) ? v : o.Default;
            bool enabled = _saved.enabled.TryGetValue(def.Id, out var en) ? en : def.DefaultEnabled;
            return new ModState { Def = def, Enabled = enabled, Settings = settings };
        }

        public static void Register(IEnumerable<ModDef> defs)
        {
            Load();
            foreach (var def in defs) Mods.Add(MakeState(def));
            ResolveAll();
        }

        /// <summary>Добавляет мод, загруженный из файла. false — мод с таким id уже есть.</summary>
        public static bool AddExternal(ModDef def)
        {
            if (Mods.Any(m => m.Def.Id == def.Id)) return false;
            var st = MakeState(def);
            Mods.Add(st);
            ResolveAll();
            if (_ready && st.Enabled) RunEnable(st);
            RetryBlocked();
            return true;
        }

        /// <summary>Включает моды, помеченные включёнными. Вызывать на главном потоке после загрузки всех модов.</summary>
        public static void Start(CrashReport report = null)
        {
            _ready = true;
            if (report != null) ApplyCrashReport(report);
            bool crashed = _saved.booting;
            if (crashed && _saved.session != null)
            {
                // прошлый запуск не дожил до подтверждения стабильности — возвращаем набор, который работал
                Log.Write("ОШИБКА: прошлый запуск завершился аварийно. Моды сброшены к набору последнего стабильного запуска");
                foreach (var st in Mods) st.Enabled = _saved.session.TryGetValue(st.Def.Id, out var v) && v;
            }
            _startupDiff = crashed ? null : ComputeDiff();
            string failed = "";
            foreach (var st in _startOrder.ToList()) if (st.Enabled && !RunEnable(st, true) && failed == "") failed = st.Def.Id;
            _saved.booting = true;
            if (failed != "") RevertToPrevious($"не включился мод {failed}");
            else if (_startupDiff == null && !crashed) { _saved.session = CurrentEnabled(); Save(); }
            else Save();
            // через минуту без падения запуск считается стабильным
            _stableAt = Scheduler.Now + StableAfterSec;
        }

        /// <summary>Отчёт о прошлом аварийном запуске в журнал; мод, на коде которого случился сбой, отключается.</summary>
        private static void ApplyCrashReport(CrashReport r)
        {
            Log.Write("ОШИБКА: прошлый запуск игры завершился аварийно (или игру закрыли принудительно)");
            if (r.Owner == null) { Log.Write("  меток мода нет — сбой до работы модов"); return; }
            Log.Write($"  последним работал мод «{r.Owner}»: {r.Tag}, {(r.Inside ? "сбой внутри кода мода" : $"код мода закончился за {r.AgeSec:0.0} с до остановки журнала")}");
            if (r.Recent.Count > 0) Log.Write("  недавние метки (новые сверху): " + string.Join(" | ", r.Recent.Take(6)));
            var st = Mods.FirstOrDefault(m => m.Def.Id == r.Owner);
            if (st != null && st.Enabled && r.Inside)
            {
                st.Enabled = false;
                Log.Write($"  мод {r.Owner} отключён: сбой пришёлся на его код. Включите вручную, когда причина устранена");
            }
        }

        private static readonly List<(string Id, string Why)> ErrorQueue = new List<(string, string)>();

        /// <summary>Отключает мод из-за серии ошибок. Выполняется на следующем кадре (нельзя снимать хуки изнутри самого хука).</summary>
        internal static void DisableForErrors(string modId, string why) => ErrorQueue.Add((modId, why));

        /// <summary>Вызывается каждый кадр: подтверждает стабильность запуска.</summary>
        internal static void Tick()
        {
            while (ErrorQueue.Count > 0)
            {
                var (id, why) = ErrorQueue[0];
                ErrorQueue.RemoveAt(0);
                var st = Mods.FirstOrDefault(m => m.Def.Id == id);
                if (st == null || !st.Active) continue;
                Log.Write($"ОШИБКА: мод {id} отключён: {why}");
                SetEnabled(st, false);
                st.Fault = "отключён из-за ошибок: " + why;
            }
            if (_stableAt < 0 || Scheduler.Now < _stableAt) return;
            _stableAt = -1;
            if (_toggled) { _toggled = false; MarkSession(); }
            else if (_startupDiff != null) { _saved.booting = false; Save(); }
            else MarkSession();
        }

        /// <summary>Библиотеки, от которых никто из включённых модов больше не зависит, выключаются.</summary>
        private static void DisableUnusedLibraries()
        {
            bool again = true;
            while (again)
            {
                again = false;
                foreach (var lib in Mods.Where(m => m.Def.Library && m.Enabled).ToList())
                {
                    if (DependentsOf(lib).Count > 0) continue;
                    lib.Enabled = false;
                    RunDisable(lib);
                    Log.Write($"библиотека выключена (зависимых нет): {lib.Def.Id}");
                    again = true;
                }
            }
        }

        public static void SetEnabled(ModState st, bool value)
        {
            if (st.Enabled == value) return;
            st.Enabled = value;
            if (!_ready) { Save(); return; }
            if (value) RunEnable(st);
            else
            {
                // выключение зависимости выключает зависимые моды
                void Off(ModState m)
                {
                    foreach (var d in DependentsOf(m))
                    {
                        Log.Write($"[{d.Def.Id}] выключен: выключена зависимость {m.Def.Id}");
                        d.Enabled = false; Off(d); RunDisable(d);
                    }
                }
                Off(st);
                RunDisable(st);
                DisableUnusedLibraries();
                RetryBlocked();
            }
            _toggled = true; _stableAt = Scheduler.Now + ToggleStableSec;     // ручное переключение: через 20 с набор считается стабильным
            Save();
        }

        /// <summary>
        /// Обновляет установленный мод новой версией (тот же id): выключает его и зависимые, подменяет определение, запускает обратно.
        /// Включённость и настройки сохраняются. При ошибке возвращается прежняя версия и бросается исключение.
        /// </summary>
        public static void ReplaceMod(ModState st, ModDef def)
        {
            var oldDef = st.Def; var oldSettings = st.Settings;
            bool wasEnabled = st.Enabled;
            string newVer = def.Version ?? def.Meta?.Version;
            foreach (var m in Mods)
            {
                if (m != st && m.Def.Depends.TryGetValue(st.Def.Id, out var r) && !Satisfies(newVer, r))
                    throw new Exception($"мод «{m.Def.Name}» требует {st.Def.Id} {r}, а в обновлении {newVer ?? "версия не указана"}");
            }
            var dependents = DependentSet(st);
            var chain = _startOrder.Where(m => m != st && m.Active && dependents.Contains(m)).ToList();
            for (int i = chain.Count - 1; i >= 0; i--) RunDisable(chain[i]);
            RunDisable(st);

            void LoadDef(ModDef d, Dictionary<string, double> saved0)
            {
                st.Def = d;
                st.Settings = new Dictionary<string, double>();
                foreach (var o in d.Options)
                {
                    var v = saved0.TryGetValue(o.Key, out var sv) ? sv : o.Default;
                    st.Settings[o.Key] = Math.Min(o.Max, Math.Max(o.Min, v));
                }
                st.Problem = st.Fault = null;
                ResolveAll();
            }
            bool Restart()
            {
                if (wasEnabled) RunEnable(st, true);
                bool ok = !wasEnabled || st.Active;
                foreach (var m in chain) if (m.Enabled) RunEnable(m, true);
                return ok;
            }

            LoadDef(def, oldSettings);
            if (Restart()) { Save(); Log.Write($"мод обновлён: {def.Id} {ModVersion(st)}"); return; }
            var why = st.Problem ?? st.Fault ?? "ошибка при запуске";
            Log.Write($"обновление {def.Id} не удалось ({why}): возвращаю прежнюю версию");
            RunDisable(st);
            st.Enabled = wasEnabled;
            LoadDef(oldDef, oldSettings);
            Restart();
            Save();
            throw new Exception($"новая версия не запустилась: {why}. Осталась прежняя");
        }

        /// <summary>Удаляет установленный zip-мод: выключает (вместе с зависимыми), стирает файл, убирает из списка.</summary>
        public static void RemoveMod(ModState st)
        {
            if (st.Def.Meta == null) throw new Exception("встроенный мод удалить нельзя");
            SetEnabled(st, false);
            try { if (st.Def.Source != null) File.Delete(st.Def.Source); }
            catch (Exception e) { Log.Write($"[{st.Def.Id}] файл {st.Def.Source} удалить не удалось: {e.Message}"); }
            Mods.Remove(st);
            _saved.settings.Remove(st.Def.Id);
            _saved.enabled.Remove(st.Def.Id);
            ResolveAll();
            Save();
            Log.Write($"мод удалён: {st.Def.Id}");
        }

        /// <summary>Меняет число в настройке; мод перезапускается, чтобы подхватить значение.</summary>
        public static void SetSetting(ModState st, string key, double value)
        {
            st.Settings[key] = value;
            if (_ready && st.Active)
            {
                RunDisable(st);
                RunEnable(st);
            }
            Save();
        }
    }

    /// <summary>Таймеры на главном потоке (вызывается из MelonMod.OnUpdate).</summary>
    public static class Scheduler
    {
        private class Timer { public double Period; public double Next; public Action Fn; public bool Dead; }
        private static readonly List<Timer> Timers = new List<Timer>();
        private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();
        public static double Now => Clock.Elapsed.TotalSeconds;

        /// <summary>Периодический вызов; возвращает действие для отмены.</summary>
        public static Action Every(double ms, Action fn)
        {
            var t = new Timer { Period = Math.Max(1, ms) / 1000.0, Fn = fn };
            t.Next = Now + t.Period;
            Timers.Add(t);
            return () => t.Dead = true;
        }

        internal static void Tick()
        {
            double now = Now;
            Timers.RemoveAll(t => t.Dead);
            foreach (var t in Timers.ToArray())
            {
                if (t.Dead || now < t.Next) continue;
                t.Next = now + t.Period;
                try { t.Fn(); } catch (Exception e) { Log.Write("ОШИБКА таймера: " + e); }
            }
            Core.Tick();
        }
    }
}
