using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace ModKit
{
    /// <summary>
    /// Хуки по имени класса/метода поверх Harmony. Один Harmony-патч на метод; под ним список «до», «после» и замена.
    /// Патч снимается, когда на методе никого не осталось.
    /// </summary>
    public static class Hooks
    {
        private class Entry { public string Owner, Tag; public Action<object, object[]> Fn; public Func<object, object[], object> Replace; }

        private class Slot
        {
            public readonly List<Entry> Before = new List<Entry>();
            public readonly List<Entry> After = new List<Entry>();
            public Entry Replace;
            public bool ResultPatch;
        }

        private static readonly HarmonyLib.Harmony Hx = new HarmonyLib.Harmony("modkit.hooks");
        private static readonly Dictionary<MethodBase, Slot> Slots = new Dictionary<MethodBase, Slot>();
        /// <summary>Методы, заменённые через Replace: ключ -> id мода.</summary>
        private static readonly Dictionary<string, string> Replaced = new Dictionary<string, string>();

        // ---------------------------------------------------------------- поиск типов и методов

        public static Type FindType(string name)
        {
            string asm = "Assembly-CSharp", cls = name;
            int i = name.IndexOf(':');
            if (i >= 0) { asm = name.Substring(0, i); cls = name.Substring(i + 1); }
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                var an = a.GetName().Name;
                if (an != asm && an != "Il2Cpp" + asm) continue;
                foreach (var cand in new[] { "Il2Cpp." + cls, cls, "Il2Cpp" + cls })
                {
                    var t = a.GetType(cand, false);
                    if (t != null) return t;
                }
            }
            throw new Exception($"класс не найден: {name}");
        }

        public static MethodInfo FindMethod(string className, string method, int paramCount)
        {
            var t = FindType(className);
            const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
            var m = t.GetMethods(all | BindingFlags.DeclaredOnly).FirstOrDefault(x => x.Name == method && x.GetParameters().Length == paramCount)
                    ?? t.GetMethods(all).FirstOrDefault(x => x.Name == method && x.GetParameters().Length == paramCount);
            if (m == null) throw new Exception($"метод не найден: {className}.{method}({paramCount})");
            return m;
        }

        // ---------------------------------------------------------------- общие обработчики Harmony

        private static readonly MethodInfo PrefixVoid = AccessTools.Method(typeof(Hooks), nameof(PrefixV));
        private static readonly MethodInfo PrefixResult = AccessTools.Method(typeof(Hooks), nameof(PrefixR));
        private static readonly MethodInfo PostfixM = AccessTools.Method(typeof(Hooks), nameof(PostfixV));

        private static bool PrefixV(object __instance, object[] __args, MethodBase __originalMethod)
        {
            if (!Slots.TryGetValue(__originalMethod, out var s)) return true;
            Run(s.Before, __instance, __args);
            if (s.Replace == null) return true;
            Call(s.Replace, __instance, __args, out _);
            return false;
        }

        private static bool PrefixR(object __instance, object[] __args, MethodBase __originalMethod, ref object __result)
        {
            if (!Slots.TryGetValue(__originalMethod, out var s)) return true;
            Run(s.Before, __instance, __args);
            if (s.Replace == null) return true;
            Call(s.Replace, __instance, __args, out var r);
            __result = r;
            return false;
        }

        private static void PostfixV(object __instance, object[] __args, MethodBase __originalMethod)
        {
            if (Slots.TryGetValue(__originalMethod, out var s)) Run(s.After, __instance, __args);
        }

        private static int _slowLogs;
        private static void Run(List<Entry> list, object self, object[] args)
        {
            if (list.Count == 0) return;
            foreach (var e in list.ToArray())
            {
                Crash.Enter(e.Owner, e.Tag);
                long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                try { e.Fn(self, args); } catch (Exception ex) { Crash.Error(e.Owner, "хук " + e.Tag, ex); }
                Crash.Leave();
                double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                if (ms > 8 && _slowLogs++ < 30) Log.Write($"медленный хук [{e.Owner}] {e.Tag}: {ms:0} мс");
            }
        }

        private static void Call(Entry e, object self, object[] args, out object result)
        {
            result = null;
            Crash.Enter(e.Owner, e.Tag);
            try { result = e.Replace(self, args); } catch (Exception ex) { Crash.Error(e.Owner, "замена " + e.Tag, ex); }
            Crash.Leave();
        }

        /// <summary>
        /// Патч с параметром __result ставится только когда у метода есть замена (Replace): на обычных хуках «до/после»
        /// он ломал результат методов (характеристики героя обнулялись), поэтому там используется префикс без __result.
        /// </summary>
        private static Slot SlotOf(MethodInfo m, bool needResult = false)
        {
            bool hasResult = m.ReturnType != typeof(void);
            if (Slots.TryGetValue(m, out var s))
            {
                if (!(needResult && hasResult && !s.ResultPatch)) return s;
                Hx.Unpatch(m, HarmonyPatchType.All, Hx.Id);        // перепатчиваем с префиксом, умеющим подменять результат
            }
            else { s = new Slot(); Slots[m] = s; }
            try
            {
                var prefix = needResult && hasResult ? PrefixResult : PrefixVoid;
                Hx.Patch(m, prefix: new HarmonyMethod(prefix), postfix: new HarmonyMethod(PostfixM));
                s.ResultPatch = needResult && hasResult;
            }
            catch
            {
                Slots.Remove(m);
                throw;
            }
            return s;
        }

        // ---------------------------------------------------------------- публичный API

        /// <summary>Хук «до» на конкретный метод (если у метода несколько перегрузок). Возвращает действие, снимающее хук.</summary>
        public static Action HookBefore(string owner, MethodInfo m, Action<object, object[]> fn)
        {
            var e = new Entry { Owner = owner, Fn = fn, Tag = $"{m.DeclaringType?.Name}.{m.Name} (до)" };
            SlotOf(m).Before.Add(e);
            return () => Drop(m, s => s.Before.Remove(e));
        }

        /// <summary>Хук «после» на конкретный метод. Возвращает действие, снимающее хук.</summary>
        public static Action HookAfter(string owner, MethodInfo m, Action<object, object[]> fn)
        {
            var e = new Entry { Owner = owner, Fn = fn, Tag = $"{m.DeclaringType?.Name}.{m.Name} (после)" };
            SlotOf(m).After.Add(e);
            return () => Drop(m, s => s.After.Remove(e));
        }

        public static void AddBefore(string owner, string cls, string method, int n, Action<object, object[]> fn, List<Action> cleanups) =>
            cleanups.Add(HookBefore(owner, FindMethod(cls, method, n), fn));

        public static void AddAfter(string owner, string cls, string method, int n, Action<object, object[]> fn, List<Action> cleanups) =>
            cleanups.Add(HookAfter(owner, FindMethod(cls, method, n), fn));

        public static void AddReplace(string owner, string cls, string method, int n, Func<object, object[], object> impl, List<Action> cleanups)
        {
            var key = $"{cls}.{method}({n})";
            if (Replaced.TryGetValue(key, out var other) && other != owner)
                throw new ModConflictException($"конфликт с модом «{other}»: оба заменяют {key}");
            var m = FindMethod(cls, method, n);
            var e = new Entry { Owner = owner, Replace = impl, Tag = key + " (замена)" };
            SlotOf(m, true).Replace = e;
            Replaced[key] = owner;
            cleanups.Add(() =>
            {
                Replaced.Remove(key);
                Drop(m, s => { if (s.Replace == e) s.Replace = null; });
            });
        }

        private static void Drop(MethodInfo m, Action<Slot> remove)
        {
            if (!Slots.TryGetValue(m, out var s)) return;
            remove(s);
            if (s.Before.Count == 0 && s.After.Count == 0 && s.Replace == null)
            {
                Hx.Unpatch(m, HarmonyPatchType.All, Hx.Id);
                Slots.Remove(m);
            }
        }
    }
}
