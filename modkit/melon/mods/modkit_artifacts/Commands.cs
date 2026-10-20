using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Il2Cpp;
using ModKit;

namespace ModKitData
{
    /// <summary>Правка артефактов: редкость, пулы выпадения, запрет и возврат. Каждая команда записывает откат в undo.</summary>
    internal static class ArtifactCommands
    {
        /// <summary>Артефакты, запрещённые модами: игра наполняет пулы позже запуска мода и при старте боя, поэтому вычищаем периодически.</summary>
        public static readonly HashSet<int> Banned = new HashSet<int>();
        private static readonly Dictionary<int, List<(string pool, int n)>> Removed = new Dictionary<int, List<(string, int)>>();

        public static void EditArtifact(int id, ArtifactChanges ch, Action<Action> undo, string owner)
        {
            var t = new Table("Ability");
            var changes = Commands.TextChanges(t, ch.Effects, ch.Lines, 9, 6, "효과 설명줄 개수");
            if (ch.Name != null) changes["이름"] = ch.Name;
            Commands.SetCells(t, id, changes, undo);
            Artifacts.UpdateAdded(id, ch.Name, ch.Lines);
            if (ch.Rarity != null)
            {
                var was = Commands.TakeFromPools(id);
                int total = was.Where(w => w.pool != "Drop").Sum(w => w.n);
                if (total == 0) total = 5;
                var parts = new List<(string, int)> { (ch.Rarity, total) };
                var drop = was.FirstOrDefault(w => w.pool == "Drop");
                if (drop.pool != null) parts.Add(("Drop", drop.n));
                Commands.PutToPools(id, parts);
                undo(() => { Commands.TakeFromPools(id); Commands.PutToPools(id, was); });
                Log.Write($"[{owner}] артефакт {id}: редкость -> {ch.Rarity} (копий {total})");
            }
            Log.Write($"[{owner}] артефакт {id} изменён");
        }

        public static void RemoveArtifact(int id, Action<Action> undo, string owner)
        {
            var was = Commands.TakeFromPools(id);
            Banned.Add(id);
            if (was.Count > 0) Removed[id] = was;
            else if (!Removed.ContainsKey(id)) Removed[id] = new List<(string, int)> { ("B", 5) };    // пулы ещё пусты: при возврате 5 копий (обычный вес)
            undo(() => RestoreArtifact(id, owner));
            Log.Write($"[{owner}] артефакт {id} убран из выпадения ({(was.Count > 0 ? string.Join(", ", was.Select(w => $"{w.pool}×{w.n}")) : "пулы пока пусты — будет вычищен позже")})");
        }

        public static void RestoreArtifact(int id, string owner)
        {
            Banned.Remove(id);
            if (!Removed.TryGetValue(id, out var was)) { Log.Write($"[{owner}] RestoreArtifact {id}: он не был убран"); return; }
            Commands.TakeFromPools(id);                       // не плодим дубликаты, если вернули руками
            Commands.PutToPools(id, was);
            Removed.Remove(id);
            Log.Write($"[{owner}] артефакт {id} возвращён в выпадение");
        }
    }
}
