using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ModKit;
using ModKitData;

namespace MagicBalance
{
    /// <summary>
    /// Применяет файл balance.json сайта «Баланс магии» (modkit/tools/balance, кнопка «Скачать JSON»), встроенный в DLL.
    /// Пишется только то, что отличается от текущих таблиц игры:
    ///   spells[id].lv — эффекты уровней → слоты Ability 2..9 (сопоставление уровней и слотов как в gen_data.py);
    ///   spells[id].attrs — эффекты атрибутов (строки Ability с ID из поля game);
    ///   combos[id] — эффекты, имя и требования комбинаций (MagicCom); newCombos — новые комбинации.
    ///   artifacts[id] / synergies[id] — полный список эффектов строки Ability / Synergy и (необязательно) lines/colors описания.
    /// Тексты (lines, desc, name) — строкой/массивом или по языкам: {"ru": …, "en": …} (язык игры → en → ru).
    /// У атрибута и комбинации необязательные lines (строки описания 효과 설명줄01..06, до 6) и colors (цвет строк «#RRGGBB»):
    /// если они есть, описание переписывается, чтобы числа совпадали с новыми эффектами.
    /// Поля hero и pick — только калькулятор сайта, в игру не идут.
    /// Все правки откатываются при выключении мода.
    /// </summary>
    public class BalanceMod : IKitMod
    {
        private const string Res = "balance.json";
        private static string C(int n, string suffix = "") => $"효과ID_{n:00}{suffix}";

        public ModDef Define() => new ModDef
        {
            Id = "magic_balance", Name = "Баланс магии", Version = "2.1",
            Description = "Правки заклинаний, их атрибутов и комбинаций из файла balance.json сайта modkit/tools/balance. Механики артефактов: ДНК без снижения, взрыв боссов Девой/Графиней, проклятие Экскалибура, руна неуязвимости Гильгамеша.",
            Depends = { ["modkit_data"] = ">=1.5", ["modkit_spells_data"] = ">=1.0", ["modkit_combos"] = ">=1.0", ["modkit_effects"] = ">=1.0" },
            // при каждом входе в главное меню правки проверяются заново: если игра перечитала таблицы, отличия снова записываются (иначе ничего не меняется)
            OnMainMenu = (ctx, menu) => ctx.Guard("повторное применение баланса", () => Apply(ctx, ctx.Use<IData>("modkit_data"))),
            OnEnable = ctx =>
            {
                var data = ctx.Use<IData>("modkit_data");
                Mechanics.Enable(ctx);
                Done.Clear();
                ctx.OnCleanup(Done.Clear);
                data.WhenReady(() => ctx.Guard("применение баланса", () => Apply(ctx, data)));
                // диагностика энциклопедии: что лежит в строке MagicCom (игра берёт её по индексу списка = ID) в момент открытия карточки
                ctx.Before("V_MagicComShow", "Show_MagicComDetail", 1, (self, args) => ctx.Guard("диагностика энциклопедии", () =>
                {
                    int i = Convert.ToInt32(args[0]);
                    var t = data.Table("MagicCom");
                    if (i < 0 || i >= t.Length) { ctx.Log($"энциклопедия: комбинация {i} — нет строки (всего {t.Length})"); return; }
                    var lines = Enumerable.Range(1, 6).Select(n => t.Get(i, $"효과 설명줄{n:00}") as string).Where(s => s != null && s != "`");
                    var fx = Enumerable.Range(1, 9).Where(n => t.Num(i, C(n)) > 1).Select(n => $"{(int)t.Num(i, C(n))}={t.Num(i, C(n, "값"))}");
                    ctx.Log($"энциклопедия: комбинация {i} (ID строки {t.Get(i, t.IdColumn)}) «{t.Get(i, "이름")}»: эффекты {string.Join(", ", fx)}; текст: {string.Join(" / ", lines)}");
                }));
            },
        };

        private static JsonElement Load()
        {
            using var s = typeof(BalanceMod).Assembly.GetManifestResourceStream(Res) ?? throw new Exception($"нет встроенного {Res}");
            using var r = new StreamReader(s);
            return JsonDocument.Parse(r.ReadToEnd()).RootElement;
        }

        private static List<Effect> Effects(JsonElement a) =>
            a.ValueKind != JsonValueKind.Array ? new List<Effect>() : a.EnumerateArray().Select(e => new Effect(e[0].GetInt32(), e[1].GetDouble())).ToList();

        private static string Fx(IEnumerable<Effect> l) => l.Any() ? string.Join(", ", l.Select(e => $"{e.Id}={e.Value}")) : "—";

        /// <summary>Колонки описания из lines/colors: строки 01..06 (лишние — «`»), цвета только у заданных строк. Пусто — описания нет.</summary>
        private static Dictionary<string, object> Text(JsonElement o, out List<string> lines)
        {
            var d = new Dictionary<string, object>();
            lines = Has(o, "lines", out var l) ? l.EnumerateArray().Select(x => x.GetString() ?? "`").ToList() : null;
            if (lines == null) return d;
            if (lines.Count > 6) throw new Exception("строк описания больше 6");
            for (int n = 1; n <= 6; n++) d[$"효과 설명줄{n:00}"] = n <= lines.Count ? lines[n - 1] : "`";
            if (Has(o, "colors", out var c))
            {
                int n = 0;
                foreach (var x in c.EnumerateArray()) if (++n <= 6 && x.GetString() is string h && h != "") d[$"설명줄{n:00} 색깔"] = $"<color={h}>";
            }
            return d;
        }

        private static bool Has(JsonElement o, string k, out JsonElement v)
        {
            v = default;
            if (!(o.ValueKind == JsonValueKind.Object && o.TryGetProperty(k, out v) && v.ValueKind != JsonValueKind.Null)) return false;
            // тексты по языкам: «lines»/«desc»/«name» могут быть объектом {"ru": …, "en": …} — берём язык игры, затем en, ru
            if ((k == "lines" || k == "desc" || k == "name") && v.ValueKind == JsonValueKind.Object)
            {
                foreach (var code in new[] { _lang, "en", "ru" })
                    if (code != null && v.TryGetProperty(code, out var t) && t.ValueKind != JsonValueKind.Null) { v = t; return true; }
                return false;
            }
            return true;
        }

        private static string _lang;     // язык игры (ctx.Lang) для текстов balance.json

        /// <summary>ctx.Lang есть в ядре с 2026-10-25; на старом ядре — MissingMethodException при вызове, тогда русский.</summary>
        private static string LangOf(ModContext ctx) { try { return LangCore(ctx); } catch { return "ru"; } }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static string LangCore(ModContext ctx) => ctx.Lang;

        /// <summary>Заклинание → слоты после применения scale/add: повторно множить нельзя, пока таблица не сброшена к исходной.</summary>
        private static readonly Dictionary<int, string> Done = new Dictionary<int, string>();
        private static string Snap(SpellInfo s) => string.Join(";", s.Slots.Select(kv => $"{kv.Key}:{kv.Value.Id}={kv.Value.Value:R}"));

        private static void Apply(ModContext ctx, IData data)
        {
            _lang = LangOf(ctx);
            if (data.Table("Ability").Length == 0 || data.Table("MagicCom").Length == 0) return;   // таблицы ещё не загружены
            var root = Load();
            int sp = 0, at = 0, cb = 0, nc = 0;
            if (Has(root, "spells", out var spells))
            {
                var lib = ctx.Use<ISpellsData>("modkit_spells_data");
                foreach (var p in spells.EnumerateObject())
                {
                    int id = int.Parse(p.Name);
                    if (EditSpell(ctx, lib, id, p.Value)) sp++;
                    if (Has(p.Value, "attrs", out var attrs))
                        foreach (var lv in attrs.EnumerateObject())
                            foreach (var a in lv.Value.EnumerateArray())
                                if (EditAttr(ctx, data, id, a)) at++;
                }
            }
            if (Has(root, "combos", out var combos) || Has(root, "newCombos", out _))
            {
                var lib = ctx.Use<ICombos>("modkit_combos");
                if (Has(root, "combos", out combos))
                    foreach (var p in combos.EnumerateObject())
                        if (EditCombo(ctx, data, lib, int.Parse(p.Name), p.Value)) cb++;
                if (Has(root, "newCombos", out var added))
                    foreach (var c in added.EnumerateArray()) if (AddCombo(ctx, lib, c)) nc++;
            }
            int ar = 0, sy = 0;
            if (Has(root, "artifacts", out var arts))
                foreach (var p in arts.EnumerateObject()) if (EditRow(ctx, data, "Ability", 9, int.Parse(p.Name), p.Value)) ar++;
            if (Has(root, "synergies", out var syns))
                foreach (var p in syns.EnumerateObject()) if (EditRow(ctx, data, "Synergy", 3, int.Parse(p.Name), p.Value)) sy++;
            if (sp + at + cb + nc + ar + sy > 0) ctx.Log($"баланс применён: заклинаний {sp}, атрибутов {at}, комбинаций изменено {cb}, добавлено {nc}, артефактов {ar}, синергий {sy}");
        }

        // ------------------------------------------------------------ заклинания

        /// <summary>Уровень → слоты Ability (как в gen_data.py): если слоты 4-6 пусты, слоты 2-3 — общий шаблон уровней 2..max-1, иначе слот N = уровень N; слоты 8, 9 — максимальный уровень.</summary>
        private static Dictionary<int, List<int>> SlotMap(SpellInfo s)
        {
            var m = new Dictionary<int, List<int>>();
            bool tmpl = !new[] { 4, 5, 6 }.Any(s.Slots.ContainsKey);
            for (int n = 2; n < s.MaxLevel; n++)
            {
                var ns = tmpl ? new[] { 2, 3 }.Where(s.Slots.ContainsKey).ToList() : (s.Slots.ContainsKey(n) ? new List<int> { n } : new List<int>());
                if (ns.Count > 0) m[n] = ns;
            }
            var top = new[] { 8, 9 }.Where(s.Slots.ContainsKey).ToList();
            if (top.Count > 0) m[s.MaxLevel] = top;
            return m;
        }

        private static bool EditSpell(ModContext ctx, ISpellsData lib, int id, JsonElement o)
        {
            var cur = lib.Get(id);
            var ch = new SpellChanges();
            bool any = false;
            if (Has(o, "name", out var nm) && nm.GetString() is string name && name != "" && name != cur.Name) { ch.Name = name; any = true; }
            if (Has(o, "desc", out var ds) && ds.GetString() is string desc && desc != "" && desc != cur.Description) { ch.Description = desc; any = true; }
            if (Has(o, "maxLevel", out var ml) && ml.GetInt32() != cur.MaxLevel) { ch.MaxLevel = ml.GetInt32(); any = true; }
            bool scaled = Done.TryGetValue(id, out var snap) && snap == Snap(cur);
            foreach (var key in new[] { "scale", "add" })
                if (!scaled && Has(o, key, out var arr))
                    foreach (var e in Effects(arr)) { (key == "scale" ? ch.Scale : ch.Add)[e.Id] = e.Value; any = true; }

            if (Has(o, "lv", out var lvs))
            {
                var map = SlotMap(cur);
                var want = new Dictionary<int, Effect>();
                var used = new HashSet<int>();
                foreach (var p in lvs.EnumerateObject())
                {
                    int n = int.Parse(p.Name);
                    if (n < 2) continue;
                    var list = Effects(p.Value);
                    if (!map.TryGetValue(n, out var ns)) { if (list.Count > 0) ctx.Warn($"заклинание {id} ур.{n}: нет слота для {Fx(list)}"); continue; }
                    for (int i = 0; i < ns.Count; i++)
                    {
                        if (i >= list.Count) continue;           // слот уровня без эффекта не трогаем
                        var e = list[i];
                        int slot = ns[i];
                        used.Add(slot);
                        if (want.TryGetValue(slot, out var was) && (was.Id != e.Id || Math.Abs(was.Value - e.Value) > 1e-9))
                        {
                            // общий слот шаблона: побеждает значение, отличное от игры
                            ctx.Warn($"заклинание {id}: слот {slot} общий для нескольких уровней, разные значения {was.Id}={was.Value} и {e.Id}={e.Value} (ур.{n})");
                            if (Same(cur, slot, e)) continue;
                        }
                        want[slot] = e;
                    }
                    for (int i = ns.Count; i < list.Count; i++) ctx.Warn($"заклинание {id} ур.{n}: нет слота для {list[i].Id}={list[i].Value}");
                }
                foreach (var kv in want)
                    if (!Same(cur, kv.Key, kv.Value)) { ch.Slots[kv.Key] = kv.Value; any = true; }
            }
            if (!any) return false;
            lib.Edit(id, ch);
            Done[id] = Snap(lib.Get(id));
            ctx.Log($"заклинание {id} «{cur.Name}»: " + string.Join("; ", new[]
            {
                ch.Name != null ? $"имя «{ch.Name}»" : null,
                ch.MaxLevel != null ? $"макс. уровень {ch.MaxLevel}" : null,
                ch.Slots.Count > 0 ? "слоты " + string.Join(", ", ch.Slots.Select(kv => $"{kv.Key}: {(cur.Slots.TryGetValue(kv.Key, out var w) ? $"{w.Id}={w.Value}" : "—")} → {kv.Value.Id}={kv.Value.Value}")) : null,
                ch.Scale.Count > 0 ? "×: " + string.Join(", ", ch.Scale.Select(kv => $"{kv.Key}×{kv.Value}")) : null,
                ch.Add.Count > 0 ? "+: " + string.Join(", ", ch.Add.Select(kv => $"{kv.Key}+{kv.Value}")) : null,
            }.Where(x => x != null)));
            return true;
        }

        private static bool Same(SpellInfo s, int slot, Effect e) =>
            s.Slots.TryGetValue(slot, out var c) && c.Id == e.Id && Math.Abs(c.Value - e.Value) < 1e-9;

        // ------------------------------------------------------------ атрибуты (строки Ability с 파생기술ID = заклинание)

        private static bool EditAttr(ModContext ctx, IData data, int spell, JsonElement a)
        {
            if (!Has(a, "game", out var g)) return false;            // атрибут без строки игры (заготовка сайта)
            int gid = g.GetInt32();
            var want = Has(a, "effects", out var fx) ? Effects(fx) : new List<Effect>();
            var t = data.Table("Ability");
            int row = t.IndexOf(gid, t.IdColumn);
            if (row < 0) { ctx.Warn($"атрибут {gid}: нет строки в Ability"); return false; }
            if ((int)t.Num(row, "파생기술ID") != spell) { ctx.Warn($"атрибут {gid} «{t.Get(row, "이름")}»: относится к заклинанию {(int)t.Num(row, "파생기술ID")}, а не {spell} — пропущен"); return false; }

            var cur = new SortedDictionary<int, Effect>();
            for (int n = 2; n <= 9; n++)
            {
                int eid = (int)t.Num(row, C(n));
                if (eid > 1) cur[n] = new Effect(eid, t.Num(row, C(n, "값")));
            }
            string Key(IEnumerable<Effect> l) => string.Join(";", l.Select(e => $"{e.Id}={e.Value:R}").OrderBy(x => x));
            // описание: пишутся только отличающиеся от игры ячейки (счётчик 효과 설명줄 개수 у атрибутов всегда 1 — не трогаем)
            var o = new Dictionary<string, object>();
            var text = Text(a, out var lines);
            foreach (var kv in text)
                if (!Equals(t.Get(row, kv.Key) as string, kv.Value as string)) o[kv.Key] = kv.Value;
            bool fxSame = Key(cur.Values) == Key(want);
            if (fxSame && o.Count == 0) return false;
            if (want.Count > 8) { ctx.Warn($"атрибут {gid}: эффектов больше 8"); return false; }

            // эффект с тем же ID остаётся в своём слоте, новые — в освободившиеся или пустые слоты
            var res = new Dictionary<int, Effect>();
            var rest = new List<Effect>();
            foreach (var e in want)
            {
                int slot = cur.Keys.FirstOrDefault(n => !res.ContainsKey(n) && cur[n].Id == e.Id);
                if (slot != 0) res[slot] = e; else rest.Add(e);
            }
            var free = cur.Keys.Where(n => !res.ContainsKey(n)).Concat(Enumerable.Range(2, 8).Where(n => !cur.ContainsKey(n))).ToList();
            for (int i = 0; i < rest.Count; i++) res[free[i]] = rest[i];

            for (int n = 2; n <= 9 && !fxSame; n++)
            {
                bool had = cur.ContainsKey(n), has = res.ContainsKey(n);
                if (!had && !has) continue;
                o[C(n)] = has ? res[n].Id : 1;
                o[C(n, "값")] = has ? res[n].Value : 0;
            }
            data.Edit("Ability", gid, o);
            ctx.Log($"атрибут {gid} «{t.Get(row, "이름")}» (заклинание {spell}): {(fxSame ? "эффекты прежние" : $"{Fx(cur.Values)} → {Fx(want)}")}" + (lines != null ? ", описание: " + string.Join(" / ", lines) : ""));
            return true;
        }

        // ------------------------------------------------------------ комбинации

        private static bool EditCombo(ModContext ctx, IData data, ICombos lib, int id, JsonElement o)
        {
            var t = data.Table("MagicCom");
            int row = t.IndexOf(id, t.IdColumn);
            if (row < 0) { ctx.Warn($"комбинация {id}: нет строки в MagicCom"); return false; }
            var cols = new HashSet<string>(t.Columns());
            var cur = new List<Effect>();
            for (int n = 1; n <= 9; n++)
            {
                if (!cols.Contains(C(n))) continue;
                int eid = (int)t.Num(row, C(n));
                if (eid > 1) cur.Add(new Effect(eid, t.Num(row, C(n, "값"))));
            }
            var ch = new ComboChanges();
            bool any = false;
            if (Has(o, "effects", out var fx))
            {
                var want = Effects(fx);
                if (Fx(want) != Fx(cur)) { ch.Effects = want; any = true; }
            }
            if (Has(o, "name", out var nm) && nm.GetString() is string name && name != (t.Get(row, "이름") as string)) { ch.Name = name; any = true; }
            if (Has(o, "req", out var rq)) { ch.Requires = rq.EnumerateArray().Select(x => x.GetInt32()).ToArray(); any = true; }
            // описание: строки — через ComboChanges.Lines (лишние до 6 — «`»), цвета — отдельной правкой строки MagicCom
            var text = Text(o, out var lines);
            var colors = text.Where(kv => kv.Key.EndsWith("색깔") && cols.Contains(kv.Key) && !Equals(t.Get(row, kv.Key) as string, kv.Value as string)).ToDictionary(kv => kv.Key, kv => kv.Value);
            if (lines != null && Enumerable.Range(1, 6).Any(n => cols.Contains($"효과 설명줄{n:00}") && !Equals(t.Get(row, $"효과 설명줄{n:00}") as string, text[$"효과 설명줄{n:00}"] as string)))
            { ch.Lines = lines; any = true; }
            // своего описания нет, а значения эффектов изменились — меняем старые числа в тексте игры на новые
            if (lines == null && ch.Effects != null && AutoLines(t, row, cols, cur, ch.Effects, out var auto, out int replaced))
            { ch.Lines = auto; ctx.Log($"комбинация {id}: в описании заменено чисел {replaced}"); }
            if (!any && colors.Count == 0) return false;
            if (any) lib.Edit(id, ch);
            if (colors.Count > 0) data.Edit("MagicCom", id, colors);
            ctx.Log($"комбинация {id} «{t.Get(row, "이름")}»: {Fx(cur)} → {(ch.Effects != null ? Fx(ch.Effects) : "эффекты прежние")}" + (ch.Name != null ? $", имя «{ch.Name}»" : "") + (ch.Lines != null ? ", описание: " + string.Join(" / ", ch.Lines) : ""));
            return true;
        }

        // ------------------------------------------------------------ числа в описании комбинации

        private static readonly System.Text.RegularExpressions.Regex NumToken =
            new System.Text.RegularExpressions.Regex(@"(?<![\d.,])\d+(?:[.,]\d+)?(?![\d])");

        /// <summary>
        /// Строки описания комбинации 01..06 с новыми числами: для эффектов с тем же ID и другим значением в тексте ищется
        /// число, равное старому значению (по одному на эффект), и меняется на новое. Дробь пишется так же, как в тексте
        /// (запятая в русском, точка в остальных языках). false — ничего не заменено.
        /// </summary>
        private static bool AutoLines(Table t, int row, HashSet<string> cols, List<Effect> cur, List<Effect> want, out List<string> lines, out int replaced)
        {
            lines = null; replaced = 0;
            var changes = new List<(double oldV, double newV)>();
            foreach (var w in want)
            {
                int k = cur.FindIndex(x => x.Id == w.Id);
                if (k >= 0 && Math.Abs(cur[k].Value - w.Value) > 1e-6) changes.Add((cur[k].Value, w.Value));
            }
            if (changes.Count == 0) return false;
            var src = Enumerable.Range(1, 6).Select(n => cols.Contains($"효과 설명줄{n:00}") ? (t.Get(row, $"효과 설명줄{n:00}") as string ?? "`") : "`").ToList();
            var used = new HashSet<(int line, int pos)>();
            var edits = new List<(int line, int pos, int len, string val)>();
            foreach (var (oldV, newV) in changes)
            {
                bool done = false;
                for (int li = 0; li < src.Count && !done; li++)
                    foreach (System.Text.RegularExpressions.Match m in NumToken.Matches(src[li]))
                    {
                        if (used.Contains((li, m.Index))) continue;
                        if (Math.Abs(double.Parse(m.Value.Replace(',', '.'), System.Globalization.CultureInfo.InvariantCulture) - oldV) > 1e-6) continue;
                        used.Add((li, m.Index));
                        bool ru = src[li].Any(ch => ch >= 'А' && ch <= 'я');
                        var v = (Math.Round(newV * 100) / 100).ToString(System.Globalization.CultureInfo.InvariantCulture);
                        edits.Add((li, m.Index, m.Length, ru ? v.Replace('.', ',') : v));
                        done = true;
                        break;
                    }
            }
            if (edits.Count == 0) return false;
            foreach (var e in edits.OrderByDescending(e => e.pos))
                src[e.line] = src[e.line].Substring(0, e.pos) + e.val + src[e.line].Substring(e.pos + e.len);
            int last = src.FindLastIndex(x => x != "`");
            lines = src.Take(last + 1).ToList();
            replaced = edits.Count;
            return true;
        }

        // ------------------------------------------------------------ артефакты и синергии

        /// <summary>Строка Ability (артефакт) или Synergy: effects — полный список эффектов (слоты 1..slots по порядку, лишние пустые), lines/colors — описание.</summary>
        private static bool EditRow(ModContext ctx, IData data, string table, int slots, int id, JsonElement o)
        {
            var t = data.Table(table);
            int row = t.IndexOf(id, t.IdColumn);
            if (row < 0) { ctx.Warn($"{table} {id}: нет строки"); return false; }
            var cols = new HashSet<string>(t.Columns());
            var cur = new List<Effect>();
            for (int n = 1; n <= slots; n++) { int eid = (int)t.Num(row, C(n)); if (eid > 1) cur.Add(new Effect(eid, t.Num(row, C(n, "값")))); }
            var ch = new Dictionary<string, object>();
            var want = Has(o, "effects", out var fx) ? Effects(fx) : null;
            if (want != null && Fx(want) != Fx(cur))
            {
                if (want.Count > slots) { ctx.Warn($"{table} {id}: эффектов больше {slots}"); return false; }
                for (int n = 1; n <= slots; n++) { ch[C(n)] = n <= want.Count ? want[n - 1].Id : 1; ch[C(n, "값")] = n <= want.Count ? want[n - 1].Value : 0; }
            }
            var text = Text(o, out var lines);
            foreach (var kv in text)
                if (cols.Contains(kv.Key) && !Equals(t.Get(row, kv.Key) as string, kv.Value as string)) ch[kv.Key] = kv.Value;
            if (ch.Count == 0) return false;
            data.Edit(table, id, ch);
            ctx.Log($"{table} {id} «{t.Get(row, "이름")}»: {Fx(cur)} → {(want != null ? Fx(want) : "эффекты прежние")}" + (lines != null ? ", описание: " + string.Join(" / ", lines) : ""));
            return true;
        }

        private static bool AddCombo(ModContext ctx, ICombos lib, JsonElement c)
        {
            var t = new Table("MagicCom");
            string nm0 = Has(c, "name", out var n0) ? n0.GetString() : "Комбинация";
            for (int i = 0; i < t.Length; i++) if (t.Get(i, "이름") as string == nm0) return false;   // уже добавлена (повторное применение)
            var def = new ComboDef
            {
                Name = Has(c, "name", out var nm) ? nm.GetString() : "Комбинация",
                CloneFrom = Has(c, "cloneFrom", out var cf) ? cf.GetInt32() : 1,
                Effects = Has(c, "effects", out var fx) ? Effects(fx) : new List<Effect>(),
            };
            if (Has(c, "req", out var rq) && rq.GetArrayLength() > 0) def.Requires = rq.EnumerateArray().Select(x => x.GetInt32()).ToArray();
            if (Has(c, "desc", out var ds) && ds.GetString() is string desc && desc != "") def.Lines = new List<string> { desc };
            int id = lib.Add(def);
            ctx.Log($"новая комбинация «{def.Name}»: ID {id}, эффекты {Fx(def.Effects)}");
            return true;
        }
    }
}
