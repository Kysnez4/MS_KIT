using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Il2Cpp;

namespace ModKit
{
    /// <summary>Заклинания героя в бою: уровень, отключение авто-каста, ручной каст. Проверено на Огненном шаре (docs/ACTIVE_SPELLS.md, MELONLOADER.md §16).</summary>
    public interface ISpells
    {
        /// <summary>
        /// Делает заклинание активным: авто-каст отключается, на экране боя появляется кнопка, нажатие запускает Burst залпов, затем идёт перезарядка кнопки
        /// (по игровому времени, на паузе стоит). Кнопка видна, пока идёт бой, герой жив, заклинание получено и нет паузы. Убирается при выключении мода.
        /// </summary>
        IActiveSpell AddActive(ActiveSpellDef def);
        /// <summary>Идёт бой: герой найден и жив (Unit_Hp > 0), его объект активен и главное меню сейчас не показано.</summary>
        bool InBattle { get; }
        /// <summary>Пауза боя (игра заморозила время, Time.timeScale = 0).</summary>
        bool Paused { get; }
        /// <summary>Уровень заклинания (ID из таблицы Ability, 1..21); 0 — не получено или боя нет.</summary>
        int Level(int id);
        /// <summary>Заклинание, для которого есть корутина Play_X и флаг IsX_P (кастуется вручную).</summary>
        bool CanCast(int id);
        /// <summary>true — игра сама кастует (по умолчанию). false — авто-каст отключён: запущенная корутина остановлена, флаг IsX_P поднят. Мод возвращает авто-каст при выключении.</summary>
        void SetAuto(int id, bool auto);
        /// <summary>Один залп: новая корутина Play_X и steps шагов MoveNext (по умолчанию 2). false — боя нет, заклинание не получено или нет корутины.</summary>
        bool Cast(int id, int steps = 2);
        /// <summary>Перезарядка заклинания в секундах — как в игре (UnitApply_{Имя}PSpeed героя, с учётом артефактов и скорости); 0 — неизвестна или боя нет.</summary>
        double Cooldown(int id);
    }

    public class ActiveSpellDef
    {
        /// <summary>ID заклинания (Ability 1..21), у которого есть Play_X (см. CanCast).</summary>
        public int SpellId;
        /// <summary>Подпись на кнопке; по умолчанию «Каст».</summary>
        public string Label = "Каст";
        /// <summary>Перезарядка кнопки, с. 0 или меньше — перезарядка заклинания игры (Cooldown).</summary>
        public double Cooldown = 8;
        /// <summary>Залпов за нажатие.</summary>
        public int Burst = 1;
        /// <summary>Место кнопки: 0 — справа внизу, дальше влево по 170 пикселей.</summary>
        public int Slot = 0;
        /// <summary>Ряд кнопок: 0 — нижний, 1 — выше на 170 пикселей и т. д.</summary>
        public int Row = 0;
        /// <summary>Цвет кнопки RGBA 0..1.</summary>
        public double[] Color = { 0.75, 0.25, 0.05, 0.9 };
        /// <summary>Вызывается после удачного нажатия (число выпущенных залпов).</summary>
        public Action<int> OnCast;
    }

    public interface IActiveSpell
    {
        /// <summary>Секунд до готовности кнопки (0 — готова).</summary>
        double Remaining { get; }
        /// <summary>Нажать программно (как кнопку). true — каст вышел.</summary>
        bool Press();
        /// <summary>Снять кнопку и вернуть авто-каст.</summary>
        void Remove();
    }

    internal class ActiveSpell : IActiveSpell
    {
        private readonly ISpells _sp; private readonly ActiveSpellDef _d; private readonly string _owner;
        private UiKit.HudButton _btn;
        private readonly System.Diagnostics.Stopwatch _real = System.Diagnostics.Stopwatch.StartNew();
        private double _game, _last, _readyAt;
        private bool _removed;

        public ActiveSpell(ISpells sp, ActiveSpellDef d, string owner) { _sp = sp; _d = d; _owner = owner; }

        public double Remaining => Math.Max(0, _readyAt - _game);

        public void Start(Action<double, Action> every)
        {
            _sp.SetAuto(_d.SpellId, false);
            every(150, Tick);
        }

        private void Tick()
        {
            if (_removed) return;
            double now = _real.Elapsed.TotalSeconds;
            if (!_sp.Paused) _game += now - _last;
            _last = now;
            bool show = _sp.InBattle && _sp.Level(_d.SpellId) >= 1;
            if (show && (_btn == null || !_btn.Alive))
                _btn = UiKit.AddHudButton("active_" + _owner + "_" + _d.SpellId, _d.Label, UiKit.LogicalW - 200 - _d.Slot * 170, UiKit.ScreenH - 520 - _d.Row * 170, 160, 160, 30, _d.Color, () => Press());
            else if (!show && _btn != null) { _btn.Destroy(); _btn = null; }
            if (_btn != null)
            {
                _btn.SetVisible(!_sp.Paused);
                double left = Remaining;
                _btn.SetText(left > 0 ? left.ToString("0.0") : _d.Label);
            }
        }

        public bool Press()
        {
            if (_removed || _game < _readyAt) return false;
            int done = 0;
            for (int i = 0; i < Math.Max(1, _d.Burst); i++) if (_sp.Cast(_d.SpellId)) done++;
            if (done == 0) return false;
            double cd = _d.Cooldown > 0 ? _d.Cooldown : _sp.Cooldown(_d.SpellId);
            _readyAt = _game + Math.Max(0.1, cd);
            _d.OnCast?.Invoke(done);
            return true;
        }

        public void Remove()
        {
            if (_removed) return;
            _removed = true;
            _btn?.Destroy(); _btn = null;
            try { _sp.SetAuto(_d.SpellId, true); } catch { /* боя нет */ }
        }
    }

    public static class SpellTable
    {
        /// <summary>ID заклинания → часть имени: флаг Is{имя}_P и корутина Play_{имя} у героя.</summary>
        public static readonly Dictionary<int, string> Names = new Dictionary<int, string>
        {
            [1] = "MagicBullet", [2] = "Fireball", [5] = "IceNova", [7] = "ThunderA", [8] = "ElectricZone", [9] = "Tsunami", [10] = "Meteor",
            [12] = "Tornado", [13] = "Electric", [14] = "Armageddon", [15] = "Incinerate", [16] = "EnergyBullet", [17] = "Blizzard",
            [18] = "ArcaneRay", [19] = "MagicCircle", [20] = "LavaField", [21] = "FlashShock",
        };
        private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly Dictionary<int, (PropertyInfo flag, MethodInfo play)> Cache = new Dictionary<int, (PropertyInfo, MethodInfo)>();

        public static (PropertyInfo flag, MethodInfo play) Find(int id)
        {
            if (Cache.TryGetValue(id, out var c)) return c;
            PropertyInfo f = null; MethodInfo m = null;
            if (Names.TryGetValue(id, out var n))
            {
                var t = typeof(C_Battle_AllyUnit);
                f = t.GetProperty($"Is{n}_P", All);
                m = t.GetMethod($"Play_{n}", All, null, Type.EmptyTypes, null);
            }
            return Cache[id] = (f != null && f.CanWrite ? f : null, m);
        }
    }

    internal class SpellApi : ISpells
    {
        private static C_Battle_AllyUnit _hero;
        private static string _heroKey = "";
        private static int _logs, _castFails;
        private readonly string _owner;
        private readonly Dictionary<int, bool> _muted = new Dictionary<int, bool>();    // id -> флаг был поднят до отключения

        private readonly Action<double, Action> _every;

        public SpellApi(string owner, List<Action> cleanups, Action<double, Action> every)
        {
            _owner = owner; _every = every;
            Effects.AddPass(owner, h => { _hero = h.Raw; Remute(h.Raw); }, cleanups);     // проход нужен, чтобы знать героя нового боя
            // игра при каждом StartInformation_B сама сбрасывает флаги (уровень 0) и запускает корутины (уровень >= 1): глушим заново после неё
            Hooks.AddAfter(owner, "A_MainMenuInformation", "Update", 0, (self, a) => _menuAt = Clock.Elapsed.TotalSeconds, cleanups);
            Hooks.AddAfter(owner, "C_Battle_AllyUnit", "StartInformation_B", 0, (self, a) =>
            {
                if (_muted.Count == 0) return;
                var hero = (C_Battle_AllyUnit)self;
                foreach (var id in _muted.Keys.ToList()) Mute(hero, id);
            }, cleanups);
            cleanups.Add(() => { foreach (var a in _owned) a.Remove(); });
            cleanups.Add(RestoreAll);
        }

        private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();
        private static double _menuAt = -100;        // когда в последний раз работал Update главного меню (он идёт только пока меню активно)

        public bool InBattle => InBattleNow;
        internal static bool HeroKnown => _hero != null;
        internal static bool InBattleNow
        {
            get
            {
                if (Clock.Elapsed.TotalSeconds - _menuAt < 0.7) return false;
                try { if (C_Battle_AllyUnit.Unit_Hp <= 0) return false; } catch { /* нет поля */ }      // герой мёртв (экран «возродиться или умереть»)
                try { var h = _hero; if (h == null) return false; var go = h.gameObject; return go != null && go.activeInHierarchy; } catch { return false; }
            }
        }

        public IActiveSpell AddActive(ActiveSpellDef def)
        {
            if (!CanCast(def.SpellId)) throw new Exception($"заклинание {def.SpellId}: нет корутины для ручного каста");
            var a = new ActiveSpell(this, def, _owner);
            a.Start(_every);
            _owned.Add(a);
            return a;
        }
        private readonly List<ActiveSpell> _owned = new List<ActiveSpell>();

        public bool Paused => PausedNow();

        public int Level(int id)
        {
            try { var l = Z_Database.List_AllyMagicLevel; return id >= 0 && id < l.Count ? l[id] : 0; } catch { return 0; }
        }

        public bool CanCast(int id) { var f = SpellTable.Find(id); return f.flag != null && f.play != null; }

        private static string Name(int id) => SpellTable.Names[id];

        /// <summary>Новый бой: поля героя созданы заново, отключение нужно поставить снова (до получения заклинания — просто флаг, после — ещё и остановить корутину).</summary>
        private void Remute(C_Battle_AllyUnit hero)
        {
            var key = hero.Pointer.ToString();
            if (key == _heroKey) return;
            _heroKey = key;
            foreach (var id in _muted.Keys.ToList()) Mute(hero, id);
        }

        private void Mute(C_Battle_AllyUnit hero, int id)
        {
            var f = SpellTable.Find(id);
            if (f.flag == null) return;
            bool was = (bool)f.flag.GetValue(hero);
            if (was && Level(id) >= 1) hero.StopCoroutine("Play_" + Name(id));
            f.flag.SetValue(hero, true);
            if (_logs++ < 40) Log.Write($"[{_owner}] авто-каст {Name(id)} отключён (флаг был {was}, уровень {Level(id)})");
        }

        public void SetAuto(int id, bool auto)
        {
            var f = SpellTable.Find(id);
            if (f.flag == null) throw new Exception($"заклинание {id}: нет флага автокаста");
            var hero = _hero;
            if (!auto)
            {
                if (_muted.ContainsKey(id)) return;
                _muted[id] = hero != null && (bool)f.flag.GetValue(hero);
                if (hero != null) Mute(hero, id);
            }
            else if (_muted.Remove(id) && hero != null)
            {
                f.flag.SetValue(hero, false);
                if (Level(id) >= 1) { hero.StartCoroutine("Play_" + Name(id)); f.flag.SetValue(hero, true); }   // вернуть работу авто-каста
                Log.Write($"[{_owner}] авто-каст {Name(id)} возвращён (уровень {Level(id)})");
            }
        }

        private static bool _pauseLogged;
        /// <summary>Пауза боя: игра замораживает время (Time.timeScale = 0) или поднят флаг IsGamePaused (приложение свёрнуто).</summary>
        internal static bool PausedNow()
        {
            bool flag = false, scale = false;
            try { flag = C_Battle_Manager.IsGamePaused; } catch { }
            try { scale = UnityEngine.Time.timeScale < 0.001f; } catch { }
            if ((flag || scale) && !_pauseLogged) { _pauseLogged = true; Log.Write($"[spells] пауза обнаружена: IsGamePaused={flag}, timeScale=0:{scale}"); }
            return flag || scale;
        }

        public double Cooldown(int id)
        {
            var hero = _hero;
            if (hero == null || !SpellTable.Names.TryGetValue(id, out var n)) return 0;
            foreach (var name in new[] { $"UnitApply_{n}PSpeed", $"UnitApply_{n.TrimEnd('A')}PSpeed" })
            {
                var p = typeof(C_Battle_AllyUnit).GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (p != null) try { return Convert.ToDouble(p.GetValue(hero)); } catch { }
            }
            return 0;
        }

        public bool Cast(int id, int steps = 2)
        {
            var hero = _hero;
            var f = SpellTable.Find(id);
            if (hero == null || f.play == null || Level(id) < 1) return false;
            if (PausedNow()) return false;                                                                          // на паузе не кастуем
            try
            {
                var it = f.play.Invoke(hero, null);
                if (it == null) return false;
                var move = it.GetType().GetMethod("MoveNext");
                for (int i = 0; i < Math.Max(1, steps); i++) if (!(bool)move.Invoke(it, null)) break;
                return true;
            }
            catch (Exception e)
            {
                // корутина игры бросает исключение, когда нет цели (пустой список врагов): это «каст не вышел», а не ошибка мода
                if (_castFails++ < 5) Log.Write($"[{_owner}] каст {Name(id)} не вышел: {(e.InnerException ?? e).Message}");
                return false;
            }
        }

        private void RestoreAll()
        {
            foreach (var id in _muted.Keys.ToList()) { try { SetAuto(id, true); } catch { /* боя нет */ } }
        }
    }
}
