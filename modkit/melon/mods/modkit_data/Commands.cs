using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
using Il2Cpp;
using ModKit;

namespace ModKitData
{
    /// <summary>Спрайты Z_Database.SpriteList_* через указатели (обёртка Sprite роняет Mono, как и Texture).</summary>
    public static unsafe class Sprites
    {
        private static IntPtr _zdb;
        private static IntPtr Zdb => _zdb != IntPtr.Zero ? _zdb : _zdb = Raw.Class("Assembly-CSharp.dll", "", "Z_Database");

        private static IntPtr ArrayOf(IntPtr field)
        {
            IntPtr arr = IntPtr.Zero;
            IL2CPP.il2cpp_field_static_get_value(field, &arr);
            return arr;
        }

        public static IntPtr Get(string fieldName, int index)
        {
            var arr = ArrayOf(IL2CPP.GetIl2CppField(Zdb, fieldName));
            if (arr == IntPtr.Zero || index < 0 || index >= Marshal.ReadInt32(arr + 0x18)) return IntPtr.Zero;
            return Marshal.ReadIntPtr(arr + 0x20 + index * IntPtr.Size);
        }

        /// <summary>Увеличивает статический int[] (например Array_SynergyObtain) до длины не меньше minLen: игра индексирует такие массивы по ID строки таблицы.</summary>
        public static int GrowInts(string fieldName, int minLen)
        {
            var field = IL2CPP.GetIl2CppField(Zdb, fieldName);
            var arr = ArrayOf(field);
            int before = Marshal.ReadInt32(arr + 0x18);
            if (before >= minLen) return before;
            var elem = IL2CPP.il2cpp_class_get_element_class(IL2CPP.il2cpp_object_get_class(arr));
            var bigger = IL2CPP.il2cpp_array_new(elem, (ulong)minLen);
            for (int i = 0; i < before; i++) Marshal.WriteInt32(bigger + 0x20 + i * 4, Marshal.ReadInt32(arr + 0x20 + i * 4));
            IL2CPP.il2cpp_field_static_set_value(field, (void*)bigger);
            return before;
        }

        /// <summary>
        /// Гарантирует, что в массиве есть элемент index (при необходимости массив растёт) и кладёт в него спрайт из copyFrom.
        /// Без этого новый ID выходит за границы массива иконок и игра падает. Возвращает длину до изменения.
        /// </summary>
        public static int Ensure(string fieldName, int index, int copyFrom)
        {
            var field = IL2CPP.GetIl2CppField(Zdb, fieldName);
            var arr = ArrayOf(field);
            int before = Marshal.ReadInt32(arr + 0x18);
            if (before <= index)
            {
                var elem = IL2CPP.il2cpp_class_get_element_class(IL2CPP.il2cpp_object_get_class(arr));
                var bigger = IL2CPP.il2cpp_array_new(elem, (ulong)(index + 1));
                for (int i = 0; i < before; i++) Marshal.WriteIntPtr(bigger + 0x20 + i * IntPtr.Size, Marshal.ReadIntPtr(arr + 0x20 + i * IntPtr.Size));
                IL2CPP.il2cpp_field_static_set_value(field, (void*)bigger);
                arr = bigger;
            }
            Marshal.WriteIntPtr(arr + 0x20 + index * IntPtr.Size, Marshal.ReadIntPtr(arr + 0x20 + copyFrom * IntPtr.Size));
            return before;
        }
    }

    /// <summary>Сырые операции над таблицами игры: ячейки с откатом, тексты эффектов, пулы выпадения. Каждая записывает откат в undo.</summary>
    public static class Commands
    {
        public static readonly string[] Grades = { "C", "B", "A", "P", "S" };
        private static string Pad(int n) => n.ToString("00");

        public static Il2CppSystem.Collections.Generic.List<int> Pool(string field)
        {
            var p = typeof(Z_Database).GetProperty(field, BindingFlags.Public | BindingFlags.Static) ?? throw new Exception($"нет списка {field}");
            return (Il2CppSystem.Collections.Generic.List<int>)p.GetValue(null) ?? throw new Exception($"список {field} ещё не создан");
        }

        // ------------------------------------------------------------ ячейки

        public static void SetCells(Table t, int id, Dictionary<string, object> changes, Action<Action> undo)
        {
            var idCol = t.IdColumn;
            int i = t.IndexOf(id, idCol);
            if (i < 0) throw new Exception($"{t.Name}: нет строки с ID {id}");
            var old = new List<(string col, Il2CppSystem.Object raw)>();
            foreach (var kv in changes) { old.Add((kv.Key, t.GetRaw(i, kv.Key))); t.Set(i, kv.Key, kv.Value); }
            undo(() =>
            {
                int k = t.IndexOf(id, idCol);
                if (k >= 0) foreach (var o in old) t.SetRaw(k, o.col, o.raw);
            });
        }

        /// <summary>Эффекты и строки описания -> изменения ячеек.</summary>
        public static Dictionary<string, object> TextChanges(Table t, List<Effect> effects, List<string> lines, int maxEffects, int maxLines, string countCol = null)
        {
            var cols = new HashSet<string>(t.Columns());
            var o = new Dictionary<string, object>();
            if (effects != null)
            {
                if (effects.Count > maxEffects) throw new Exception($"{t.Name}: эффектов не больше {maxEffects}");
                for (int n = 1; n <= maxEffects; n++)
                {
                    bool has = n <= effects.Count;
                    if (cols.Contains($"효과ID_{Pad(n)}")) o[$"효과ID_{Pad(n)}"] = has ? effects[n - 1].Id : 1;
                    if (cols.Contains($"효과ID_{Pad(n)}값")) o[$"효과ID_{Pad(n)}값"] = has ? effects[n - 1].Value : 0;
                }
            }
            if (lines != null)
            {
                if (lines.Count > maxLines) throw new Exception($"{t.Name}: строк описания не больше {maxLines}");
                for (int n = 1; n <= maxLines; n++)
                    if (cols.Contains($"효과 설명줄{Pad(n)}")) o[$"효과 설명줄{Pad(n)}"] = n <= lines.Count ? lines[n - 1] : "`";
                if (countCol != null && cols.Contains(countCol)) o[countCol] = lines.Count;
            }
            return o;
        }

        public static List<string> DefaultLines(List<Effect> effects) => effects.Select(e => $"Эффект {e.Id}: {e.Value}").ToList();

        // ------------------------------------------------------------ пулы выпадения артефактов

        private static readonly string[] Pools = Grades.Select(g => "List_ArtifactPossibleGrade_" + g).Concat(new[] { "List_ArtifactDrop" }).ToArray();
        private static string PoolName(string p) => p.StartsWith("List_ArtifactPossibleGrade_") ? p.Substring(p.Length - 1) : "Drop";

        public static List<(string pool, int n)> TakeFromPools(int id)
        {
            var res = new List<(string, int)>();
            foreach (var p in Pools)
            {
                Il2CppSystem.Collections.Generic.List<int> l;
                try { l = Pool(p); } catch { continue; }
                int n = 0;
                while (l.Remove(id)) n++;
                if (n > 0) res.Add((PoolName(p), n));
            }
            return res;
        }

        public static void PutToPools(int id, List<(string pool, int n)> parts)
        {
            foreach (var (name, n) in parts)
            {
                var l = Pool(name == "Drop" ? "List_ArtifactDrop" : "List_ArtifactPossibleGrade_" + name);
                for (int i = 0; i < n; i++) l.Add(id);
            }
        }
    }
}
