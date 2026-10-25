using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ModKit;

namespace Achievements
{
    /// <summary>
    /// Дерево из 20 достижений на PanView. Прогресс считается по событиям ядра: enemyDeath (убийства), bossSpawn (боссы), battleStart/battleEnd (бои),
    /// areaChange (смены зоны); время боя — по игровому времени (на паузе и после смерти героя не идёт). Прогресс лежит в файле
    /// UserData/ModKit/achievements.json. Награды — маленькие постоянные бонусы через ctx.SetGlobalEffects (применяются при очередном пересчёте
    /// характеристик игры: со следующего боя). Собрано, на устройстве не проверено.
    /// </summary>
    public class AchMod : IKitMod
    {
        private class Ach
        {
            public string Id, Name, Desc, Counter, Req; public double Target; public int Fx; public double Value; public double X, Y;
        }

        // Счётчики: kills, bosses, battles, areas — суммы за всё время; surv — лучшее время жизни в одном бою (с), runKills — лучшие убийства за бой.
        private static readonly Ach[] List =
        {
            new Ach { Id = "first_blood", Name = "Первая кровь", Desc = "Убить первого врага", Counter = "kills", Target = 1, Fx = 4, Value = 1, X = 150, Y = 150 },
            new Ach { Id = "kills_100", Name = "Сотня", Desc = "Убить 100 врагов", Counter = "kills", Target = 100, Req = "first_blood", Fx = 4, Value = 1, X = 380, Y = 150 },
            new Ach { Id = "kills_1000", Name = "Тысяча", Desc = "Убить 1000 врагов", Counter = "kills", Target = 1000, Req = "kills_100", Fx = 4, Value = 2, X = 610, Y = 150 },
            new Ach { Id = "kills_5000", Name = "Косарь", Desc = "Убить 5000 врагов", Counter = "kills", Target = 5000, Req = "kills_1000", Fx = 4, Value = 3, X = 840, Y = 150 },
            new Ach { Id = "kills_20000", Name = "Жнец", Desc = "Убить 20000 врагов", Counter = "kills", Target = 20000, Req = "kills_5000", Fx = 4, Value = 5, X = 1070, Y = 150 },

            new Ach { Id = "run_500", Name = "Резня", Desc = "Убить 500 врагов за один бой", Counter = "runKills", Target = 500, Req = "kills_100", Fx = 14, Value = 2, X = 380, Y = 370 },
            new Ach { Id = "run_2000", Name = "Буря клинков", Desc = "Убить 2000 врагов за один бой", Counter = "runKills", Target = 2000, Req = "run_500", Fx = 14, Value = 3, X = 610, Y = 370 },

            new Ach { Id = "battle_1", Name = "Новичок", Desc = "Начать первый бой", Counter = "battles", Target = 1, Req = "first_blood", Fx = 40, Value = 3, X = 150, Y = 590 },
            new Ach { Id = "battle_10", Name = "Ветеран", Desc = "Начать 10 боёв", Counter = "battles", Target = 10, Req = "battle_1", Fx = 40, Value = 5, X = 380, Y = 590 },
            new Ach { Id = "battle_50", Name = "Закалённый", Desc = "Начать 50 боёв", Counter = "battles", Target = 50, Req = "battle_10", Fx = 40, Value = 7, X = 610, Y = 590 },

            new Ach { Id = "surv_5", Name = "Пять минут", Desc = "Прожить 5 минут в одном бою", Counter = "surv", Target = 300, Req = "battle_1", Fx = 23, Value = 1, X = 380, Y = 810 },
            new Ach { Id = "surv_10", Name = "Десять минут", Desc = "Прожить 10 минут в одном бою", Counter = "surv", Target = 600, Req = "surv_5", Fx = 23, Value = 2, X = 610, Y = 810 },
            new Ach { Id = "surv_20", Name = "Двадцать минут", Desc = "Прожить 20 минут в одном бою", Counter = "surv", Target = 1200, Req = "surv_10", Fx = 23, Value = 3, X = 840, Y = 810 },
            new Ach { Id = "surv_30", Name = "Полчаса", Desc = "Прожить 30 минут в одном бою", Counter = "surv", Target = 1800, Req = "surv_20", Fx = 23, Value = 4, X = 1070, Y = 810 },

            new Ach { Id = "boss_1", Name = "Первый босс", Desc = "Встретить первого босса", Counter = "bosses", Target = 1, Req = "battle_1", Fx = 20, Value = 3, X = 380, Y = 1030 },
            new Ach { Id = "boss_10", Name = "Охотник", Desc = "Встретить 10 боссов", Counter = "bosses", Target = 10, Req = "boss_1", Fx = 20, Value = 5, X = 610, Y = 1030 },
            new Ach { Id = "boss_50", Name = "Убийца титанов", Desc = "Встретить 50 боссов", Counter = "bosses", Target = 50, Req = "boss_10", Fx = 20, Value = 8, X = 840, Y = 1030 },

            new Ach { Id = "area_1", Name = "Новые земли", Desc = "Пройти в следующую зону", Counter = "areas", Target = 1, Req = "battle_1", Fx = 30, Value = 2, X = 380, Y = 1250 },
            new Ach { Id = "area_5", Name = "Путешественник", Desc = "Сменить зону 5 раз", Counter = "areas", Target = 5, Req = "area_1", Fx = 30, Value = 3, X = 610, Y = 1250 },
            new Ach { Id = "area_20", Name = "Покоритель", Desc = "Сменить зону 20 раз", Counter = "areas", Target = 20, Req = "area_5", Fx = 30, Value = 5, X = 840, Y = 1250 },
        };

        private static readonly Dictionary<int, string> FxName = new Dictionary<int, string>
        {
            [4] = "Атака", [14] = "Шанс крита", [40] = "Радиус сбора предметов", [23] = "Получаемый урон −", [20] = "Максимальное здоровье", [30] = "Скорость передвижения",
        };
        private static readonly Dictionary<string, string> CounterName = new Dictionary<string, string>
        {
            ["kills"] = "Убито врагов", ["runKills"] = "Рекорд убийств за бой", ["battles"] = "Боёв начато", ["surv"] = "Рекорд времени жизни, с",
            ["bosses"] = "Боссов встречено", ["areas"] = "Смен зоны",
        };

        // ---- состояние (хранится в файле)
        private static readonly Dictionary<string, double> C = new Dictionary<string, double>();
        private static readonly HashSet<string> Done = new HashSet<string>();
        private static bool _dirty;
        private static double _lastSave;
        private static double _runKills, _runSeconds;
        private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();
        private static readonly Queue<string> Toasts = new Queue<string>();
        private static UiKit.HudButton _toast;
        private static double _toastUntil;
        private static Action _open;
        private static int _evLogs;

        private static string FilePath => Path.Combine(Paths.Dir, "achievements.json");
        private static double Get(string k) => C.TryGetValue(k, out var v) ? v : 0;

        // ---------------------------------------------------------------- файл

        private static void Load(ModContext ctx)
        {
            C.Clear(); Done.Clear();
            try
            {
                if (!File.Exists(FilePath)) { ctx.Log("файл прогресса не найден, начинаем с нуля: " + FilePath); return; }
                var text = File.ReadAllText(FilePath, Encoding.UTF8);
                foreach (Match m in Regex.Matches(text, "\"(\\w+)\"\\s*:\\s*(-?\\d+(?:\\.\\d+)?)"))
                    C[m.Groups[1].Value] = double.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
                var d = Regex.Match(text, "\"done\"\\s*:\\s*\\[(.*?)\\]", RegexOptions.Singleline);
                if (d.Success) foreach (Match m in Regex.Matches(d.Groups[1].Value, "\"(\\w+)\"")) Done.Add(m.Groups[1].Value);
                ctx.Log($"прогресс загружен: убийств {Get("kills"):0}, боссов {Get("bosses"):0}, боёв {Get("battles"):0}, зон {Get("areas"):0}, выполнено {Done.Count} из {List.Length}");
            }
            catch (Exception e) { ctx.Log("чтение прогресса: " + e.Message); }
        }

        private static void Save(ModContext ctx, string why)
        {
            try
            {
                var sb = new StringBuilder("{\n");
                foreach (var k in new[] { "kills", "bosses", "battles", "areas", "surv", "runKills" })
                    sb.Append($"  \"{k}\": {Get(k).ToString(System.Globalization.CultureInfo.InvariantCulture)},\n");
                sb.Append("  \"done\": [").Append(string.Join(", ", Done.OrderBy(x => x, StringComparer.Ordinal).Select(x => "\"" + x + "\""))).Append("]\n}\n");
                Directory.CreateDirectory(Paths.Dir);
                File.WriteAllText(FilePath, sb.ToString(), new UTF8Encoding(false));
                _dirty = false; _lastSave = Clock.Elapsed.TotalSeconds;
                ctx.Log($"прогресс сохранён ({why}): убийств {Get("kills"):0}, выполнено {Done.Count} из {List.Length}");
            }
            catch (Exception e) { ctx.Log("запись прогресса: " + e.Message); }
        }

        // ---------------------------------------------------------------- логика

        private static bool Reached(Ach a) => Get(a.Counter) >= a.Target;

        /// <summary>Закрывает достижения, у которых цель достигнута и выполнено условие дерева; повторяет, пока что-то закрывается. true — что-то получено.</summary>
        private static bool Check(ModContext ctx)
        {
            bool any = false, again = true;
            while (again)
            {
                again = false;
                foreach (var a in List)
                {
                    if (Done.Contains(a.Id) || !Reached(a) || (a.Req != null && !Done.Contains(a.Req))) continue;
                    Done.Add(a.Id); again = any = true; _dirty = true;
                    ctx.Log($"достижение «{a.Name}» получено ({CounterName[a.Counter]}: {Get(a.Counter):0} из {a.Target:0}); награда: {FxName[a.Fx]} +{a.Value}%; всего {Done.Count} из {List.Length}");
                    Toasts.Enqueue($"Достижение: {a.Name}  (+{a.Value}% {FxName[a.Fx].ToLowerInvariant()})");
                }
            }
            return any;
        }

        private static void ApplyRewards(ModContext ctx)
        {
            double scale = ctx.Setting("reward") / 100.0;
            var sum = new Dictionary<int, double>();
            foreach (var a in List) if (Done.Contains(a.Id)) sum[a.Fx] = (sum.TryGetValue(a.Fx, out var v) ? v : 0) + a.Value * scale;
            var pairs = sum.Where(kv => kv.Value > 0).Select(kv => (kv.Key, Math.Round(kv.Value, 1))).ToArray();
            ctx.Use<IEffects>("modkit_effects").SetGlobal(pairs);
            ctx.Log($"награды: {(pairs.Length == 0 ? "нет" : string.Join(", ", pairs.Select(p => $"{FxName[p.Key]} +{p.Item2}% (эффект {p.Key})")))} (масштаб {scale * 100:0}%)");
        }

        private static void Bump(string key, double by) { C[key] = Get(key) + by; _dirty = true; }
        private static void Best(string key, double v) { if (v > Get(key)) { C[key] = v; _dirty = true; } }

        // ---------------------------------------------------------------- мод

        public ModDef Define() => new ModDef
        {
            Id = "achievements", Name = "Достижения", Version = "1.1",
            Description = "Дерево из 20 достижений (убийства, боссы, бои, время жизни, смены зоны) с маленькими постоянными наградами. «Настройки» — карта достижений. На устройстве не проверено.",
            Options =
            {
                new ModOption { Key = "reward", Label = "Награды, % от номинала (0 — только счётчики)", Default = 100, Min = 0, Max = 300, Step = 25 },
            },
            OpenSettings = ctx => _open?.Invoke(),
            OnEnable = Enable,
            OnDisable = ctx => { Save(ctx, "выключение"); _toast?.Destroy(); _toast = null; _open = null; },
        };

        private static void Enable(ModContext ctx)
        {
            Load(ctx);
            Check(ctx);
            ApplyRewards(ctx);
            var spells = ctx.Use<ISpells>("modkit_spells");      // нужен InBattle/Paused для игрового времени
            double last = Clock.Elapsed.TotalSeconds;

            string D(object e, string key) { var ed = e as EventData; return ed != null && ed.TryGetValue(key, out var v) ? Convert.ToString(v) : "?"; }

            ctx.Events.On("enemyDeath", e =>
            {
                Bump("kills", 1); _runKills++;
                Best("runKills", _runKills);
                if (Get("kills") % 100 == 0) ctx.Log($"убийств всего {Get("kills"):0}, за бой {_runKills:0}");
                if (_evLogs++ < 3) ctx.Log($"событие enemyDeath: данные={string.Join(",", ((EventData)e).Keys)}, тип врага={(((EventData)e).TryGetValue("enemy", out var en) && en != null ? en.GetType().Name : "нет")}");
                if (Check(ctx)) ApplyRewards(ctx);
            });
            ctx.Events.On("bossSpawn", e =>
            {
                int n = 1;
                try { int c = ((EventData)e).Get<int>("count"); if (c >= 1 && c <= 10) n = c; } catch { /* оставляем 1 */ }
                Bump("bosses", n);
                ctx.Log($"событие bossSpawn: unitId={D(e, "unitId")}, count={D(e, "count")}; боссов всего {Get("bosses"):0}");
                if (Check(ctx)) ApplyRewards(ctx);
            });
            ctx.Events.On("battleStart", e =>
            {
                _runKills = 0; _runSeconds = 0;
                Bump("battles", 1);
                ctx.Log($"событие battleStart: боёв всего {Get("battles"):0}");
                if (Check(ctx)) ApplyRewards(ctx);
            });
            ctx.Events.On("battleEnd", e =>
            {
                Best("surv", Math.Floor(_runSeconds)); Best("runKills", _runKills);
                ctx.Log($"событие battleEnd: прожито {_runSeconds:0} с, убийств за бой {_runKills:0}; рекорды {Get("surv"):0} с и {Get("runKills"):0}");
                if (Check(ctx)) ApplyRewards(ctx);
                Save(ctx, "конец боя");
            });
            ctx.Events.On("areaChange", e =>
            {
                Bump("areas", 1);
                ctx.Log($"событие areaChange: смен зоны всего {Get("areas"):0}");
                if (Check(ctx)) ApplyRewards(ctx);
            });

            // игровое время боя, тосты, периодическое сохранение (всё на главном потоке)
            ctx.Every(500, () =>
            {
                double now = Clock.Elapsed.TotalSeconds;
                if (spells.InBattle && !spells.Paused) { _runSeconds += now - last; Best("surv", Math.Floor(_runSeconds)); }
                last = now;
                if (Check(ctx)) ApplyRewards(ctx);
                if (_dirty && now - _lastSave > 30) Save(ctx, "по таймеру");
                ShowToast(ctx, now);
            });

            _open = () => ctx.Guard("карта достижений", () => OpenMap(ctx));
            ctx.Log($"достижения включены ({List.Length}); файл {FilePath}");
        }

        private static void ShowToast(ModContext ctx, double now)
        {
            if (_toast != null && (now >= _toastUntil || !_toast.Alive)) { _toast.Destroy(); _toast = null; }
            if (_toast == null && Toasts.Count > 0)
            {
                var text = Toasts.Dequeue();
                try
                {
                    _toast = UiKit.AddHudButton("achievements_toast", text, 30, 150, UiKit.LogicalW - 60, 90, 26, new[] { 0.35, 0.28, 0.05, 0.95 }, () => { });
                    _toastUntil = now + 4;
                }
                catch (Exception e) { ctx.Log("тост не показан: " + e.Message + "; " + text); }
            }
        }

        // ---------------------------------------------------------------- карта

        private static UiKit.PanState StateOf(Ach a) =>
            Done.Contains(a.Id) ? UiKit.PanState.Done : (a.Req == null || Done.Contains(a.Req)) ? UiKit.PanState.Available : UiKit.PanState.Locked;

        private static void OpenMap(ModContext ctx)
        {
            var o = new UiKit.PanOptions { ContentW = 1300, ContentH = 1400, NodeSize = 100 };
            foreach (var a in List) o.Nodes.Add(new UiKit.PanNode(a.Id, a.X, a.Y, null, a.Name, StateOf(a)));
            foreach (var a in List) if (a.Req != null) o.Edges.Add(new UiKit.PanEdge(a.Req, a.Id));

            UnityEngine.UI.Text name = null, body = null, hint = null;
            o.OnTap = n =>
            {
                var a = List.First(x => x.Id == n.Id);
                var prog = Math.Min(Get(a.Counter), a.Target);
                name.text = a.Name + (Done.Contains(a.Id) ? "   (получено)" : "");
                body.text = $"{a.Desc}\n{CounterName[a.Counter]}: {Get(a.Counter):0} / {a.Target:0}\nНаграда: {FxName[a.Fx]} +{a.Value * ctx.Setting("reward") / 100.0:0.#}%";
                hint.text = Done.Contains(a.Id) ? "Награда действует" : a.Req != null && !Done.Contains(a.Req) ? "Сначала: " + List.First(x => x.Id == a.Req).Name : "Прогресс " + (int)(100 * prog / a.Target) + "%";
            };
            var map = UiKit.OpenPanWindow("achievements_map", "Достижения", $"Получено {Done.Count} из {List.Length}. Тяните карту, нажмите на узел", o, 330);
            var parent = map.View.parent;
            double top = UiKit.ScreenH - 320;
            name = UiKit.Tint(UiKit.Label(parent, "Нажмите на достижение", 40, top, UiKit.LogicalW - 80, 50, 32, 3), UiKit.Gold);
            body = UiKit.Label(parent, "", 40, top + 54, UiKit.LogicalW - 80, 170, 24, 0);
            hint = UiKit.Tint(UiKit.Label(parent, "", 40, top + 228, UiKit.LogicalW - 80, 40, 20, 3), UiKit.Col(0.7, 0.75, 0.85));
            var first = List.FirstOrDefault(a => !Done.Contains(a.Id) && StateOf(a) == UiKit.PanState.Available) ?? List[0];
            map.CenterOn(first.Id);
            ctx.Log($"карта достижений открыта: получено {Done.Count} из {List.Length}");
        }
    }
}
