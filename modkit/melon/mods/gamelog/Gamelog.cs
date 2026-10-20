using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Il2Cpp;
using Il2CppInterop.Runtime;
using ModKit;

namespace GameLog
{
    /// <summary>
    /// Журнал игры (отладка): пишет в gamelog.txt изменения значений (Z_Database, герой, менеджеры боя и способностей),
    /// вызовы методов, окно характеристик героя, сундуки и торговца. Значения читаются из памяти по смещениям полей IL2CPP.
    /// </summary>
    public unsafe class GameLogMod : IKitMod
    {
        private const string OutName = "gamelog.txt";
        private const string DownloadDir = "/sdcard/Download/ModKit";
        private static readonly string[] InstanceClasses = { "C_Battle_Manager", "V_Ability_Manager" };
        private static readonly string[] TraceClasses = { "C_Battle_Manager", "C_Battle_AllyUnit", "V_Ability_Manager", "Z_Database" };
        private const string ArtifactClass = "V_Artifact_Manager";
        private static readonly Regex SkipField = new Regex("^(LSoundList_|Array_Float_AutoSave|Array_Int_AutoSave)");
        private static readonly Regex SkipMethod = new Regex(@"^(Update|FixedUpdate|LateUpdate|OnGUI|get_|set_|\.ctor|\.cctor)");
        private const int NoisyChanges = 20;
        private const double WindowSec = 10;
        private const int HotCallsPerFlush = 400;
        private const double CallsFlushMs = 2000;
        private const int TickCooldown = 4;
        private const int MaxElems = 400;
        private const int ArrayLength = 0x18, ArrayData = 0x20;

        private static readonly (string key, string label)[] Groups =
        {
            ("g_vars", "Z_Database: характеристики исследований и эффектов (AllyUnitVar_*)"),
            ("g_lists", "Z_Database: списки и массивы (артефакты, способности, пулы выпадения)"),
            ("g_other", "Z_Database: прочее (Game_*, AllyUnitGlobal_* и др.)"),
            ("g_heroStat", "Герой: характеристики (UnitApply_*, BUnitApply_*)"),
            ("g_heroOther", "Герой: остальные поля"),
            ("g_mgrBattle", "Менеджер боя (C_Battle_Manager)"),
            ("g_mgrAbility", "Менеджер способностей (V_Ability_Manager)"),
            ("g_calls", "Вызовы методов (CALLS)"),
            ("g_art", "Сундуки, выбор артефактов, торговец (ART, OFFER)"),
            ("g_status", "Окно характеристик героя: названия и значения строк (STATUS)"),
        };

        /// <summary>Шумные источники: по умолчанию не пишутся (настройка != 0 — «не писать»). value — «источник.поле», call — «Класс.метод».</summary>
        private static readonly (string key, string label, string value, string call)[] Excludes =
        {
            ("x_kill", "Счётчик убийств (Game_KillMonsterNum)", "Z_Database.Game_KillMonsterNum", null),
            ("x_manastock", "Запас маны (AllyUnitGlobal_ManaStock)", "Z_Database.AllyUnitGlobal_ManaStock", null),
            ("x_timesec", "Секунды боя (Time_Sec)", "C_Battle_Manager.Time_Sec", null),
            ("x_timescale", "Масштаб времени (ApplyTimeScale)", "C_Battle_Manager.ApplyTimeScale", null),
            ("x_enemylvl", "Уровень врагов (EnemyLevelNum)", "C_Battle_Manager.EnemyLevelNum", null),
            ("x_shield", "Задержка щита героя (ShieldDelay)", "hero.ShieldDelay", null),
            ("x_bullet", "Вызовы: создание снарядов (Bullet_Create)", null, "C_Battle_AllyUnit.Bullet_Create"),
            ("x_collision", "Вызовы: столкновения (OnCollisionStay)", null, "C_Battle_AllyUnit.OnCollisionStay"),
            ("x_managain", "Вызовы: получение маны (ManaGain)", null, "V_Ability_Manager.ManaGain"),
            ("x_statebar", "Вызовы: полоска состояния (Display_StateBar)", null, "C_Battle_AllyUnit.Display_StateBar"),
        };

        public ModDef Define()
        {
            var def = new ModDef
            {
                Id = "gamelog",
                Name = "Журнал игры (отладка)",
                Description = "Пишет в gamelog.txt все изменения значений и вызовы методов.",
                Version = "2.0",
                OnEnable = ctx => new Session(ctx).Start(),
                OpenSettings = ctx => UiKit.OpenChecklist(new UiKit.ChecklistOptions
                {
                    Name = "ModKitGamelogSettings",
                    Title = "Журнал игры",
                    Subtitle = "Отметьте, что и куда писать в gamelog.txt. Меняется сразу, без перезапуска.",
                    Sections =
                    {
                        new UiKit.ChecklistSection { Header = "Куда писать", Items = { new UiKit.ChecklistItem { Key = "toDownload", Label = "В Download/ModKit (иначе UserData/ModKit)" } } },
                        new UiKit.ChecklistSection { Header = "Писать", Items = Groups.Select(g => new UiKit.ChecklistItem { Key = g.key, Label = g.label }).ToList() },
                        new UiKit.ChecklistSection { Header = "Опасно (применится после перезапуска мода)", Items = { new UiKit.ChecklistItem { Key = "trace", Label = "Трассировка вызовов (CALLS): ~300 хуков, может ронять игру" } } },
                        new UiKit.ChecklistSection { Header = "Не писать (шумные источники)", Items = Excludes.Select(e => new UiKit.ChecklistItem { Key = e.key, Label = e.label }).ToList() },
                    },
                    Get = key => key == "toDownload" || key == "trace" ? ctx.Setting(key) == 1 : ctx.Setting(key) != 0,
                    Set = (key, v) => ctx.SetSetting(key, v ? 1 : 0),
                }),
            };
            def.Options.Add(new ModOption { Key = "interval", Label = "Интервал снимка, с", Default = 0.7, Min = 0.5, Max = 5, Step = 0.5 });
            def.Options.Add(new ModOption { Key = "trace", Label = "Трассировка вызовов (хуки Harmony, может ронять игру)", Default = 0, Min = 0, Max = 1, Step = 1, Hidden = true });
            def.Options.Add(new ModOption { Key = "dbg_art_all", Label = "Отладка: хуки на ВСЕ методы V_Artifact_Manager (как в первой версии; роняет игру)", Default = 0, Min = 0, Max = 1, Step = 1, Hidden = true });
            def.Options.Add(new ModOption { Key = "dbg_grab", Label = "Отладка: хуки захвата объектов", Default = 1, Min = 0, Max = 1, Step = 1, Hidden = true });
            def.Options.Add(new ModOption { Key = "dbg_main", Label = "Отладка: таймер снимков", Default = 1, Min = 0, Max = 1, Step = 1, Hidden = true });
            def.Options.Add(new ModOption { Key = "dbg_status", Label = "Отладка: хук окна характеристик", Default = 1, Min = 0, Max = 1, Step = 1, Hidden = true });
            def.Options.Add(new ModOption { Key = "toDownload", Label = "Папка журнала", Default = 0, Min = 0, Max = 1, Step = 1, Hidden = true });
            foreach (var g in Groups) def.Options.Add(new ModOption { Key = g.key, Label = g.label, Default = 1, Min = 0, Max = 1, Step = 1, Hidden = true });
            foreach (var e in Excludes) def.Options.Add(new ModOption { Key = e.key, Label = e.label, Default = 1, Min = 0, Max = 1, Step = 1, Hidden = true });
            return def;
        }

        // ---------------------------------------------------------------- чтение значений

        private static string Round(double v) => (Math.Round(v * 10000) / 10000).ToString("R", CultureInfo.InvariantCulture);

        /// <summary>Размер элемента и разбор значения из блока памяти. null — тип не поддерживается.</summary>
        private static (int size, Func<byte[], int, string> read)? Elem(string typeName, bool scalar)
        {
            switch (typeName)
            {
                case "System.Int32": return (4, (b, o) => BitConverter.ToInt32(b, o).ToString(CultureInfo.InvariantCulture));
                case "System.UInt32": return (4, (b, o) => BitConverter.ToUInt32(b, o).ToString(CultureInfo.InvariantCulture));
                case "System.Single": return (4, (b, o) => Round(BitConverter.ToSingle(b, o)));
                case "System.Double": return (8, (b, o) => Round(BitConverter.ToDouble(b, o)));
                case "System.Boolean": return (1, scalar ? (b, o) => b[o] != 0 ? "true" : "false" : (Func<byte[], int, string>)((b, o) => b[o].ToString()));
                case "System.Byte": return (1, (b, o) => b[o].ToString());
                case "System.Int16": return (2, (b, o) => BitConverter.ToInt16(b, o).ToString(CultureInfo.InvariantCulture));
                case "System.Int64": return (8, (b, o) => BitConverter.ToInt64(b, o).ToString(CultureInfo.InvariantCulture));
                default: return null;
            }
        }

        private enum Kind { Scalar, Slow, Array, List }

        private class Spec
        {
            public string Key, Group, Name, TypeName;
            public IntPtr Field;
            public int Off, Size;
            public Kind Kind;
            public Func<byte[], int, string> Read;
            public int ElemSize;
            public Func<byte[], int, string> ElemRead;
            public byte[] Last;               // прежние байты скаляра или содержимого массива
            public bool HasLast;
            public PropertyInfo Prop;
        }

        private class Source
        {
            public string Label;
            public bool IsStatic;
            public IntPtr Klass;
            public Type Wrapper;
            public Func<IntPtr> Base;
            public readonly List<Spec> Specs = new List<Spec>();
            public List<Spec> Active = new List<Spec>();
            public bool Built;
            public string Sig = "";
            public IntPtr PrevBase;
            public bool Fresh = true;         // нет прежних значений: сверять всё
        }

        private class HookInfo { public string Name; public int N, LastN, Win; public bool Skip; public Action Detach; }

        private class Session
        {
            private readonly ModContext _ctx;
            private StreamWriter _file;
            private string _outPath = "";
            private readonly object _gate = new object();
            private readonly double _t0 = Scheduler.Now;
            private readonly Dictionary<string, (IntPtr ptr, double at)> _seen = new Dictionary<string, (IntPtr, double)>();
            private readonly List<Source> _sources = new List<Source>();
            private readonly Dictionary<string, string> _prevVals = new Dictionary<string, string>();
            private readonly Dictionary<string, int> _winChanges = new Dictionary<string, int>();
            private readonly HashSet<string> _muted = new HashSet<string>();
            private double _windowStart = Scheduler.Now;
            private string _sigAll = "";
            private HashSet<string> _exValues;
            private List<string> _exCalls;
            private bool _callsOn, _artOn, _statusOn;
            private int _listItemsOff = -1, _listSizeOff = -1;
            private double _nextTick;
            private readonly List<HookInfo> _hooks = new List<HookInfo>();
            private string _callsSig = "";

            public Session(ModContext ctx) { _ctx = ctx; }

            private bool On(string key) => _ctx.Setting(key) != 0;
            private double TickMs => Math.Max(300, Math.Round(_ctx.Setting("interval") * 1000));

            // ---------------------------------------------------------------- файл

            private void Reopen()
            {
                var path = (_ctx.Setting("toDownload") == 1 ? DownloadDir : Paths.Dir) + "/" + OutName;
                if (path == _outPath && _file != null) return;
                try { _file?.Dispose(); } catch { /* */ }
                _file = null;
                _outPath = path;
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    _file = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(false));
                }
                catch (Exception e) { _ctx.Log($"не удалось открыть {path}: {e.Message}"); }
            }

            private void W(string s)
            {
                lock (_gate)
                {
                    Reopen();
                    try { _file?.Write($"[{(Scheduler.Now - _t0):0.0}] {s}\n"); } catch { /* нет файла */ }
                }
            }

            private void Flush() { lock (_gate) { try { _file?.Flush(); } catch { /* */ } } }

            // ---------------------------------------------------------------- запуск

            public void Start()
            {
                Reopen();
                _ctx.OnCleanup(() => { lock (_gate) { try { _file?.Dispose(); } catch { /* */ } _file = null; } });
                W("===== сессия начата =====");
                RefreshSettings();
                W($"настройки: выключено групп {Groups.Count(g => !On(g.key))}, исключений {_exValues.Count + _exCalls.Count}, интервал {TickMs} мс");

                // захват живых объектов
                if (On("dbg_grab"))
                {
                    Grab("C_Battle_Manager", "Update", 0);
                    Grab("V_Ability_Manager", "AbilityStatApply", 1);
                    Grab("V_Ability_Manager", "Update", 0);
                }

                // источники снимков
                var zdb = IL2CPP.GetIl2CppClass("Assembly-CSharp.dll", "", "Z_Database");
                _sources.Add(new Source { Label = "Z_Database", IsStatic = true, Klass = zdb, Wrapper = typeof(Z_Database), Base = () => IntPtr.Zero });
                foreach (var c in InstanceClasses)
                {
                    var name = c;
                    _sources.Add(new Source { Label = name, Klass = IL2CPP.GetIl2CppClass("Assembly-CSharp.dll", "", name), Wrapper = Hooks.FindType(name), Base = () => Fresh(name) });
                }
                _sources.Add(new Source { Label = "hero", Klass = IL2CPP.GetIl2CppClass("Assembly-CSharp.dll", "", "C_Battle_AllyUnit"), Wrapper = typeof(C_Battle_AllyUnit), Base = FindHero });

                // снимок читает память игры: выполняем на главном потоке, не чаще раза в интервал и не больше доли времени
                if (On("dbg_main")) _ctx.Every(100, OnMain);

                if (_ctx.Setting("trace") != 0) StartTrace(); else W("трассировка отключена настройкой trace=0");
                if (On("dbg_status")) StartStatus();
                if (On("g_art")) StartArtifacts();      // хуки на все методы V_Artifact_Manager ставим только по запросу
                _ctx.Every(CallsFlushMs, FlushCalls);
            }

            private void RefreshSettings()
            {
                _exValues = new HashSet<string>(Excludes.Where(e => e.value != null && _ctx.Setting(e.key) != 0).Select(e => e.value));
                _exCalls = Excludes.Where(e => e.call != null && _ctx.Setting(e.key) != 0).Select(e => e.call).ToList();
                _callsOn = On("g_calls");
                _artOn = On("g_art");
                _statusOn = On("g_status");
            }

            private void Grab(string cls, string method, int n)
            {
                try
                {
                    _ctx.Before(cls, method, n, (self, args) =>
                    {
                        if (self is Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase o) _seen[cls] = (o.Pointer, Scheduler.Now);
                    });
                }
                catch { /* метода нет в этой версии игры */ }
            }

            private IntPtr Fresh(string cls) => _seen.TryGetValue(cls, out var s) && Scheduler.Now - s.at < 2 ? s.ptr : IntPtr.Zero;

            // ---------------------------------------------------------------- герой

            private C_Battle_AllyUnit _hero;

            private IntPtr FindHero()
            {
                var mp = Fresh("C_Battle_Manager");
                if (mp == IntPtr.Zero) return IntPtr.Zero;
                if (_hero != null) return _hero.Pointer;           // Unity-сравнение: уничтоженный объект == null
                var mgr = new C_Battle_Manager(mp);
                var field = mgr.Obj_FieldAllyUnit;
                if (field == null) return IntPtr.Zero;
                var tf = field.transform;
                for (int i = 0; i < tf.childCount; i++)
                {
                    var c = tf.GetChild(i).gameObject.GetComponent<C_Battle_AllyUnit>();
                    if (c != null) { _hero = c; return c.Pointer; }
                }
                return IntPtr.Zero;
            }

            // ---------------------------------------------------------------- снимки

            private static string GroupOf(string label, string name, string typeName)
            {
                if (label == "Z_Database")
                {
                    if (name.StartsWith("AllyUnitVar_")) return "g_vars";
                    return typeName.EndsWith("[]") || typeName.StartsWith("System.Collections.Generic.List<") ? "g_lists" : "g_other";
                }
                if (label == "hero") return name.StartsWith("UnitApply_") || name.StartsWith("BUnitApply_") ? "g_heroStat" : "g_heroOther";
                return label == "C_Battle_Manager" ? "g_mgrBattle" : "g_mgrAbility";
            }

            private static string Str(IntPtr p) => p == IntPtr.Zero ? "" : Marshal.PtrToStringAnsi(p);

            private void Build(Source src)
            {
                var t0 = Scheduler.Now;
                IntPtr iter = IntPtr.Zero, f;
                while ((f = IL2CPP.il2cpp_class_get_fields(src.Klass, ref iter)) != IntPtr.Zero)
                {
                    int flags = IL2CPP.il2cpp_field_get_flags(f);
                    bool isStatic = (flags & 0x10) != 0, isLiteral = (flags & 0x40) != 0;
                    var name = Str(IL2CPP.il2cpp_field_get_name(f));
                    if (isStatic != src.IsStatic || isLiteral || SkipField.IsMatch(name)) continue;
                    try
                    {
                        var ft = IL2CPP.il2cpp_field_get_type(f);
                        var tn = Str(IL2CPP.il2cpp_type_get_name(ft));
                        var kl = IL2CPP.il2cpp_class_from_type(ft);
                        var spec = new Spec
                        {
                            Key = $"{src.Label}.{name}", Name = name, TypeName = tn, Field = f, Off = (int)IL2CPP.il2cpp_field_get_offset(f),
                            Group = GroupOf(src.Label, name, tn),
                        };
                        var scalar = Elem(tn, true);
                        if (scalar != null) { spec.Kind = Kind.Scalar; spec.Size = scalar.Value.size; spec.Read = scalar.Value.read; }
                        else if (Str(IL2CPP.il2cpp_class_get_namespace(kl)) == "CodeStage.AntiCheat.ObscuredTypes" || IL2CPP.il2cpp_class_is_enum(kl))
                        {
                            uint align = 0;
                            spec.Kind = Kind.Slow;
                            spec.Size = (int)IL2CPP.il2cpp_class_value_size(kl, ref align);
                            spec.Prop = src.Wrapper.GetProperty(name, BindingFlags.Public | (src.IsStatic ? BindingFlags.Static : BindingFlags.Instance));
                        }
                        else if (tn.EndsWith("[]"))
                        {
                            var el = Elem(tn.Substring(0, tn.Length - 2), false);
                            if (el == null) continue;
                            spec.Kind = Kind.Array; spec.Size = IntPtr.Size; spec.ElemSize = el.Value.size; spec.ElemRead = el.Value.read;
                        }
                        else if (tn.StartsWith("System.Collections.Generic.List<"))
                        {
                            var m = Regex.Match(tn, @"^System\.Collections\.Generic\.List<(System\.\w+)>$");
                            var el = m.Success ? Elem(m.Groups[1].Value, false) : null;
                            if (el == null) continue;
                            spec.Kind = Kind.List; spec.Size = IntPtr.Size; spec.ElemSize = el.Value.size; spec.ElemRead = el.Value.read;
                            if (_listItemsOff < 0) FindListOffsets(kl);
                        }
                        else continue;
                        if (spec.Off < 0 || spec.Size <= 0) continue;
                        spec.Last = new byte[spec.Size];
                        src.Specs.Add(spec);
                    }
                    catch { /* тип без поддержки */ }
                }
                src.Built = true;
                W($"подготовка {src.Label}: полей {src.Specs.Count}, {(Scheduler.Now - t0) * 1000:0} мс");
            }

            private void FindListOffsets(IntPtr listClass)
            {
                IntPtr iter = IntPtr.Zero, f;
                while ((f = IL2CPP.il2cpp_class_get_fields(listClass, ref iter)) != IntPtr.Zero)
                {
                    var n = Str(IL2CPP.il2cpp_field_get_name(f));
                    if (n == "_items") _listItemsOff = (int)IL2CPP.il2cpp_field_get_offset(f);
                    else if (n == "_size") _listSizeOff = (int)IL2CPP.il2cpp_field_get_offset(f);
                }
            }

            /// <summary>Читает байты скаляра/указателя поля в s.Last-совместимый буфер.</summary>
            private static void ReadField(Source src, Spec s, IntPtr b, byte[] buf)
            {
                if (src.IsStatic) { fixed (byte* p = buf) IL2CPP.il2cpp_field_static_get_value(s.Field, p); }
                else Marshal.Copy(b + s.Off, buf, 0, s.Size);
            }

            private string ReadSlow(Source src, Spec s, IntPtr b)
            {
                if (s.Prop == null) return null;
                object target = null;
                if (!src.IsStatic) target = Activator.CreateInstance(src.Wrapper, b);
                var v = s.Prop.GetValue(target);
                if (v == null) return null;
                var dec = v.GetType().GetMethod("GetDecrypted", Type.EmptyTypes);
                if (dec != null) v = dec.Invoke(v, null);
                if (v is float f) return Round(f);
                if (v is double d) return Round(d);
                return Convert.ToString(v, CultureInfo.InvariantCulture);
            }

            /// <summary>Содержимое массива/списка: null — нет данных, "" — не изменилось.</summary>
            private string ReadRef(Spec s, IntPtr ptr, bool force)
            {
                if (ptr == IntPtr.Zero) { s.HasLast = false; return "null"; }
                int n; IntPtr items; string label;
                if (s.Kind == Kind.Array) { n = (int)(uint)Marshal.ReadInt32(ptr + ArrayLength); items = ptr; label = "len"; }
                else { n = Marshal.ReadInt32(ptr + _listSizeOff); items = Marshal.ReadIntPtr(ptr + _listItemsOff); label = "count"; }
                if (n > MaxElems) { s.HasLast = false; return $"{label}={n}"; }
                if (n == 0 || items == IntPtr.Zero) { s.HasLast = false; return "[]"; }
                var buf = new byte[n * s.ElemSize];
                Marshal.Copy(items + ArrayData, buf, 0, buf.Length);
                bool same = !force && s.HasLast && s.Last.AsSpan().SequenceEqual(buf);
                s.Last = buf; s.HasLast = true;
                if (same) return "";
                var sb = new StringBuilder("[");
                for (int i = 0; i < n; i++) { if (i > 0) sb.Append(','); sb.Append(s.ElemRead(buf, i * s.ElemSize)); }
                return sb.Append(']').ToString();
            }

            private void Emit(string key, string val, ref int baseline)
            {
                if (!_prevVals.TryGetValue(key, out var old))
                {
                    _prevVals[key] = val;
                    W($"INIT {key} = {val}");
                    baseline++;
                    return;
                }
                _prevVals[key] = val;
                if (old == val) return;
                int n = (_winChanges.TryGetValue(key, out var c) ? c : 0) + 1;
                _winChanges[key] = n;
                if (_muted.Contains(key)) return;
                if (n > NoisyChanges)
                {
                    _muted.Add(key);
                    W($"NOISY {key} (часто меняется, дальше не пишу), последнее: {val}");
                    return;
                }
                W($"SET {key}: {old} -> {val}");
            }

            private void Snapshot(Source src)
            {
                if (!src.Built) Build(src);
                if (src.Sig != _sigAll)
                {
                    src.Sig = _sigAll;
                    src.Active = src.Specs.Where(s => On(s.Group) && !_exValues.Contains(s.Key)).ToList();
                    src.Fresh = true;
                }
                if (src.Active.Count == 0) return;
                var b = src.IsStatic ? new IntPtr(1) : src.Base();
                if (b == IntPtr.Zero) { src.Fresh = true; src.PrevBase = IntPtr.Zero; return; }
                if (b != src.PrevBase) { src.Fresh = true; src.PrevBase = b; }       // другой объект: сверяем значения целиком
                bool force = src.Fresh;
                int baseline = 0;
                foreach (var s in src.Active)
                {
                    try
                    {
                        if (s.Kind == Kind.Array || s.Kind == Kind.List)
                        {
                            var pb = new byte[IntPtr.Size];
                            ReadField(src, s, b, pb);
                            var ptr = new IntPtr(BitConverter.ToInt64(pb, 0));
                            var val = ReadRef(s, ptr, force);
                            if (val != "") Emit(s.Key, val, ref baseline);
                            continue;
                        }
                        var cur = new byte[s.Size];
                        ReadField(src, s, b, cur);
                        if (!force && s.Last.AsSpan().SequenceEqual(cur)) continue;
                        s.Last = cur;
                        var v = s.Kind == Kind.Scalar ? s.Read(cur, 0) : ReadSlow(src, s, b);
                        if (v != null) Emit(s.Key, v, ref baseline);
                    }
                    catch { /* поле недоступно */ }
                }
                src.Fresh = false;
                if (baseline > 0) W($"-- {src.Label}: начальных значений {baseline}");
            }

            private void Tick()
            {
                RefreshSettings();
                RefreshCalls();
                _sigAll = string.Concat(Groups.Select(g => On(g.key) ? "1" : "0")) + "|" + string.Join(",", _exValues);
                if (Scheduler.Now - _windowStart > WindowSec) { _windowStart = Scheduler.Now; _winChanges.Clear(); }
                foreach (var src in _sources)
                {
                    try { Snapshot(src); } catch (Exception e) { src.Fresh = true; W($"ошибка снимка {src.Label}: {e.Message}"); }
                }
                Flush();
            }

            private void OnMain()
            {
                var now = Scheduler.Now;
                if (now * 1000 < _nextTick) return;
                try { Tick(); } catch (Exception e) { W($"ошибка снимка: {e.Message}"); }
                var dt = (Scheduler.Now - now) * 1000;
                _nextTick = now * 1000 + Math.Max(TickMs, dt * TickCooldown);
                if (dt > 100) W($"МЕДЛЕННО: снимок занял {dt:0} мс (главный поток; отключите тяжёлые группы в «Настройки»)");
            }

            // ---------------------------------------------------------------- трассировка вызовов

            private static string Sig(string cls, MethodInfo m) =>
                $"{cls}.{m.Name}({string.Join(",", m.GetParameters().Select(p => p.ParameterType.Name))})";

            private static IEnumerable<MethodInfo> Methods(Type t) =>
                t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                    .Where(m => !m.IsGenericMethodDefinition && !m.IsAbstract);

            private void RefreshCalls()
            {
                var sig = string.Join(",", _exCalls);
                if (sig == _callsSig) return;
                _callsSig = sig;
                foreach (var h in _hooks) h.Skip = _exCalls.Any(p => h.Name.StartsWith(p + "("));
            }

            private void StartTrace()
            {
                int failed = 0;
                foreach (var c in TraceClasses)
                {
                    Type t;
                    try { t = Hooks.FindType(c); } catch { continue; }
                    foreach (var m in Methods(t))
                    {
                        if (SkipMethod.IsMatch(m.Name) || m.Name.StartsWith("<")) continue;
                        var h = new HookInfo { Name = Sig(c, m) };
                        h.Skip = _exCalls.Any(p => h.Name.StartsWith(p + "("));
                        try
                        {
                            h.Detach = Hooks.HookBefore("gamelog", m, (self, args) =>
                            {
                                if (!_callsOn || h.Skip) return;
                                h.N++;
                                if (++h.Win == HotCallsPerFlush)
                                {
                                    h.Detach?.Invoke();
                                    W($"HOT {h.Name} (> {HotCallsPerFlush} вызовов за {CallsFlushMs / 1000:0}с, трассировку снял)");
                                }
                            });
                            var hh = h;
                            _ctx.OnCleanup(() => hh.Detach?.Invoke());
                            _hooks.Add(h);
                        }
                        catch { failed++; }
                    }
                }
                _callsSig = string.Join(",", _exCalls);
                W($"трассировка: методов под наблюдением {_hooks.Count}, не удалось {failed}");
            }

            private void FlushCalls()
            {
                var lines = new List<string>();
                foreach (var h in _hooks)
                {
                    int d = h.N - h.LastN;
                    if (d > 0) lines.Add($"{h.Name}×{d}");
                    h.LastN = h.N;
                    h.Win = 0;
                }
                if (lines.Count > 0) W("CALLS " + string.Join("  ", lines));
            }

            // ---------------------------------------------------------------- окно характеристик героя

            private static string Clean(string s) => Regex.Replace(s ?? "", @"\s+", " ").Trim();

            private bool _labelsLogged;
            private string _lastStatus = "";

            private void StartStatus()
            {
                try
                {
                    _ctx.After("C_Battle_AllyUnit", "Display_BattleStatus", 0, (self, args) =>
                    {
                        if (!_statusOn) return;
                        try
                        {
                            var hero = (C_Battle_AllyUnit)self;
                            var arr = hero.Array_BattleStatus_Value;
                            var values = new List<string>();
                            for (int i = 0; i < (int)arr.Length; i++) values.Add(arr[i] == null ? "" : Clean(arr[i].text));
                            var mp = Fresh("C_Battle_Manager");
                            if (!_labelsLogged && mp != IntPtr.Zero)
                            {
                                var names = new C_Battle_Manager(mp).Array_TextLanguage_BattleStatus;
                                var nl = new List<string>();
                                for (int i = 0; i < (int)names.Length; i++) nl.Add($"{i}={(names[i] == null ? "" : Clean(names[i].text))}");
                                W("STATUS названия строк: " + string.Join(" | ", nl));
                                _labelsLogged = true;
                            }
                            var line = string.Join(" | ", values.Select((v, i) => $"{i}={v}"));
                            if (line == _lastStatus) return;                // окно обновляется постоянно, а значения не меняются
                            _lastStatus = line;
                            W("STATUS значения: " + line);
                        }
                        catch (Exception e) { W("ошибка STATUS: " + e.Message); }
                    });
                }
                catch (Exception e) { W("STATUS: метод Display_BattleStatus не найден (" + e.Message + ")"); }
            }

            // ---------------------------------------------------------------- сундуки, выбор артефактов, торговец

            private static string Ints(Il2CppSystem.Collections.Generic.List<int> l)
            {
                if (l == null) return "null";
                var o = new List<int>();
                for (int i = 0; i < l.Count; i++) o.Add(l[i]);
                return "[" + string.Join(",", o) + "]";
            }

            private void DumpOffer(V_Artifact_Manager self, string why)
            {
                string costs;
                try
                {
                    var l = V_Artifact_Manager.List_MerchantSelectCost;
                    var o = new List<int>();
                    for (int i = 0; i < l.Count; i++) o.Add(l[i]);
                    costs = string.Join(",", o);
                }
                catch { costs = ""; }
                W($"OFFER {why}: ArtifactType={Z_Database.ArtifactType} select={Ints(self.List_ArtifactSelectID)} merchant={Ints(self.List_MerchantSelectID)} costs=[{costs}]");
            }

            private void StartArtifacts()
            {
                Type t;
                try { t = Hooks.FindType(ArtifactClass); } catch (Exception e) { W("ART: " + e.Message); return; }
                var dumpOn = new Regex("^(Create_Artifact|Create_Merchant|Button_Artifact_Change|Button_Artifact_Select)$");
                foreach (var m in Methods(t))
                {
                    if (m.Name.StartsWith("<") || SkipMethod.IsMatch(m.Name)) continue;
                    var ps = m.GetParameters();
                    // Harmony-хук на метод с объектными параметрами (GameObject, Transform, Image...) или корутину портит вызов из игры
                    // (аргумент приходит null → NullReference/порча кучи). Frida-версия только читала регистры, Harmony переупаковывает — поэтomu только void и числа
                    if (!On("dbg_art_all") && (m.ReturnType != typeof(void) || ps.Any(p => !p.ParameterType.IsPrimitive))) continue;
                    var sig = $"{ArtifactClass}.{m.Name}";
                    var name = m.Name;
                    try
                    {
                        _ctx.OnCleanup(Hooks.HookBefore("gamelog", m, (self, args) =>
                        {
                            if (!_artOn) return;
                            var a = string.Join(" ", ps.Select((p, i) => p.ParameterType == typeof(int) ? $"{p.Name}={args[i]}" : p.Name));
                            W($"ART {sig}({a})");
                        }));
                        if (dumpOn.IsMatch(name))
                            _ctx.OnCleanup(Hooks.HookAfter("gamelog", m, (self, args) =>
                            {
                                if (!_artOn) return;
                                try { DumpOffer((V_Artifact_Manager)self, name); } catch (Exception e) { W("ошибка OFFER: " + e.Message); }
                            }));
                    }
                    catch { /* метод без нативного адреса */ }
                }
            }
        }
    }
}
