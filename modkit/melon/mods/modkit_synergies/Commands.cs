using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Il2Cpp;
using ModKit;

namespace ModKitData
{
    internal static class SynergyCommands
    {
        private static readonly string[] Slots = { "ID_01", "ID_02", "ID_03", "ID_04", "ID_05" };

        private static List<int> Members(Table t, int i) =>
            Slots.Select(c => t.Get(i, c) is double d ? (int)d : 0).Where(v => v > 0).ToList();

        private static void SetMembers(Table t, int id, List<int> list, Action<Action> undo)
        {
            if (list.Count > Slots.Length) throw new Exception($"Synergy: в синергии не больше {Slots.Length} артефактов");
            var changes = new Dictionary<string, object> { ["개수"] = list.Count };
            for (int k = 0; k < Slots.Length; k++) changes[Slots[k]] = k < list.Count ? list[k] : 0;
            Commands.SetCells(t, id, changes, undo);
        }

        public static int AddSynergy(SynergyDef def, Action<Action> undo, string owner)
        {
            var t = new Table("Synergy");
            var idCol = t.IdColumn;
            int src = t.IndexOf(def.CloneFrom, idCol);
            if (src < 0) throw new Exception($"Synergy: нет образца {def.CloneFrom}");
            int row = t.CloneRow(src);
            int id = (int)t.MaxId() + 1;
            t.Set(row, idCol, id);
            Sprites.Ensure("SpriteList_SynergyPortrait", id, def.Icon ?? def.CloneFrom);
            int was = Sprites.GrowInts("Array_SynergyObtain", id + 1);      // счётчики синергий по ID: без этого Start_Ability падает с IndexOutOfRange
            Log.Write($"[{owner}] Array_SynergyObtain: длина была {was}, нужна {id + 1}");
            t.Set(row, "이름", def.Name);
            foreach (var kv in Commands.TextChanges(t, def.Effects, def.Lines ?? Commands.DefaultLines(def.Effects), 3, 3)) t.Set(row, kv.Key, kv.Value);
            var list = def.Members ?? new int[0];
            t.Set(row, "개수", list.Length);
            for (int k = 0; k < Slots.Length; k++) t.Set(row, Slots[k], k < list.Length ? list[k] : 0);
            undo(() =>
            {
                var tt = new Table("Synergy");
                int ti = tt.IndexOf(id, idCol);
                if (ti == tt.Length - 1) tt.RemoveLast();
                else Log.Write($"[{owner}] синергию {id} нельзя убрать: после неё добавлены другие");
            });
            Log.Write($"[{owner}] синергия «{def.Name}»: ID {id}, образец {def.CloneFrom}, артефактов {list.Length}");
            return id;
        }

        public static void RemoveSynergy(int id, Action<Action> undo, string owner)
        {
            SetMembers(new Table("Synergy"), id, new List<int>(), undo);
            Log.Write($"[{owner}] синергия {id} очищена (артефактов нет — не срабатывает)");
        }

        public static void SynergyAddArtifact(int synergyId, int artifactId, Action<Action> undo, string owner)
        {
            var t = new Table("Synergy");
            int i = t.IndexOf(synergyId, t.IdColumn);
            if (i < 0) throw new Exception($"Synergy: нет строки {synergyId}");
            var list = Members(t, i);
            if (list.Contains(artifactId)) { Log.Write($"[{owner}] синергия {synergyId}: артефакт {artifactId} уже есть"); return; }
            if (list.Count >= Slots.Length) throw new Exception($"Synergy {synergyId}: нет свободных мест (максимум {Slots.Length})");
            list.Add(artifactId);
            SetMembers(t, synergyId, list, undo);
            Log.Write($"[{owner}] синергия {synergyId}: добавлен артефакт {artifactId}");
        }

        public static void SynergyRemoveArtifact(int synergyId, int artifactId, Action<Action> undo, string owner)
        {
            var t = new Table("Synergy");
            int i = t.IndexOf(synergyId, t.IdColumn);
            if (i < 0) throw new Exception($"Synergy: нет строки {synergyId}");
            var list = Members(t, i);
            if (!list.Contains(artifactId)) { Log.Write($"[{owner}] синергия {synergyId}: артефакта {artifactId} нет"); return; }
            list.Remove(artifactId);
            SetMembers(t, synergyId, list, undo);
            Log.Write($"[{owner}] синергия {synergyId}: убран артефакт {artifactId}");
        }
    }
}
