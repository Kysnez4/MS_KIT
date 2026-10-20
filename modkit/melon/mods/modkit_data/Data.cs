using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Il2Cpp;
using Il2CppSystem.Collections.Generic;
using List = Il2CppSystem.Collections.Generic.List<Il2CppSystem.Collections.Generic.Dictionary<string, Il2CppSystem.Object>>;
using Row = Il2CppSystem.Collections.Generic.Dictionary<string, Il2CppSystem.Object>;

namespace ModKit
{
    /// <summary>
    /// Чтение игровых таблиц Z_Database.Dictionary_* (List&lt;Dictionary&lt;string, object&gt;&gt;). Пока только чтение:
    /// запись, клонирование строк и артефакты переносятся из data.ts позже.
    /// </summary>
    public class Table
    {
        public readonly string Name;
        private List _list;

        public Table(string name) { Name = name; }

        private List Rows
        {
            get
            {
                if (_list != null) return _list;
                var p = typeof(Z_Database).GetProperty("Dictionary_" + Name, BindingFlags.Public | BindingFlags.Static)
                        ?? throw new Exception($"нет таблицы {Name}");
                _list = (List)p.GetValue(null) ?? throw new Exception($"таблица {Name} ещё не загружена");
                return _list;
            }
        }

        public int Length => Rows.Count;

        /// <summary>Указатель на текущий список таблицы в игре (меняется, если игра перезагрузила таблицу).</summary>
        public IntPtr ListPointer()
        {
            var p = typeof(Z_Database).GetProperty("Dictionary_" + Name, BindingFlags.Public | BindingFlags.Static);
            return ((Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase)p.GetValue(null))?.Pointer ?? IntPtr.Zero;
        }

        /// <summary>Значение ячейки: double (любое число), bool, string или null.</summary>
        public object Get(int i, string column)
        {
            var o = Rows[i][column];
            return Unbox(o);
        }

        public double Num(int i, string column) => Get(i, column) is double d ? d : 0;

        /// <summary>Распаковка боксированного значения по имени типа: Int32/Single/... → double, Boolean, String.</summary>
        public static object Unbox(Il2CppSystem.Object o)
        {
            if (o == null) return null;
            // ToString() у боксированных чисел через обёртку возвращает мусор — читаем значение распаковкой
            switch (o.GetIl2CppType().Name)
            {
                case "Boolean": return o.Unbox<byte>() != 0;
                case "String": return o.ToString();
                case "Int32": return (double)o.Unbox<int>();
                case "UInt32": return (double)o.Unbox<uint>();
                case "Int64": return (double)o.Unbox<long>();
                case "Int16": return (double)o.Unbox<short>();
                case "Byte": return (double)o.Unbox<byte>();
                case "Single": return double.Parse(o.Unbox<float>().ToString("R", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);   // без хвоста float -> double
                case "Double": return o.Unbox<double>();
                default: return $"<{o.GetIl2CppType().Name}>";
            }
        }

        /// <summary>Имена колонок (по ключам первой строки).</summary>
        private System.Collections.Generic.List<string> _cols;

        public System.Collections.Generic.List<string> Columns()
        {
            if (_cols != null) return _cols;
            var cols = new System.Collections.Generic.List<string>();
            foreach (var kv in Rows[0]) cols.Add(kv.Key);
            return _cols = cols;
        }

        /// <summary>Первая колонка таблицы — идентификатор (ID / ID001 / ...).</summary>
        public bool HasColumn(string name) => Columns().Contains(name);

        public string IdColumn => Columns()[0];

        private static unsafe Il2CppSystem.Object BoxPrim(string typeName, void* value)
        {
            var klass = Il2CppInterop.Runtime.IL2CPP.GetIl2CppClass("mscorlib.dll", "System", typeName);
            return new Il2CppSystem.Object(Il2CppInterop.Runtime.IL2CPP.il2cpp_value_box(klass, (IntPtr)value));
        }

        /// <summary>Упаковывает значение; числовой тип повторяет тип прежнего значения (иначе игра споткнётся о приведение).</summary>
        private static unsafe Il2CppSystem.Object Box(object value, Il2CppSystem.Object like)
        {
            if (value == null) return null;
            if (value is string s) return new Il2CppSystem.Object(Il2CppInterop.Runtime.IL2CPP.ManagedStringToIl2Cpp(s));
            if (value is bool b) { byte v = (byte)(b ? 1 : 0); return BoxPrim("Boolean", &v); }
            double d = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            switch (like?.GetIl2CppType().Name)
            {
                case "Int32": { int v = (int)Math.Round(d); return BoxPrim("Int32", &v); }
                case "Int64": { long v = (long)Math.Round(d); return BoxPrim("Int64", &v); }
                case "String": return new Il2CppSystem.Object(Il2CppInterop.Runtime.IL2CPP.ManagedStringToIl2Cpp(d.ToString(CultureInfo.InvariantCulture)));
                case "Double": return BoxPrim("Double", &d);
                default: { float v = (float)d; return BoxPrim("Single", &v); }
            }
        }

        /// <summary>Боксированное значение ячейки как есть (для отката через <see cref="SetRaw"/>).</summary>
        public Il2CppSystem.Object GetRaw(int i, string column) => Rows[i][column];

        public void SetRaw(int i, string column, Il2CppSystem.Object raw) => Rows[i][column] = raw;

        /// <summary>Меняет значение существующей колонки. Возвращает прежнее значение (для отката).</summary>
        public object Set(int i, string column, object value)
        {
            var row = Rows[i];
            var old = row[column];
            row[column] = Box(value, old);
            return Unbox(old);
        }

        /// <summary>Копирует строку в конец таблицы. Возвращает индекс новой строки.</summary>
        public int CloneRow(int from)
        {
            var src = Rows[from];
            var copy = new Row();
            foreach (var kv in src) copy[kv.Key] = kv.Value;
            Rows.Add(copy);
            return Length - 1;
        }

        public void RemoveLast() => Rows.RemoveAt(Length - 1);

        public double MaxId()
        {
            var col = IdColumn;
            double m = 0;
            for (int i = 0; i < Length; i++) if (Get(i, col) is double d) m = Math.Max(m, d);   // бывают строки-заголовки с текстовым ID
            return m;
        }

        public int IndexOf(double id, string idColumn = "ID")
        {
            for (int i = 0; i < Length; i++) if (Get(i, idColumn) is double d && d == id) return i;
            return -1;
        }
    }
}
