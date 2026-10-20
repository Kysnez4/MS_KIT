using System;
using System.Collections.Generic;

namespace ModKit
{
    /// <summary>
    /// Шина событий. Игровые события: mainMenu {menu}, battleStart/battleEnd/areaChange {manager}, enemySpawn/enemyDeath {enemy},
    /// bossSpawn {manager, unitId, count}, modEnabled/modDisabled {id}. Данные — <see cref="EventData"/> (поле → значение).
    /// Свои события мод шлёт через ctx.Events.Emit("мой_мод:событие", данные).
    /// </summary>
    public class EventData : Dictionary<string, object>
    {
        public T Get<T>(string key) => TryGetValue(key, out var v) && v is T t ? t : default;
    }

    internal static class EventBus
    {
        private class Sub { public Func<bool> IsActive; public Action<object> Fn; public string ModId; }
        private static readonly Dictionary<string, List<Sub>> Subs = new Dictionary<string, List<Sub>>();

        private class HookSpec
        {
            public string Cls, Method; public int Params; public bool Leave;
            public Func<object, object[], EventData> Payload;
        }

        private static EventData D(params (string, object)[] kv)
        {
            var d = new EventData();
            foreach (var (k, v) in kv) d[k] = v;
            return d;
        }

        private static readonly Dictionary<string, HookSpec> Specs = new Dictionary<string, HookSpec>
        {
            ["battleStart"] = new HookSpec { Cls = "C_Battle_Manager", Method = "Start", Params = 0, Leave = true, Payload = (s, a) => D(("manager", s)) },
            ["battleEnd"] = new HookSpec { Cls = "C_Battle_Manager", Method = "End_Battle", Params = 0, Payload = (s, a) => D(("manager", s)) },
            ["areaChange"] = new HookSpec { Cls = "C_Battle_Manager", Method = "Start_AreaChange", Params = 0, Leave = true, Payload = (s, a) => D(("manager", s)) },
            ["bossSpawn"] = new HookSpec { Cls = "C_Battle_Manager", Method = "Create_UnitEnemyBoss", Params = 2, Payload = (s, a) => D(("manager", s), ("unitId", Convert.ToInt32(a[0])), ("count", Convert.ToInt32(a[1]))) },
            ["enemySpawn"] = new HookSpec { Cls = "C_Battle_EnemyUnit", Method = "Start_Unit", Params = 3, Leave = true, Payload = (s, a) => D(("enemy", s)) },
            ["enemyDeath"] = new HookSpec { Cls = "C_Battle_EnemyUnit", Method = "Unit_Death", Params = 0, Payload = (s, a) => D(("enemy", s), ("unitId", ((Il2Cpp.C_Battle_EnemyUnit)s).UnitID)) },
            ["heroDamaged"] = new HookSpec { Cls = "C_Battle_AllyUnit", Method = "Unit_Damage", Params = 3, Payload = (s, a) => D(("amount", Convert.ToDouble(a[0]))) },
            ["manaPickup"] = new HookSpec { Cls = "V_Ability_Manager", Method = "ManaGain", Params = 1, Payload = (s, a) => D(("amount", Convert.ToDouble(a[0]))) },
            ["itemPickup"] = new HookSpec { Cls = "C_Battle_Item", Method = "Obtain_Item", Params = 0, Payload = (s, a) => { var it = (Il2Cpp.C_Battle_Item)s; return D(("type", it.ObtainItemType), ("objType", it.Type_ObjItem)); } },
        };

        /// <summary>Хуки событий: снимаются, когда подписчиков не осталось.</summary>
        private static readonly Dictionary<string, List<Action>> HookCleanups = new Dictionary<string, List<Action>>();

        private static void EnsureHook(string name)
        {
            if (!Specs.TryGetValue(name, out var spec) || HookCleanups.ContainsKey(name)) return;
            var cl = new List<Action>();
            HookCleanups[name] = cl;
            void Fire(object self, object[] args)
            {
                if (!Subs.TryGetValue(name, out var l) || l.Count == 0) return;
                Emit(name, spec.Payload(self, args));
            }
            try
            {
                if (spec.Leave) Hooks.AddAfter("events", spec.Cls, spec.Method, spec.Params, Fire, cl);
                else Hooks.AddBefore("events", spec.Cls, spec.Method, spec.Params, Fire, cl);
            }
            catch (Exception e)
            {
                HookCleanups.Remove(name);
                Log.Write($"events: метод {spec.Cls}.{spec.Method} не найден, событие {name} не будет приходить ({e.Message})");
            }
        }

        private static void DropHookIfUnused(string name)
        {
            if (Subs.TryGetValue(name, out var l) && l.Count > 0) return;
            if (!HookCleanups.TryGetValue(name, out var cl)) return;
            foreach (var c in cl) c();
            HookCleanups.Remove(name);
        }

        public static void Emit(string name, object payload)
        {
            if (!Subs.TryGetValue(name, out var list)) return;
            foreach (var s in list.ToArray())
            {
                if (!s.IsActive()) continue;
                Crash.Enter(s.ModId, "событие " + name);
                try { s.Fn(payload); } catch (Exception e) { Crash.Error(s.ModId, "событие " + name, e); }
                Crash.Leave();
            }
        }

        /// <summary>Ключ типизированного события: полное имя типа (одинаков у модов, делящих тип через библиотеку).</summary>
        internal static string TypeKey(Type t) => "type:" + t.FullName;

        public static ModEvents For(string modId, Func<bool> isActive, List<Action> cleanups) => new Bus(modId, isActive, cleanups);

        private class Bus : ModEvents
        {
            private readonly string _id; private readonly Func<bool> _active; private readonly List<Action> _cleanups;
            public Bus(string id, Func<bool> active, List<Action> cleanups) { _id = id; _active = active; _cleanups = cleanups; }

            public void On(string name, Action<object> fn)
            {
                var sub = new Sub { IsActive = _active, Fn = fn, ModId = _id };
                if (!Subs.TryGetValue(name, out var list)) Subs[name] = list = new List<Sub>();
                list.Add(sub);
                EnsureHook(name);
                _cleanups.Add(() => { list.Remove(sub); DropHookIfUnused(name); });
            }

            public void Emit(string name, object payload = null) => EventBus.Emit(name, payload);

            public void On<T>(Action<T> fn) => On(TypeKey(typeof(T)), o => fn((T)o));

            public void Emit<T>(T payload) => EventBus.Emit(TypeKey(typeof(T)), payload);
        }
    }
}
