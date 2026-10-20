using System;
using System.Collections.Generic;
using System.Linq;

namespace ModKit
{
    /// <summary>Общие реестры: по имени и типу значения; записи привязаны к модам-владельцам.</summary>
    internal static class Registries
    {
        private static readonly Dictionary<string, object> Stores = new Dictionary<string, object>();

        public static IRegistry<T> Get<T>(string name, string owner, List<Action> cleanups)
        {
            var key = name + "|" + typeof(T).FullName;
            if (!Stores.TryGetValue(key, out var store)) Stores[key] = store = new Store<T>(name);
            return new View<T>((Store<T>)store, owner, cleanups);
        }

        private class Store<T>
        {
            public readonly string Name;
            public readonly Dictionary<string, (T value, string owner)> Items = new Dictionary<string, (T, string)>();
            public Store(string name) { Name = name; }
        }

        private class View<T> : IRegistry<T>
        {
            private readonly Store<T> _s; private readonly string _owner; private readonly List<Action> _cleanups;
            public View(Store<T> s, string owner, List<Action> cleanups) { _s = s; _owner = owner; _cleanups = cleanups; }

            public string Name => _s.Name;

            public void Register(string id, T value)
            {
                _s.Items[id] = (value, _owner);
                Notify(id, true);
                _cleanups.Add(() => { if (_s.Items.TryGetValue(id, out var cur) && cur.owner == _owner) { _s.Items.Remove(id); Notify(id, false); } });
            }

            public bool Unregister(string id)
            {
                if (!_s.Items.TryGetValue(id, out var cur) || cur.owner != _owner) return false;
                _s.Items.Remove(id);
                Notify(id, false);
                return true;
            }

            public bool TryGet(string id, out T value)
            {
                if (_s.Items.TryGetValue(id, out var cur)) { value = cur.value; return true; }
                value = default;
                return false;
            }

            public IReadOnlyDictionary<string, T> All => _s.Items.ToDictionary(kv => kv.Key, kv => kv.Value.value);

            private void Notify(string id, bool added) =>
                EventBus.Emit(EventBus.TypeKey(typeof(RegistryChanged)), new RegistryChanged { Registry = _s.Name, Id = id, Owner = _owner, Added = added });
        }
    }
}
