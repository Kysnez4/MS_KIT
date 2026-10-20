using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Il2Cpp;
using ModKit;

namespace ModKitData
{
    internal static class ComboCommands
    {
        private static readonly string[] ReqCols = { "특성A레벨", "특성B레벨", "특성C레벨" };

        public static void EditCombo(int id, ComboChanges ch, Action<Action> undo, string owner)
        {
            var t = new Table("MagicCom");
            var changes = Commands.TextChanges(t, ch.Effects, ch.Lines, 9, 6);
            if (ch.Name != null) changes["이름"] = ch.Name;
            if (ch.Requires != null) for (int k = 0; k < 3; k++) changes[ReqCols[k]] = k < ch.Requires.Length ? ch.Requires[k] : 0;
            Commands.SetCells(t, id, changes, undo);
            Log.Write($"[{owner}] комбинация {id} изменена");
        }

        public static int AddCombo(ComboDef def, Action<Action> undo, string owner)
        {
            var t = new Table("MagicCom");
            var idCol = t.IdColumn;
            int src = t.IndexOf(def.CloneFrom, idCol);
            if (src < 0) throw new Exception($"MagicCom: нет образца {def.CloneFrom}");
            int row = t.CloneRow(src);
            int id = (int)t.MaxId() + 1;
            t.Set(row, idCol, id);
            Sprites.Ensure("SpriteList_MagicComPortrait", id, def.Icon ?? def.CloneFrom);
            t.Set(row, "이름", def.Name);
            foreach (var kv in Commands.TextChanges(t, def.Effects, def.Lines ?? Commands.DefaultLines(def.Effects), 9, 6)) t.Set(row, kv.Key, kv.Value);
            if (def.Requires != null) for (int k = 0; k < 3; k++) t.Set(row, ReqCols[k], k < def.Requires.Length ? def.Requires[k] : 0);
            undo(() =>
            {
                var tt = new Table("MagicCom");
                int ti = tt.IndexOf(id, idCol);
                if (ti == tt.Length - 1) tt.RemoveLast();
                else Log.Write($"[{owner}] комбинацию {id} нельзя убрать: после неё добавлены другие");
            });
            Log.Write($"[{owner}] комбинация «{def.Name}»: ID {id}, образец {def.CloneFrom}");
            return id;
        }
    }
}
