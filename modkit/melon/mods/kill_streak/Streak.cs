using System;
using System.Linq;
using Il2Cpp;
using ModKit;

namespace KillStreak
{
    /// <summary>
    /// Серия убийств: событие enemyDeath увеличивает счётчик; серия обрывается, если врагов не убивали «окно» секунд. Каждые «шаг» убийств серии
    /// повышают уровень бонуса (до «макс. уровня»); бонус (Атака % и Скорость передвижения % за уровень) действует «длительность» секунд игрового
    /// времени с последнего повышения и снимается сам. Бонус задаётся ctx.SetGlobalEffects; игра подставляет его при пересчёте характеристик,
    /// поэтому после каждой смены мод просит пересчёт (V_Ability_Manager.AbilityStatApply(2), настройка «recalc»). Индикатор — кнопка на экране боя.
    /// Собрано, на устройстве не проверено (в том числе принудительный пересчёт).
    /// </summary>
    public class StreakMod : IKitMod
    {
        private static readonly System.Diagnostics.Stopwatch Real = System.Diagnostics.Stopwatch.StartNew();
        private static double _game, _last;                 // игровое время: не идёт на паузе и вне боя
        private static int _streak, _level, _applied = -1, _best;
        private static double _lastKillAt, _bonusUntil;
        private static bool _wantApply;
        private static V_Ability_Manager _mgr;
        private static UiKit.HudButton _hud;

        public ModDef Define()
        {
            var d = new ModDef
            {
                Id = "kill_streak", Name = "Серия убийств", Version = "1.0",
                Description = "Каждые N убийств подряд повышают бонус к атаке и скорости на несколько секунд (игрового времени). Индикатор на экране боя. На устройстве не проверено.",
                OnEnable = Enable,
                OnDisable = ctx => Cleanup(ctx),
            };
            d.Options.Add(new ModOption { Key = "step", Label = "Убийств на уровень серии", Default = 10, Min = 2, Max = 100, Step = 1 });
            d.Options.Add(new ModOption { Key = "window", Label = "Окно серии, с (без убийств — обрыв)", Default = 4, Min = 1, Max = 30, Step = 1 });
            d.Options.Add(new ModOption { Key = "duration", Label = "Длительность бонуса, с", Default = 8, Min = 3, Max = 30, Step = 1 });
            d.Options.Add(new ModOption { Key = "maxlevel", Label = "Макс. уровень бонуса", Default = 5, Min = 1, Max = 10, Step = 1 });
            d.Options.Add(new ModOption { Key = "atk", Label = "Атака за уровень, %", Default = 4, Min = 0, Max = 20, Step = 1 });
            d.Options.Add(new ModOption { Key = "speed", Label = "Скорость за уровень, %", Default = 3, Min = 0, Max = 20, Step = 1 });
            d.Options.Add(new ModOption { Key = "recalc", Label = "Пересчёт характеристик при смене бонуса (1/0, не проверено)", Default = 1, Min = 0, Max = 1, Step = 1 });
            return d;
        }

        private static void Enable(ModContext ctx)
        {
            var spells = ctx.Use<ISpells>("modkit_spells");
            _game = 0; _last = Real.Elapsed.TotalSeconds; _streak = 0; _level = 0; _applied = 0; _best = 0; _wantApply = false;

            void Reset(string why)
            {
                if (_streak > 0 || _level > 0) ctx.Log($"серия сброшена ({why}): было {_streak} убийств, уровень {_level}");
                bool had = _level > 0 || _applied != 0;
                _streak = 0; _level = 0; if (had) _wantApply = true;
            }

            ctx.Events.On("enemyDeath", e =>
            {
                if (_game - _lastKillAt > ctx.Setting("window")) _streak = 0;      // окно истекло между убийствами
                _lastKillAt = _game;
                _streak++;
                if (_streak > _best) _best = _streak;
                int step = Math.Max(2, (int)ctx.Setting("step")), max = Math.Max(1, (int)ctx.Setting("maxlevel"));
                if (_streak % step == 0)
                {
                    int lvl = Math.Min(max, _streak / step);
                    _bonusUntil = _game + ctx.Setting("duration");
                    if (lvl != _level) { _level = lvl; _wantApply = true; }
                    ctx.Log($"серия {_streak} → бонус уровня {_level} на {ctx.Setting("duration")} с (игровое время {_game:0.0})");
                }
            });
            ctx.Events.On("battleStart", e => { _game = 0; _lastKillAt = 0; _bonusUntil = 0; Reset("начало боя"); });
            ctx.Events.On("battleEnd", e => { ctx.Log($"бой закончен: лучшая серия {_best}"); Reset("конец боя"); });

            ctx.Every(200, () =>
            {
                double now = Real.Elapsed.TotalSeconds;
                bool live = spells.InBattle && !spells.Paused;
                if (live) _game += now - _last;
                _last = now;
                if (live)
                {
                    if (_streak > 0 && _game - _lastKillAt > ctx.Setting("window")) { ctx.Log($"серия оборвана: {_streak} убийств, окно {ctx.Setting("window")} с"); _streak = 0; }
                    if (_level > 0 && _game >= _bonusUntil) { ctx.Log($"бонус уровня {_level} закончился (серия была {_streak})"); _level = 0; _wantApply = true; }
                }
                if (_wantApply || _applied != _level) ApplyBonus(ctx);
                UpdateHud(ctx, spells, live);
            });
            ctx.Log("серия убийств включена: шаг " + ctx.Setting("step") + ", окно " + ctx.Setting("window") + " с, длительность " + ctx.Setting("duration") + " с");
        }

        private static void ApplyBonus(ModContext ctx)
        {
            _wantApply = false;
            _applied = _level;
            double atk = _level * ctx.Setting("atk"), spd = _level * ctx.Setting("speed");
            var pairs = new[] { (4, atk), (30, spd) }.Where(p => p.Item2 > 0).ToArray();
            ctx.Use<IEffects>("modkit_effects").SetGlobal(pairs);
            ctx.Log($"бонус серии: уровень {_level}, Атака +{atk}%, Скорость +{spd}%");
            if (ctx.Setting("recalc") >= 1) Recalc(ctx);
        }

        /// <summary>Пересчёт характеристик игрой: без него подстановка эффекта попадёт в героя только при следующем пересчёте (подбор артефакта, уровень).</summary>
        private static void Recalc(ModContext ctx)
        {
            try
            {
                if (_mgr == null || _mgr.gameObject == null) _mgr = FindMgr();
                if (_mgr == null) { ctx.Log("пересчёт: V_Ability_Manager не найден (нет боя?)"); return; }
                ctx.Mark("kill_streak: AbilityStatApply(2)");
                _mgr.AbilityStatApply(2);
                ctx.Log("пересчёт характеристик выполнен: AbilityStatApply(2)");
            }
            catch (Exception e) { _mgr = null; ctx.Log("пересчёт не удался: " + e.Message); }
        }

        private static V_Ability_Manager FindMgr()
        {
            foreach (var o in UnityEngine.Object.FindObjectsOfType(Il2CppInterop.Runtime.Il2CppType.Of<V_Ability_Manager>()))
            {
                var m = o.TryCast<V_Ability_Manager>();
                if (m != null) return m;
            }
            return null;
        }

        private static void UpdateHud(ModContext ctx, ISpells spells, bool live)
        {
            bool show = spells.InBattle && !spells.Paused;
            if (show && (_hud == null || !_hud.Alive))
                _hud = UiKit.AddHudButton("kill_streak", "Серия 0", 20, 280, 230, 70, 24, new[] { 0.15, 0.15, 0.2, 0.75 }, () => { });
            else if (!show && _hud != null) { _hud.Destroy(); _hud = null; }
            if (_hud == null) return;
            double left = _level > 0 ? Math.Max(0, _bonusUntil - _game) : 0;
            _hud.SetText(_level > 0 ? $"Серия {_streak}  ур.{_level}  {left:0.0}с" : $"Серия {_streak}");
        }

        private static void Cleanup(ModContext ctx)
        {
            _hud?.Destroy(); _hud = null;
            _level = 0; _streak = 0;
            try { ctx.Use<IEffects>("modkit_effects").SetGlobal(new (int, double)[0]); if (_applied > 0 && ctx.Setting("recalc") >= 1) Recalc(ctx); } catch (Exception e) { ctx.Log("снятие бонуса: " + e.Message); }
            _applied = 0;
        }
    }
}
