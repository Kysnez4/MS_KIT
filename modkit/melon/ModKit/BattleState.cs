using System;
using System.Collections.Generic;
using Il2Cpp;

namespace ModKit
{
    /// <summary>
    /// Где сейчас игра: идёт ли бой, на паузе ли он. Нужно ядру (игровые таймеры, экран) и библиотекам. Раньше жило в SpellApi.
    /// Героя ядро запоминает само (хук StartInformation_B); библиотека эффектов дополнительно сообщает его через <see cref="SetHero"/> при каждом проходе.
    /// </summary>
    public static class BattleState
    {
        private static C_Battle_AllyUnit _hero;
        private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();
        private static double _menuAt = -100;        // когда в последний раз работал Update главного меню (он идёт только пока меню активно)
        private static bool _pauseLogged;
        private static readonly List<Action> Hooked = new List<Action>();

        /// <summary>Герой текущего боя (null — ещё не известен).</summary>
        public static C_Battle_AllyUnit Hero => _hero;
        public static bool HeroKnown => _hero != null;
        public static void SetHero(C_Battle_AllyUnit hero) { if (hero != null) _hero = hero; }

        /// <summary>Вызывается из Update главного меню: пока он идёт, боя нет.</summary>
        internal static void MenuTick() => _menuAt = Clock.Elapsed.TotalSeconds;

        /// <summary>Ставит хук запоминания героя. Вызывается один раз при старте загрузчика.</summary>
        internal static void Install()
        {
            try { Hooks.AddAfter("modkit", "C_Battle_AllyUnit", "StartInformation_B", 0, (self, a) => _hero = (C_Battle_AllyUnit)self, Hooked); }
            catch (Exception e) { Log.Write("BattleState: хук героя не поставлен: " + e.Message); }
        }

        /// <summary>Бой идёт прямо сейчас: не главное меню, герой жив и активен.</summary>
        public static bool InBattle
        {
            get
            {
                if (Clock.Elapsed.TotalSeconds - _menuAt < 0.7) return false;
                try { if (C_Battle_AllyUnit.Unit_Hp <= 0) return false; } catch { /* нет поля */ }      // герой мёртв (экран «возродиться или умереть»)
                try { var h = _hero; if (h == null) return false; var go = h.gameObject; return go != null && go.activeInHierarchy; } catch { return false; }
            }
        }

        /// <summary>Пауза боя: игра замораживает время (Time.timeScale = 0) или поднят флаг IsGamePaused (приложение свёрнуто).</summary>
        public static bool Paused
        {
            get
            {
                bool flag = false, scale = false;
                try { flag = C_Battle_Manager.IsGamePaused; } catch { }
                try { scale = UnityEngine.Time.timeScale < 0.001f; } catch { }
                if ((flag || scale) && !_pauseLogged) { _pauseLogged = true; Log.Write($"[core] пауза обнаружена: IsGamePaused={flag}, timeScale=0:{scale}"); }
                return flag || scale;
            }
        }
    }
}
