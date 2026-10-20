using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Il2Cpp;
using ModKit;

namespace ModKitData
{
    /// <summary>
    /// Сырой доступ к данным игры (аналог data.raw): таблицы Z_Database.Dictionary_*, строки, списки, отложенный запуск. Все правки откатываются при выключении мода.
    /// Использование из мода: <c>var data = ctx.Use&lt;IData&gt;("modkit_data");</c> (в mod.json: depends modkit_data). Вызывать правки из <see cref="IData.WhenReady"/>.
    /// Готовые рецепты — в библиотеках modkit_artifacts, modkit_combos, modkit_synergies, modkit_spells_data.
    /// </summary>
    public interface IData
    {
        /// <summary>Вызывает fn на главном потоке, когда игра загрузила таблицы (обычно через несколько секунд после старта). По одному моду за заход.</summary>
        void WhenReady(Action fn);
        /// <summary>Сырой доступ к таблице: Ability, MagicCom, Synergy, Class, AllStat, UnitStat, BattleUnitEnemy, Explain, Name.</summary>
        Table Table(string name);
        /// <summary>Меняет ячейки строки с данным ID. Пример: Edit("Ability", 41, new() { ["이름"] = "Новое имя" }).</summary>
        void Edit(string table, int id, Dictionary<string, object> changes);
        /// <summary>Добавляет копию строки cloneId с новым ID (возвращается) и применяет изменения.</summary>
        int Add(string table, int cloneId, Dictionary<string, object> changes);
        /// <summary>Добавляет число в статический List&lt;int&gt; Z_Database (например List_ArtifactPossibleGrade_C).</summary>
        void AddToList(string listField, int value);
    }

    internal class Impl : IData
    {
        private class ReadyItem { public Action Fn; }
        private static readonly List<ReadyItem> Queue = new List<ReadyItem>();
        /// <summary>Ячейки, изменённые через Edit: «Таблица#ID#колонка» → id мода.</summary>
        private static readonly Dictionary<string, string> Edited = new Dictionary<string, string>();

        private readonly ModContext _ctx;
        public Impl(ModContext ctx) { _ctx = ctx; }

        private static bool TablesLoaded()
        {
            try { return new Table("Ability").Length > 0 && new Table("MagicCom").Length > 0 && new Table("Synergy").Length > 0; }
            catch { return false; }
        }

        /// <summary>Очередь WhenReady обслуживает таймер библиотеки (запускается в её OnEnable).</summary>
        internal static void Pump()
        {
            if (Queue.Count == 0 || !TablesLoaded()) return;
            var item = Queue[0];
            Queue.RemoveAt(0);
            try { item.Fn(); } catch (Exception e) { Log.Write($"[modkit_data] ошибка в WhenReady: {e}"); }
        }

        private void Undo(Action a) => _ctx.OnCleanup(a);

        public void WhenReady(Action fn)
        {
            var item = new ReadyItem { Fn = fn };
            Queue.Add(item);
            _ctx.OnCleanup(() => Queue.Remove(item));
        }

        public Table Table(string name) => new Table(name);

        public void Edit(string table, int id, Dictionary<string, object> changes)
        {
            var t = new Table(table);
            int i = t.IndexOf(id, t.IdColumn);
            if (i < 0) throw new Exception($"{table}: нет строки с ID {id}");
            var idCol = t.IdColumn;
            var keys = new List<string>();
            foreach (var col in changes.Keys)
            {
                var key = $"{table}#{id}#{col}";
                if (Edited.TryGetValue(key, out var other) && other != _ctx.Id) _ctx.Warn($"«{col}» ({table} #{id}) уже правит мод {other}");
                else { Edited[key] = _ctx.Id; keys.Add(key); }
            }
            _ctx.OnCleanup(() => { foreach (var k in keys) Edited.Remove(k); });
            Commands.SetCells(t, id, changes, Undo);
        }

        public int Add(string table, int cloneId, Dictionary<string, object> changes)
        {
            var t = new Table(table);
            var idCol = t.IdColumn;
            int from = t.IndexOf(cloneId, idCol);
            if (from < 0) throw new Exception($"{table}: нет строки с ID {cloneId}");
            int i = t.CloneRow(from);
            int newId = (int)t.MaxId() + 1;
            t.Set(i, idCol, newId);
            foreach (var kv in changes) t.Set(i, kv.Key, kv.Value);
            _ctx.OnCleanup(() =>
            {
                var tt = new Table(table);
                int ti = tt.IndexOf(newId, idCol);
                if (ti == tt.Length - 1) tt.RemoveLast();
                else Log.Write($"[{_ctx.Id}] строку {newId} в {table} нельзя убрать: после неё добавлены другие");
            });
            return newId;
        }

        public void AddToList(string listField, int value)
        {
            var l = Commands.Pool(listField);
            l.Add(value);
            _ctx.OnCleanup(() => l.Remove(value));
        }
    }

    public class DataLib : IKitMod, ILibrary<IData>
    {
        public ModDef Define() => new ModDef
        {
            Id = "modkit_data", Name = "ModKit: данные игры", Version = "1.5", Library = true,
            OnEnable = ctx =>
            {
                ctx.Every(160, Impl.Pump);
                ctx.Expose(this);
            },
        };

        IData ILibrary<IData>.For(ModContext ctx) => new Impl(ctx);
    }
}
