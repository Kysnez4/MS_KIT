using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using ModKit;

namespace ModKitData
{
    /// <summary>Заклинание из таблицы Ability (ID 1..21): прочитанные поля.</summary>
    public class SpellInfo
    {
        public int Id, MaxLevel;
        public string Name, Description;
        /// <summary>Слоты эффектов 2..9 (слот 1 — метка заклинания «-ID»). Слот N, по-видимому, соответствует уровню N (4 и 7 пустые — уровни особенностей); пустые слоты не включены.</summary>
        public SortedDictionary<int, Effect> Slots = new SortedDictionary<int, Effect>();
    }

    public class SpellChanges
    {
        public string Name;
        /// <summary>Первая строка описания (краткое «Усиливает базовые снаряды.»).</summary>
        public string Description;
        public int? MaxLevel;
        /// <summary>Замена эффекта в слоте (2..9): слот N, по наблюдению за пулей, соответствует уровню N (4 и 7 пустые — уровни особенностей; не проверено в бою). Эффект (1,0) очищает слот.</summary>
        public Dictionary<int, Effect> Slots = new Dictionary<int, Effect>();
        /// <summary>Умножить значения всех слотов с данным ID эффекта: [ID эффекта] = множитель.</summary>
        public Dictionary<int, double> Scale = new Dictionary<int, double>();
        /// <summary>Добавить к значениям слотов с данным ID эффекта: [ID эффекта] = слагаемое.</summary>
        public Dictionary<int, double> Add = new Dictionary<int, double>();
    }

    internal static class Spells
    {
        private const int MaxSpellId = 21;
        private static string C(int n, string suffix = "") => $"효과ID_{n:00}{suffix}";

        public static SpellInfo Get(int id)
        {
            var t = new Table("Ability");
            int i = t.IndexOf(id, t.IdColumn);
            if (i < 0 || id < 1 || id > MaxSpellId) throw new Exception($"Ability {id}: это не заклинание (допустимы ID 1..{MaxSpellId})");
            var s = new SpellInfo { Id = id, Name = t.Get(i, "이름") as string, Description = t.Get(i, "효과 설명줄01") as string, MaxLevel = (int)t.Num(i, "최대레벨") };
            for (int n = 2; n <= 9; n++)
            {
                int eid = (int)t.Num(i, C(n));
                if (eid == 0 || eid == 1) continue;
                s.Slots[n] = new Effect(eid, t.Num(i, C(n, "값")));
            }
            return s;
        }

        public static void Edit(int id, SpellChanges ch, Action<Action> undo, string owner)
        {
            var cur = Get(id);                         // заодно проверяет ID
            var t = new Table("Ability");
            var o = new Dictionary<string, object>();
            if (ch.Name != null) o["이름"] = ch.Name;
            if (ch.Description != null) o["효과 설명줄01"] = ch.Description;
            if (ch.MaxLevel != null) o["최대레벨"] = ch.MaxLevel.Value;
            var slots = new Dictionary<int, Effect>(cur.Slots);
            foreach (var kv in ch.Slots)
            {
                if (kv.Key < 2 || kv.Key > 9) throw new Exception($"слот {kv.Key}: допустимы 2..9");
                if (kv.Value.Id == 1 || kv.Value.Id == 0) slots.Remove(kv.Key); else slots[kv.Key] = kv.Value;
            }
            foreach (var n in slots.Keys.ToList())
            {
                var e = slots[n];
                if (ch.Scale.TryGetValue(e.Id, out var m)) e.Value *= m;
                if (ch.Add.TryGetValue(e.Id, out var a)) e.Value += a;
                slots[n] = e;
            }
            for (int n = 2; n <= 9; n++)
            {
                bool had = cur.Slots.ContainsKey(n), has = slots.ContainsKey(n);
                if (!had && !has) continue;
                o[C(n)] = has ? slots[n].Id : 1;
                o[C(n, "값")] = has ? slots[n].Value : 0;
            }
            int slotCells = o.Count;
            foreach (var kv in RewriteText(t, id, cur, slots, owner)) o[kv.Key] = kv.Value;
            Commands.SetCells(t, id, o, undo);
            Log.Write($"[{owner}] заклинание {id} «{cur.Name}»: изменено {o.Count} ячеек (слоты и описание: {slotCells}, текст уровней: {o.Count - slotCells})");
        }

        // ---------------------------------------------------------------- текст уровней (E1, modkit_data 1.4)

        private static readonly Regex NumToken = new Regex(@"(?<![\d.,])\d+(?:[.,]\d+)?(?![\d])");
        private const string TextLevels = "효과 설명줄06", TextSpecial = "효과 설명줄05";

        private static double ParseNum(string s) => double.Parse(s.Replace(',', '.'), CultureInfo.InvariantCulture);

        private static string FormatNum(double v)
        {
            var s = (Math.Round(v * 100) / 100).ToString(CultureInfo.InvariantCulture);
            return s.Replace('.', ',');                // в русском тексте игры дробные числа пишутся с запятой («+0,3%»)
        }

        /// <summary>В строке text заменяет по одному числу, равному oldV, на newV (для каждого изменения своё число). Возвращает число замен.</summary>
        private static string ReplaceNumbers(string text, List<(double oldV, double newV)> changes, out int replaced)
        {
            replaced = 0;
            var used = new HashSet<int>();
            var edits = new List<(int pos, int len, string val)>();
            foreach (var (oldV, newV) in changes)
            {
                foreach (Match m in NumToken.Matches(text))
                {
                    if (used.Contains(m.Index) || Math.Abs(ParseNum(m.Value) - oldV) > 1e-6) continue;
                    used.Add(m.Index);
                    edits.Add((m.Index, m.Length, FormatNum(newV)));
                    replaced++;
                    break;
                }
            }
            foreach (var e in edits.OrderByDescending(e => e.pos)) text = text.Substring(0, e.pos) + e.val + text.Substring(e.pos + e.len);
            return text;
        }

        /// <summary>
        /// Обновляет числа в тексте уровней. Строка «효과 설명줄06» — по 《…》 на уровень, через «^»: если все элементы одинаковы (дух, спутник, молния…),
        /// каждый описывает слоты 2..7 сразу; иначе элемент k — слот k+2. Строка «효과 설명줄05» — особенности: сегменты через « @ » идут в порядке непустых слотов 8 и 9.
        /// Обновляются только слоты, у которых тот же ID эффекта, но другое значение; если число не найдено или эффект заменён целиком, текст не трогается (запись в журнал).
        /// </summary>
        private static Dictionary<string, object> RewriteText(Table t, int id, SpellInfo cur, Dictionary<int, Effect> slots, string owner)
        {
            var res = new Dictionary<string, object>();
            try
            {
                int row = t.IndexOf(id, t.IdColumn);
                var changed = new SortedDictionary<int, (double oldV, double newV)>();
                var skipped = new List<int>();
                foreach (var kv in slots)
                {
                    if (!cur.Slots.TryGetValue(kv.Key, out var was)) { skipped.Add(kv.Key); continue; }
                    if (was.Id != kv.Value.Id) { skipped.Add(kv.Key); continue; }
                    if (Math.Abs(was.Value - kv.Value.Value) > 1e-9) changed[kv.Key] = (was.Value, kv.Value.Value);
                }
                foreach (var n in cur.Slots.Keys) if (!slots.ContainsKey(n)) skipped.Add(n);
                if (skipped.Count > 0) Log.Write($"[{owner}] заклинание {id}: текст слотов {string.Join(",", skipped)} не обновлён (эффект заменён или убран)");
                if (changed.Count == 0) return res;

                int total = 0, missed = 0;
                if (t.Get(row, TextLevels) is string levels && levels.Contains("^"))
                {
                    var items = levels.Split('^');
                    bool merged = items.Length > 1 && items.All(x => x == items[0]);
                    for (int k = 0; k < items.Length; k++)
                    {
                        var list = new List<(double, double)>();
                        foreach (var kv in changed)
                            if (merged ? kv.Key <= 7 : kv.Key == k + 2) list.Add(kv.Value);
                        if (list.Count == 0) continue;
                        items[k] = ReplaceNumbers(items[k], list, out int n);
                        total += n; missed += list.Count - n;
                    }
                    var text = string.Join("^", items);
                    if (text != levels) res[TextLevels] = text;
                }
                if (t.Get(row, TextSpecial) is string special && special != "`" && special.Length > 0)
                {
                    var segs = special.Split(new[] { " @ " }, StringSplitOptions.None);
                    var nonEmpty = new[] { 8, 9 }.Where(n => cur.Slots.ContainsKey(n)).ToList();
                    if (segs.Length == nonEmpty.Count)
                    {
                        for (int k = 0; k < segs.Length; k++)
                        {
                            if (!changed.TryGetValue(nonEmpty[k], out var ch)) continue;
                            segs[k] = ReplaceNumbers(segs[k], new List<(double, double)> { ch }, out int n);
                            total += n; missed += 1 - n;
                        }
                        var text = string.Join(" @ ", segs);
                        if (text != special) res[TextSpecial] = text;
                    }
                    else Log.Write($"[{owner}] заклинание {id}: строка особенностей не разобрана ({segs.Length} частей, слотов {nonEmpty.Count})");
                }
                Log.Write($"[{owner}] заклинание {id}: текст уровней — заменено чисел {total}" + (missed > 0 ? $", не найдено {missed}" : ""));
            }
            catch (Exception e) { Log.Write($"[{owner}] заклинание {id}: текст уровней не обновлён ({e.Message})"); }
            return res;
        }
    }
}
