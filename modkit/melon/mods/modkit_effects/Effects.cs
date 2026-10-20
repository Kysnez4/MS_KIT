using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Il2Cpp;

namespace ModKit
{
    /// <summary>Ссылка на числовое поле: чтение даёт базу (или уже накопленную цель), запись задаёт цель.</summary>
    public class FieldRef
    {
        internal Effects.Slot S;
        public double Value { get => S.Target; set => S.Target = S.IsInt ? Math.Round(value) : value; }
    }

    /// <summary>Герой в бою (C_Battle_AllyUnit) глазами обработчика эффекта.</summary>
    public interface IHeroView
    {
        /// <summary>Поле героя (instance или static) по имени, например UnitApply_MagicBulletDamage.</summary>
        FieldRef Field(string name);
        /// <summary>Статическая переменная Z_Database, например AllyUnitVar_ItemObtainAreaU.</summary>
        FieldRef Db(string name);
        /// <summary>Поле *= (1 + percent/100).</summary>
        void MulPercent(string name, double percent);
        void Add(string name, double n);
        /// <summary>Правка только на время показа окна характеристик (поле героя или Z_Database); потом значение возвращается.</summary>
        void Display(string name, Func<double, double> fn);
        /// <summary>Сам объект C_Battle_AllyUnit.</summary>
        C_Battle_AllyUnit Raw { get; }
    }

    /// <summary>Свой эффект артефакта/комбинации. ID эффекта пишется в колонку «효과ID_NN», значение — «효과ID_NN값».</summary>
    public class EffectDef
    {
        /// <summary>Целое >= 10000 (меньшие заняты игрой).</summary>
        public int Id;
        public string Name;
        /// <summary>Вызывается после каждого пересчёта для каждого источника героя с этим эффектом. value — число из «효과ID_NN값».</summary>
        public Action<IHeroView, double, ModContext> Apply;
        /// <summary>
        /// Раскрывает эффект в пары (ID игры, значение), которые игра обработает сама при пересчёте. Первая пара занимает слот эффекта,
        /// остальные — свободные слоты (ID 0/1) других строк-источников героя.
        /// </summary>
        public Func<double, (int Id, double Value)[]> Expand;
    }

    /// <summary>
    /// Движок своих эффектов (перенос effects.ts). После каждого пересчёта характеристик (V_Ability_Manager.AbilityStatApply) и раз в ~0,7 с
    /// смотрим полученные артефакты, класс, комбинации, синергии; для эффектов с Apply правим поля героя (UnitApply_*) и Z_Database.AllyUnitVar_*.
    /// Обработчики не пишут в поля напрямую: Field().Value отдаёт БАЗУ и копит цель, запись делает движок. Для эффектов с Expand на время
    /// AbilityStatApply в слоты строк-источников подставляются пары (ID игры, значение) — игра считает их своим кодом.
    /// </summary>
    public static class Effects
    {
        private class Registered { public EffectDef Def; public ModContext Ctx; }
        private static readonly Dictionary<int, Registered> Registry = new Dictionary<int, Registered>();
        /// <summary>Глобальные эффекты модов: владелец → пары. Одинаковые ID суммируются.</summary>
        private static readonly Dictionary<string, (int Id, double V)[]> Globals = new Dictionary<string, (int, double)[]>();

        /// <summary>Проходы модов по герою (OnHeroPass): вызываются после обработчиков эффектов при каждом проходе.</summary>
        private static readonly List<(string Owner, Action<IHeroView> Fn)> Passes = new List<(string, Action<IHeroView>)>();
        private static int Users => Registry.Count + Globals.Count + Passes.Count;

        private static List<Action> _hooks = new List<Action>();
        private static V_Ability_Manager _mgr;

        /// <summary>Просит игру пересчитать характеристики героя (как после выбора артефакта). false — менеджер ещё не известен.</summary>
        public static bool Recalc()
        {
            var m = _mgr; if (m == null) return false;
            try { m.AbilityStatApply(2); return true; } catch (Exception e) { Log.Write("recalc: " + e.Message); return false; }
        }
        private static C_Battle_AllyUnit _lastHero;
        private static bool _dirty, _recomputed, _inDisplay;
        private static int _tick;
        private static string _lastSig = "", _synLog = "", _lastPatchSig = "";
        private static int _logBudget = 80;
        private static readonly List<Action> TempRestore = new List<Action>();

        private static string Pad(int n) => n.ToString("00");
        private static bool Close(double a, double b) => Math.Abs(a - b) <= 1e-4 * Math.Max(1, Math.Max(Math.Abs(a), Math.Abs(b)));
        private static void L(string s) { if (_logBudget-- > 0) Log.Write("[effects] " + s); }

        // ---------------------------------------------------------------- доступ к полям

        /// <summary>Числовое поле (свойство обёртки Il2Cpp): int или float, instance или static.</summary>
        internal class Prop
        {
            public PropertyInfo P; public bool IsStatic, IsInt;
            public double Get(object target) => Convert.ToDouble(P.GetValue(IsStatic ? null : target));
            public void Set(object target, double v) => P.SetValue(IsStatic ? null : target, IsInt ? (object)(int)Math.Round(v) : (float)v);
        }

        private static readonly Dictionary<string, Prop> PropCache = new Dictionary<string, Prop>();
        private static Prop FindProp(Type t, string name)
        {
            var key = t.Name + "." + name;
            if (PropCache.TryGetValue(key, out var c)) return c;
            var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
            if (p != null && p.PropertyType != typeof(int) && p.PropertyType != typeof(float)) p = null;
            return PropCache[key] = p == null ? null : new Prop { P = p, IsStatic = p.GetMethod?.IsStatic ?? false, IsInt = p.PropertyType == typeof(int) };
        }

        internal class Slot
        {
            public Prop F; public object Target0; public double Base, Target; public bool IsInt, Inst; public string Key;
            public double Current => F.Get(Target0);
        }
        private class Mem { public Prop F; public object Obj; public double Base, Set; public bool Inst; public string HeroKey; }
        private static readonly Dictionary<string, Mem> Written = new Dictionary<string, Mem>();

        private class View : IHeroView
        {
            private readonly C_Battle_AllyUnit _hero; private readonly Dictionary<string, Slot> _slots; private readonly bool _full; private readonly string _heroKey;
            public View(C_Battle_AllyUnit hero, Dictionary<string, Slot> slots, bool full) { _hero = hero; _slots = slots; _full = full; _heroKey = hero.Pointer.ToString(); }
            public C_Battle_AllyUnit Raw => _hero;

            private FieldRef SlotFor(string key, Prop f, object obj, bool inst)
            {
                if (!_slots.TryGetValue(key, out var s))
                {
                    double cur = f.Get(obj), b = cur;
                    // значение всё ещё наше → прежняя база. Иначе: поле героя игра пересчитала целиком → база = текущее;
                    // статик (AllyUnitVar_*) игра сдвигает сама → сдвиг переносим в базу, после полного пересчёта база = текущее
                    if (Written.TryGetValue(key, out var m) && m.HeroKey == _heroKey)
                    {
                        if (Close(cur, m.Set)) b = m.Base;
                        else if (!inst && !_full) b = m.Base + (cur - m.Set);
                    }
                    s = new Slot { F = f, Target0 = obj, Base = b, Target = b, IsInt = f.IsInt, Inst = inst, Key = key };
                    _slots[key] = s;
                }
                return new FieldRef { S = s };
            }

            public FieldRef Field(string name)
            {
                var f = FindProp(typeof(C_Battle_AllyUnit), name) ?? throw new Exception($"у героя нет поля {name}");
                return f.IsStatic ? SlotFor("S:" + name, f, null, false) : SlotFor("H" + _heroKey + ":" + name, f, _hero, true);
            }
            public FieldRef Db(string name)
            {
                var f = FindProp(typeof(Z_Database), name) ?? throw new Exception($"в Z_Database нет поля {name}");
                return SlotFor("D:" + name, f, null, false);
            }
            public void MulPercent(string name, double p) { var r = Field(name); r.Value *= 1 + p / 100; }
            public void Add(string name, double n) { var r = Field(name); r.Value += n; }
            public void Display(string name, Func<double, double> fn)
            {
                if (!_inDisplay) return;
                var f = FindProp(typeof(C_Battle_AllyUnit), name);
                object obj = _hero;
                if (f == null) { f = FindProp(typeof(Z_Database), name); obj = null; }
                if (f == null) return;
                double old = f.Get(obj);
                f.Set(obj, fn(old));
                TempRestore.Add(() => f.Set(obj, old));
            }
        }

        // ---------------------------------------------------------------- герой

        private static C_Battle_AllyUnit FindHero(C_Battle_Manager mgr)
        {
            var field = mgr.Obj_FieldAllyUnit;
            if (field == null) return null;
            var tf = field.transform;
            for (int i = 0; i < tf.childCount; i++)
            {
                var c = tf.GetChild(i).gameObject.GetComponent<C_Battle_AllyUnit>();
                if (c != null) return c;
            }
            return null;
        }

        private static List<int> IntList(Il2CppSystem.Collections.Generic.List<int> l)
        {
            var r = new List<int>();
            if (l == null) return r;
            for (int i = 0; i < l.Count; i++) r.Add(l[i]);
            return r;
        }

        // ---------------------------------------------------------------- источники эффектов

        private class Source { public string Table; public int Id; public string Label; public bool Spill; }

        private static readonly Dictionary<string, Table> Tables = new Dictionary<string, Table>();
        private static Table T(string name)
        {
            if (!Tables.TryGetValue(name, out var t)) Tables[name] = t = new Table(name);
            return t;
        }
        /// <summary>Таблицы кешируем вместе со ссылкой на список; игра могла перезагрузить таблицу — тогда берём заново.</summary>
        private static void DropTableCache() => Tables.Clear();

        private static readonly Dictionary<string, (int Len, Dictionary<int, int> Map)> RowCache = new Dictionary<string, (int, Dictionary<int, int>)>();
        private static int RowOf(Table t, int id)
        {
            int len = t.Length;
            if (!RowCache.TryGetValue(t.Name, out var c) || c.Len != len)
            {
                var map = new Dictionary<int, int>();
                var col = t.IdColumn;
                for (int i = 0; i < len; i++) if (t.Get(i, col) is double d && !map.ContainsKey((int)d)) map[(int)d] = i;
                RowCache[t.Name] = c = (len, map);
            }
            return c.Map.TryGetValue(id, out var r) ? r : -1;
        }

        private class SynRow { public int Id, Need; public List<int> Ids = new List<int>(); }
        private static (int Len, List<SynRow> Rows)? _synCache;
        private static List<SynRow> SynergyRows()
        {
            var t = T("Synergy");
            if (_synCache is { } sc && sc.Len == t.Length) return sc.Rows;
            var rows = new List<SynRow>();
            for (int i = 0; i < t.Length; i++)
            {
                if (!(t.Get(i, t.IdColumn) is double id)) continue;
                var r = new SynRow { Id = (int)id, Need = (int)t.Num(i, "개수") };
                for (int k = 1; k <= 5; k++) { var v = (int)t.Num(i, $"ID_{Pad(k)}"); if (v > 0) r.Ids.Add(v); }
                rows.Add(r);
            }
            _synCache = (t.Length, rows);
            return rows;
        }

        private static List<Source> GameSources()
        {
            var res = new List<Source>();
            void Guard(string what, Action fn) { try { fn(); } catch (Exception e) { L($"{what}: {e.Message}"); } }
            Guard("класс", () => { int id = Z_Database.Game_BattleClassID; if (id > 0) res.Add(new Source { Table = "Class", Id = id, Label = $"класс {id}" }); });
            // синергия действует, когда у героя не меньше «개수» её артефактов (считаем сами)
            Guard("синергии", () =>
            {
                var have = new HashSet<int>(IntList(Z_Database.List_ArtifactObtain));
                var act = new List<int>();
                if (have.Count > 0)
                    foreach (var syn in SynergyRows())
                        if (syn.Ids.Count > 0 && syn.Need > 0 && syn.Ids.Count(have.Contains) >= syn.Need) { act.Add(syn.Id); res.Add(new Source { Table = "Synergy", Id = syn.Id, Label = $"синергия {syn.Id}" }); }
                var sig = string.Join(",", act);
                if (sig != _synLog) { _synLog = sig; L($"включённые синергии: {(sig == "" ? "нет" : sig)}"); }
            });
            if (_mgr != null) Guard("комбинации", () =>
            {
                foreach (var c in IntList(_mgr.List_ApplyMagicComPossible)) res.Add(new Source { Table = "MagicCom", Id = c, Label = $"комбинация {c}" });
            });
            Guard("артефакты", () => { foreach (var a in IntList(Z_Database.List_ArtifactObtain)) res.Add(new Source { Table = "Ability", Id = a, Label = $"артефакт {a}" }); });
            Guard("исследования", () => { foreach (var a in IntList(Z_Database.List_AllyAbilityLearn)) if (a >= 261 && a <= 282) res.Add(new Source { Table = "Ability", Id = a, Label = $"исследование {a}", Spill = true }); });
            return res;
        }

        // ---------------------------------------------------------------- подстановка настоящих эффектов на время пересчёта

        private class Patch { public Table T; public int Row; public string Col; public Il2CppSystem.Object Old, Mine; public IntPtr ListPtr; }
        private static readonly List<Patch> Patches = new List<Patch>();

        private class Plan { public string Key; public List<(int Id, double V, string From)> Applied = new List<(int, double, string)>(); }
        private static Plan _plan;
        private static int _planVersion;

        private static void InvalidatePlan() { _planVersion++; try { Unpatch(); } catch { /* таблицы нет */ } }

        private static void PutCell(Table t, int row, string col, double v)
        {
            // прежний боксированный объект держим в обёртке (она держит его от сборщика мусора) и вернём тем же объектом
            var old = t.GetRaw(row, col);
            t.Set(row, col, v);
            Patches.Add(new Patch { T = t, Row = row, Col = col, Old = old, Mine = t.GetRaw(row, col), ListPtr = t.ListPointer() });
        }

        private static bool PlanIntact()
        {
            try { foreach (var p in Patches) if (p.T.GetRaw(p.Row, p.Col).Pointer != p.Mine.Pointer) return false; return true; }
            catch { return false; }
        }

        private static void BuildPlan(List<Source> srcs, string key)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var writes = new List<(Table T, int Row, string Col, double V)>();
            var applied = new List<(int, double, string)>();
            var free = new List<(Table T, int Row, int N)>();
            var safe = new List<(Table T, int Row, int N)>();     // слоты изученных исследований: игра читает их при каждом пересчёте
            var extra = new List<(int Id, double V, string From)>();
            var sum = new Dictionary<int, double>();
            foreach (var list in Globals.Values) foreach (var (id, v) in list) sum[id] = (sum.TryGetValue(id, out var o) ? o : 0) + v;

            void Scan(Source s)
            {
                var bucket = s.Spill ? safe : free;
                var t = T(s.Table);
                int row = RowOf(t, s.Id);
                if (row < 0) return;
                for (int n = 1; n <= 9; n++)
                {
                    if (!t.HasColumn($"효과ID_{Pad(n)}") || !t.HasColumn($"효과ID_{Pad(n)}값")) break;
                    int id = (int)t.Num(row, $"효과ID_{Pad(n)}"); double val = t.Num(row, $"효과ID_{Pad(n)}값");
                    if (id < 2) { if (val == 0) bucket.Add((t, row, n)); continue; }
                    if (!Registry.TryGetValue(id, out var reg) || reg.Def.Expand == null) continue;
                    if (reg.Def.Apply != null) applied.Add((id, val, s.Label));
                    var pairs = reg.Def.Expand(val);
                    if (pairs.Length == 0) { writes.Add((t, row, $"효과ID_{Pad(n)}", 1)); writes.Add((t, row, $"효과ID_{Pad(n)}값", 0)); continue; }
                    writes.Add((t, row, $"효과ID_{Pad(n)}", pairs[0].Id)); writes.Add((t, row, $"효과ID_{Pad(n)}값", pairs[0].Value));
                    for (int k = 1; k < pairs.Length; k++) extra.Add((pairs[k].Id, pairs[k].Value, $"{s.Label}:{id}"));
                }
            }
            foreach (var s in srcs) if (!s.Spill) Scan(s);
            int Need() => extra.Count + sum.Values.Count(v => v != 0);
            foreach (var s in srcs) { if (!s.Spill) continue; if (safe.Count >= Need()) break; Scan(s); }
            foreach (var kv in sum) if (kv.Value != 0) extra.Add((kv.Key, kv.Value, "глобальный"));
            foreach (var (id, v, from) in extra)
            {
                (Table T, int Row, int N) slot;
                if (safe.Count > 0) { slot = safe[0]; safe.RemoveAt(0); }
                else if (free.Count > 0) { slot = free[0]; free.RemoveAt(0); }
                else { L($"нет свободного слота эффекта для {id}={v} ({from}) — пропущено"); continue; }
                writes.Add((slot.T, slot.Row, $"효과ID_{Pad(slot.N)}", id)); writes.Add((slot.T, slot.Row, $"효과ID_{Pad(slot.N)}값", v));
            }
            foreach (var w in writes) PutCell(w.T, w.Row, w.Col, w.V);
            _plan = new Plan { Key = key, Applied = applied };
            if (sw.ElapsedMilliseconds > 15) L($"подстановка заняла {sw.ElapsedMilliseconds} мс");
            var sig = string.Join(", ", extra.Select(e => $"{e.Id}={e.V}({e.From})"));
            if (sig != _lastPatchSig) { _lastPatchSig = sig; L($"подстановка перед пересчётом: {(sig == "" ? "-" : sig)} (свободных слотов осталось {free.Count + safe.Count})"); }
        }

        private static void PatchRows()
        {
            if (Registry.Count == 0 && Globals.Count == 0) return;
            var srcs = GameSources();
            var key = $"{_planVersion}|{string.Join(",", srcs.Select(s => s.Table + ":" + s.Id))}";
            if (_plan != null && _plan.Key == key && PlanIntact()) return;
            Unpatch();
            DropTableCache();
            BuildPlan(srcs, key);
        }

        private static void Unpatch()
        {
            _plan = null;
            while (Patches.Count > 0)
            {
                var p = Patches[Patches.Count - 1];
                Patches.RemoveAt(Patches.Count - 1);
                try { if (p.T.ListPointer() == p.ListPtr) p.T.SetRaw(p.Row, p.Col, p.Old); }       // таблицу перезагрузили — писать в новую нечего
                catch (Exception e) { L($"откат {p.T.Name}#{p.Row}.{p.Col}: {e.Message}"); }
            }
        }

        // ---------------------------------------------------------------- проход

        private static List<(int Id, double V, string Src)> Collect()
        {
            var res = new List<(int, double, string)>();
            foreach (var s in GameSources())
            {
                var t = T(s.Table);
                int row = RowOf(t, s.Id);
                if (row < 0) continue;
                for (int n = 1; n <= 9; n++)
                {
                    if (!t.HasColumn($"효과ID_{Pad(n)}") || !t.HasColumn($"효과ID_{Pad(n)}값")) break;
                    int id = (int)t.Num(row, $"효과ID_{Pad(n)}");
                    if (id < 2 || !Registry.TryGetValue(id, out var r) || r.Def.Apply == null) continue;
                    res.Add((id, t.Num(row, $"효과ID_{Pad(n)}값"), s.Label));
                }
            }
            if (_plan != null) foreach (var a in _plan.Applied) if (Registry.TryGetValue(a.Id, out var r) && r.Def.Apply != null) res.Add(a);   // их ID в подставленных ячейках не видны
            return res;
        }

        private static void RevertUntouched(Dictionary<string, Slot> touched, string heroKey)
        {
            foreach (var kv in Written.ToList())
            {
                if (touched.ContainsKey(kv.Key)) continue;
                Written.Remove(kv.Key);
                var m = kv.Value;
                if (m.Inst && m.HeroKey != heroKey) continue;       // старый герой
                try { if (Close(m.F.Get(m.Obj), m.Set)) m.F.Set(m.Obj, m.Base); } catch { /* объект уничтожен */ }
            }
        }

        private static void RunPass(C_Battle_AllyUnit hero)
        {
            if (hero == null)
            {
                Unpatch();      // боя нет: таблицы возвращаем к исходным
                foreach (var k in Written.Where(kv => kv.Value.Inst).Select(kv => kv.Key).ToList()) Written.Remove(k);
                return;
            }
            var heroKey = hero.Pointer.ToString();
            var slots = new Dictionary<string, Slot>();
            bool full = _recomputed;
            _recomputed = false;
            var view = new View(hero, slots, full);
            var found = Registry.Count > 0 ? Collect() : new List<(int, double, string)>();
            var sig = string.Join(", ", found.Select(f => $"{f.Item3}:{f.Item1}={f.Item2}"));
            if (sig != _lastSig) { _lastSig = sig; L($"активные эффекты героя: {(sig == "" ? "нет" : sig)}"); }
            foreach (var (id, value, src) in found)
            {
                var r = Registry[id];
                Crash.Enter(r.Ctx.Id, $"эффект {id}");
                try { r.Def.Apply(view, value, r.Ctx); }
                catch (Exception e) { Crash.Error(r.Ctx.Id, $"эффект {id} «{r.Def.Name}» ({src})", e); }
                Crash.Leave();
            }
            BattleState.SetHero(view.Raw);
            foreach (var (owner, fn) in Passes.ToArray())
            {
                Crash.Enter(owner, "проход по герою");
                try { fn(view); } catch (Exception e) { Crash.Error(owner, "проход по герою", e); }
                Crash.Leave();
            }
            foreach (var s in slots.Values)
            {
                double cur = s.Current;
                if (!Close(cur, s.Target))
                {
                    s.F.Set(s.Target0, s.Target);
                    L($"{System.Text.RegularExpressions.Regex.Replace(s.Key, @"^H\d+:", "hero.")}: {cur} -> {s.Target} (база {s.Base})");
                }
                Written[s.Key] = new Mem { F = s.F, Obj = s.Target0, Base = s.Base, Set = s.Current, Inst = s.Inst, HeroKey = heroKey };
            }
            RevertUntouched(slots, heroKey);
        }

        // ---------------------------------------------------------------- хуки

        private static void AttachHooks()
        {
            if (_hooks.Count > 0) return;
            try
            {
                _hooks.Add(Hooks.HookBefore("effects", Hooks.FindMethod("C_Battle_Manager", "Update", 0), (self, a) =>
                {
                    if (Users == 0 && Written.Count == 0 && Patches.Count == 0) return;
                    if (!_dirty && ++_tick % 45 != 0) return;       // раз в ~0,7 с или сразу после пересчёта игрой
                    _dirty = false;
                    try
                    {
                        var hero = FindHero((C_Battle_Manager)self);
                        if (hero != null && (_lastHero == null || _lastHero.Pointer != hero.Pointer)) Log.Write($"[effects] герой найден ({hero.Pointer})");
                        if (hero == null) _mgr = null;
                        _lastHero = hero;
                        RunPass(hero);
                    }
                    catch (Exception e) { L($"проход: {e.Message}"); }
                }));
                // менеджер способностей берём из его Update
                try { _hooks.Add(Hooks.HookBefore("effects", Hooks.FindMethod("V_Ability_Manager", "Update", 0), (self, a) => _mgr = (V_Ability_Manager)self)); }
                catch { /* Update нет — менеджер берём из AbilityStatApply */ }
                // окно характеристик читает поля героя в момент Display_BattleStatus: применяем эффекты прямо перед ним
                _hooks.Add(Hooks.HookBefore("effects", Hooks.FindMethod("C_Battle_AllyUnit", "Display_BattleStatus", 0), (self, a) =>
                {
                    if (Registry.Count == 0 && Passes.Count == 0) return;
                    _inDisplay = true;
                    try { RunPass((C_Battle_AllyUnit)self); } catch (Exception e) { L($"перед окном характеристик: {e.Message}"); }
                    _inDisplay = false;
                }));
                _hooks.Add(Hooks.HookAfter("effects", Hooks.FindMethod("C_Battle_AllyUnit", "Display_BattleStatus", 0), (self, a) =>
                {
                    while (TempRestore.Count > 0) { var f = TempRestore[TempRestore.Count - 1]; TempRestore.RemoveAt(TempRestore.Count - 1); try { f(); } catch { /* */ } }
                }));
                // игра периодически пересчитывает характеристики героя: сразу после этого возвращаем наши значения
                _hooks.Add(Hooks.HookAfter("effects", Hooks.FindMethod("C_Battle_AllyUnit", "Unit_StatBuff", 0), (self, a) => _dirty = true));
                _hooks.Add(Hooks.HookBefore("effects", Hooks.FindMethod("V_Ability_Manager", "AbilityStatApply", 1), (self, a) =>
                {
                    if (self is V_Ability_Manager am) _mgr = am;
                    try { PatchRows(); } catch (Exception e) { Unpatch(); L($"подстановка: {e.Message}"); }
                }));
                _hooks.Add(Hooks.HookAfter("effects", Hooks.FindMethod("V_Ability_Manager", "AbilityStatApply", 1), (self, a) => { _dirty = true; _recomputed = true; }));
                Log.Write("[effects] хуки установлены");
            }
            catch { DetachHooks(); throw; }
        }

        private static void DetachHooks()
        {
            foreach (var h in _hooks) h();
            _hooks = new List<Action>();
            try { Unpatch(); } catch { /* */ }
            _mgr = null; _lastHero = null; _lastSig = "";
            Log.Write("[effects] хуки сняты");
        }

        // ---------------------------------------------------------------- API для контекста

        internal static void Define(EffectDef def, ModContext ctx, List<Action> cleanups)
        {
            if (def.Id < 10000) throw new Exception($"DefineEffect: ID должен быть >= 10000 (получено {def.Id}); меньшие ID занимает игра");
            if (def.Apply == null && def.Expand == null) throw new Exception($"DefineEffect {def.Id}: нужна функция Apply или Expand");
            if (Registry.TryGetValue(def.Id, out var ex)) throw new ModConflictException($"эффект {def.Id} уже зарегистрирован («{ex.Def.Name}», мод {ex.Ctx.Id})");
            Registry[def.Id] = new Registered { Def = def, Ctx = ctx };
            InvalidatePlan();
            AttachHooks();
            _dirty = true;
            Log.Write($"[effects] эффект {def.Id} «{def.Name}» зарегистрирован (мод {ctx.Id})");
            cleanups.Add(() =>
            {
                Registry.Remove(def.Id);
                InvalidatePlan();
                try { RunPass(_lastHero); } catch { /* героя нет */ }       // вернуть поля к базе
                if (Users == 0) DetachHooks();
                Log.Write($"[effects] эффект {def.Id} снят");
            });
        }

        public static void AddPass(string owner, Action<IHeroView> fn, List<Action> cleanups)
        {
            var item = (owner, fn);
            Passes.Add(item);
            AttachHooks();
            _dirty = true;
            cleanups.Add(() =>
            {
                Passes.Remove(item);
                try { RunPass(_lastHero); } catch { /* героя нет */ }       // вернуть поля к базе
                if (Users == 0) DetachHooks();
            });
        }

        internal static void SetGlobal(string owner, (int, double)[] pairs, List<Action> cleanups)
        {
            bool had = Globals.ContainsKey(owner);
            if (pairs.Length > 0) Globals[owner] = pairs.ToArray(); else Globals.Remove(owner);
            InvalidatePlan();
            if (pairs.Length > 0) AttachHooks();
            if (!had && pairs.Length > 0) cleanups.Add(() => { Globals.Remove(owner); InvalidatePlan(); if (Users == 0) DetachHooks(); });
            Log.Write($"[effects] глобальные эффекты {owner}: {(pairs.Length == 0 ? "нет" : string.Join(", ", pairs.Select(p => $"{p.Item1}={p.Item2}")))}");
        }

        // ---------------------------------------------------------------- встроенные эффекты

        /// <summary>Значения в процентах, если не сказано иное. Положительное число всегда «лучше для игрока».</summary>
        public static readonly EffectDef[] Builtin =
        {
            new EffectDef { Id = 10001, Name = "Урон магической пули %",
                Apply = (h, v, c) => { h.MulPercent("UnitApply_MagicBulletDamage", v); h.Display("AllyUnitVar_MagicDamageRatioU", x => x + v); } },
            new EffectDef { Id = 10002, Name = "Перезарядка заклинаний −%",
                // UnitApply_MagicCooltimeRatio — множитель времени перезарядки (0.88 = быстрее на 12%), меньше — лучше
                Apply = (h, v, c) => { var r = h.Field("UnitApply_MagicCooltimeRatio"); r.Value = Math.Max(0.05, r.Value * (1 - v / 100)); h.Display("UnitPrint_MagicCooltimeRatio", x => x * (1 - v / 100)); } },
            new EffectDef { Id = 10003, Name = "Снарядов магической пули +N, молний +N",
                Apply = (h, v, c) => { h.Add("UnitApply_MagicBulletNum", v); h.Add("UnitApply_ThunderNum", v); } },
            new EffectDef { Id = 10004, Name = "Радиус сбора предметов %",
                // само поле UnitApply_ItemObtainArea не трогаем: игра масштабирует его по статику; статик правим только на время показа окна
                Apply = (h, v, c) => h.Display("AllyUnitVar_ItemObtainAreaU", x => x + v) },
        };

        internal static void UseBuiltin(ModContext ctx, List<Action> cleanups)
        {
            foreach (var e in Builtin) if (!Registry.ContainsKey(e.Id)) Define(e, ctx, cleanups);     // уже подключены другим модом — пропускаем
        }
    }
}
