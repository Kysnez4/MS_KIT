using System;
using System.Collections.Generic;
using System.Linq;
using ModKit;
using ModKitData;
using UnityEngine;
using UnityEngine.UI;

namespace SpellTuner
{
    /// <summary>
    /// Экран «Настройка заклинаний»: по каждому из 21 заклинания три числа — урон ×, КД −N%, число +N — и три пресета.
    /// Значения пишутся в слоты таблицы Ability через EditSpell (modkit_data 1.4: заодно обновляется текст уровней) и откатываются при выключении мода.
    /// Правки абсолютные: считаются от значений, прочитанных при запуске, поэтому повторное применение не накапливается.
    /// </summary>
    public class TunerMod : IKitMod
    {
        /// <summary>ID заклинания, эффект урона %, КД −% и числа +N (EFFECTS.md). 0 — у заклинания нет такого эффекта.</summary>
        private static readonly (int Id, int Dmg, int Cd, int Cnt)[] Map =
        {
            (1, 151, 158, 153), (2, 181, 194, 184), (3, 271, 281, 273), (4, 306, 0, 308), (5, 371, 378, 0), (6, 0, 233, 231), (7, 211, 219, 213),
            (8, 291, 296, 0), (9, 251, 259, 253), (10, 321, 327, 323), (11, 0, 0, 0), (12, 391, 406, 393), (13, 421, 431, 424), (14, 0, 443, 0),
            (15, 461, 472, 463), (16, 481, 493, 485), (17, 511, 519, 513), (18, 531, 542, 533), (19, 0, 566, 0), (20, 581, 590, 583), (21, 601, 609, 603),
        };

        private const double DMin = 0.1, DMax = 5, CMax = 50, NMax = 5;
        private const int PerPage = 7;

        private static readonly string[] PresetNames = { "Свои", "Лёгкий", "Обычный", "Хардкор" };
        // пресет: урон ×, КД −%, число +N
        private static readonly (double D, double C, double N)[] Presets = { (1, 0, 0), (1.5, 10, 1), (1, 0, 0), (0.7, 0, 0) };

        // состояние текущего включения мода
        private static bool _ready;
        private static readonly Dictionary<int, SpellInfo> Orig = new Dictionary<int, SpellInfo>();
        private static readonly Dictionary<int, (double D, double C, double N)> Applied = new Dictionary<int, (double, double, double)>();
        private static Text _status;

        private static string KD(int id) => "d" + id;
        private static string KC(int id) => "c" + id;
        private static string KN(int id) => "n" + id;

        public ModDef Define()
        {
            var def = new ModDef
            {
                Id = "spell_tuner", Name = "Настройка заклинаний", Version = "1.0",
                Description = "Экран «Настройки»: урон ×, КД −% и число +N для каждого из 21 заклинания, пресеты Лёгкий / Обычный / Хардкор. Собрано, на устройстве не проверено.",
                Depends = { ["modkit_data"] = ">=1.5", ["modkit_spells_data"] = ">=1.0" },
                OpenSettings = ctx => ctx.Guard("экран настройки заклинаний", () => Open(ctx)),
                OnEnable = Enable,
            };
            def.Options.Add(new ModOption { Key = "preset", Label = "Пресет", Default = 2, Min = 0, Max = 3, Step = 1, Hidden = true });
            foreach (var m in Map)
            {
                def.Options.Add(new ModOption { Key = KD(m.Id), Label = $"Урон ×, заклинание {m.Id}", Default = 1, Min = DMin, Max = DMax, Step = 0.1, Hidden = true });
                def.Options.Add(new ModOption { Key = KC(m.Id), Label = $"КД −%, заклинание {m.Id}", Default = 0, Min = 0, Max = CMax, Step = 1, Hidden = true });
                def.Options.Add(new ModOption { Key = KN(m.Id), Label = $"Число +N, заклинание {m.Id}", Default = 0, Min = 0, Max = NMax, Step = 1, Hidden = true });
            }
            return def;
        }

        private static void Enable(ModContext ctx)
        {
            _ready = false;
            Orig.Clear(); Applied.Clear(); _status = null;
            var raw = ctx.Use<IData>("modkit_data"); var data = ctx.Use<ISpellsData>("modkit_spells_data");
            ctx.OnCleanup(() => { _ready = false; Orig.Clear(); Applied.Clear(); _status = null; });
            raw.WhenReady(() => ctx.Guard("запуск настройки заклинаний", () =>
            {
                foreach (var m in Map)
                {
                    var s = data.Get(m.Id);
                    Orig[m.Id] = s;
                    Applied[m.Id] = (1, 0, 0);
                    ctx.Log($"исходно заклинание {m.Id} «{s.Name}»: " + string.Join(" ", s.Slots.Select(kv => $"слот{kv.Key}:{kv.Value.Id}={kv.Value.Value}")));
                }
                _ready = true;
                int n = Apply(ctx, data);
                ctx.Log($"пресет «{PresetNames[(int)ctx.Setting("preset")]}», при запуске изменено заклинаний: {n}");
            }));
        }

        private static (double D, double C, double N) Want(ModContext ctx, int id) => (ctx.Setting(KD(id)), ctx.Setting(KC(id)), ctx.Setting(KN(id)));

        private static bool Has(SpellInfo s, int effectId) => effectId != 0 && s.Slots.Values.Any(e => e.Id == effectId);

        private static double R2(double v) => Math.Round(v * 100) / 100;

        /// <summary>Приводит таблицу в соответствие с настройками; возвращает число изменённых заклинаний.</summary>
        private static int Apply(ModContext ctx, ISpellsData data)
        {
            if (!_ready) return 0;
            int changed = 0;
            foreach (var m in Map)
            {
                if (!Orig.TryGetValue(m.Id, out var o)) continue;
                var want = Want(ctx, m.Id);
                if (Applied[m.Id].Equals(want)) continue;
                var ch = new SpellChanges();
                var notes = new List<string>();
                // урон: все слоты с эффектом урона
                if (Has(o, m.Dmg))
                {
                    foreach (var kv in o.Slots.Where(kv => kv.Value.Id == m.Dmg))
                    {
                        ch.Slots[kv.Key] = new Effect(m.Dmg, R2(kv.Value.Value * want.D));
                        notes.Add($"урон слот{kv.Key} {kv.Value.Value}->{R2(kv.Value.Value * want.D)}");
                    }
                }
                // КД: первый слот с эффектом перезарядки, не ниже нуля
                if (Has(o, m.Cd))
                {
                    var kv = o.Slots.First(x => x.Value.Id == m.Cd);
                    double v = Math.Max(0, kv.Value.Value + want.C);
                    ch.Slots[kv.Key] = new Effect(m.Cd, v);
                    notes.Add($"КД слот{kv.Key} {kv.Value.Value}->{v}");
                }
                // число: первый слот с эффектом числа
                if (Has(o, m.Cnt))
                {
                    var kv = o.Slots.First(x => x.Value.Id == m.Cnt);
                    double v = kv.Value.Value + want.N;
                    ch.Slots[kv.Key] = new Effect(m.Cnt, v);
                    notes.Add($"число слот{kv.Key} {kv.Value.Value}->{v}");
                }
                Applied[m.Id] = want;
                if (ch.Slots.Count == 0) continue;
                data.Edit(m.Id, ch);
                changed++;
                // чтение обратно
                var now = data.Get(m.Id);
                bool ok = ch.Slots.All(kv => now.Slots.TryGetValue(kv.Key, out var e) && e.Id == kv.Value.Id && Math.Abs(e.Value - kv.Value.Value) < 0.01);
                ctx.Log($"заклинание {m.Id} «{o.Name}»: урон ×{UiKit.Fmt(want.D)}, КД −{UiKit.Fmt(want.C)}, число +{UiKit.Fmt(want.N)} ({string.Join("; ", notes)}); чтение обратно: {(ok ? "совпало" : "НЕ СОВПАЛО")}");
                if (!ok) ctx.Warn($"заклинание {m.Id}: значения после записи не совпали");
            }
            if (_status != null) UpdateStatus(ctx);
            return changed;
        }

        private static void UpdateStatus(ModContext ctx)
        {
            try
            {
                int p = (int)ctx.Setting("preset");
                int custom = Map.Count(m => !Want(ctx, m.Id).Equals((1.0, 0.0, 0.0)));
                _status.text = $"Пресет: {PresetNames[Math.Max(0, Math.Min(3, p))]}   ·   изменено заклинаний: {custom}";
            }
            catch { /* окно закрыто */ }
        }

        private static void Open(ModContext ctx)
        {
            if (!_ready) { Log.Write("[spell_tuner] данные игры ещё не готовы — откройте экран через несколько секунд"); }
            var data = ctx.Use<ISpellsData>("modkit_spells_data");
            var win = UiKit.OpenPage("spell_tuner", "Настройка заклинаний", "Меняется сразу, откат при выключении мода");
            double W = UiKit.LogicalW - 60;
            double y = win.top;
            _status = UiKit.Tint(UiKit.Label(win.rt, "", 30, y, W, 40, 22, 3), UiKit.Col(0.7, 0.75, 0.85));
            UiKit.Pager pager = null;

            double bw = (W - 2 * 16) / 3;
            for (int i = 1; i <= 3; i++)
            {
                int preset = i;
                UiKit.AddButton(win.rt, PresetNames[i], 30 + (i - 1) * (bw + 16), y + 48, bw, 70, 28, new[] { 0.22, 0.30, 0.45, 0.95 }, () =>
                {
                    var (d, c, n) = Presets[preset];
                    foreach (var m in Map)
                    {
                        ctx.SetSetting(KD(m.Id), d); ctx.SetSetting(KC(m.Id), c); ctx.SetSetting(KN(m.Id), n);
                    }
                    ctx.SetSetting("preset", preset);
                    ctx.Log($"пресет «{PresetNames[preset]}»: урон ×{d}, КД −{c}%, число +{n}");
                    int k = Apply(ctx, data);
                    ctx.Log($"пресет применён: изменено заклинаний {k}");
                    UpdateStatus(ctx);
                    pager?.Go(pager.Page);
                });
            }

            var pages = new List<string>();
            for (int p = 0; p * PerPage < Map.Length; p++)
                pages.Add($"Заклинания {p * PerPage + 1}-{Math.Min(Map.Length, (p + 1) * PerPage)}");
            double py = y + 136;
            pager = UiKit.AddPager(win.rt, 30, py, W, UiKit.ScreenH - py - 30, new UiKit.PagerOptions
            {
                Pages = pages,
                OnPage = (pi, title, page) => FillPage(ctx, data, pi, page, W),
            });
            UpdateStatus(ctx);
        }

        private static void FillPage(ModContext ctx, ISpellsData data, int pageIndex, RectTransform page, double W)
        {
            double labelW = 215, gap = 12, fw = 130;
            double Fx(int i) => labelW + 12 + i * (fw + gap);
            string[] heads = { "урон ×", "КД −%", "число +" };
            for (int i = 0; i < 3; i++) UiKit.Tint(UiKit.Label(page, heads[i], Fx(i), 0, fw, 44, 22, 4), UiKit.Col(0.55, 0.8, 1));
            for (int r = 0; r < PerPage; r++)
            {
                int idx = pageIndex * PerPage + r;
                if (idx >= Map.Length) break;
                var m = Map[idx];
                double y = 48 + r * 84;
                Orig.TryGetValue(m.Id, out var o);
                var row = UiKit.Node("row", page);
                UiKit.Place(row.rt, 0, y, W, 76);
                UiKit.AddImage(row.go, 0.08, 0.10, 0.15, 1).raycastTarget = false;
                UiKit.Tint(UiKit.Label(row.rt, o != null ? o.Name : "Заклинание " + m.Id, 10, 0, labelW, 76, 20, 3), UiKit.Col(0.92, 0.92, 0.92));
                AddField(ctx, data, row.rt, Fx(0), fw, KD(m.Id), o != null && Has(o, m.Dmg), DMin, DMax, true);
                AddField(ctx, data, row.rt, Fx(1), fw, KC(m.Id), o != null && Has(o, m.Cd), 0, CMax, false);
                AddField(ctx, data, row.rt, Fx(2), fw, KN(m.Id), o != null && Has(o, m.Cnt), 0, NMax, false);
            }
        }

        private static void AddField(ModContext ctx, ISpellsData data, Transform row, double x, double w, string key, bool available, double min, double max, bool dec)
        {
            if (!available)
            {
                UiKit.Tint(UiKit.Label(row, "—", x, 8, w, 60, 30, 4), UiKit.Col(0.4, 0.42, 0.5));
                return;
            }
            InputField input = null;
            input = UiKit.AddNumberField(row, UiKit.Fmt(ctx.Setting(key)), x, 8, w, 60, 30, dec, text =>
            {
                double cur = ctx.Setting(key);
                if (!double.TryParse(text.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)) v = cur;
                v = Math.Min(max, Math.Max(min, dec ? v : Math.Round(v)));
                if (Math.Abs(v - cur) > 1e-9)
                {
                    ctx.SetSetting(key, v);
                    ctx.SetSetting("preset", 0);
                    ctx.Log($"настройка {key}: {UiKit.Fmt(cur)} -> {UiKit.Fmt(v)}");
                    Apply(ctx, data);
                }
                input.text = UiKit.Fmt(v);
            });
        }
    }
}
