using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Il2Cpp;
using UnityEngine;

namespace ModKit
{
    // ------------------------------------------------------------ Триггеры

    /// <summary>Готовые условия-события для «событийных эффектов» (раньше каждый мод считал их сам). Счётчики сбрасываются на battleStart.</summary>
    public interface ITriggers
    {
        /// <summary>Каждые every убийств врагов: fn(всего убийств за бой).</summary>
        void OnKills(int every, Action<int> fn);
        /// <summary>Герой получил урон: fn(величина урона).</summary>
        void OnHeroDamaged(Action<double> fn);
        /// <summary>Здоровье героя упало ниже доли frac (0..1); повторно не чаще, чем раз в cooldown секунд игрового времени.</summary>
        void OnLowHp(double frac, double cooldown, Action fn);
        /// <summary>Каждые every подобранных шариков маны.</summary>
        void OnManaPickups(int every, Action fn);
    }

    internal class TriggersImpl : ITriggers
    {
        private readonly ModContext _c; private readonly IHero _hero;
        public TriggersImpl(ModContext c) { _c = c; _hero = c.Use<IHero>("modkit_hero"); }

        public void OnKills(int every, Action<int> fn)
        {
            int n = 0; every = Math.Max(1, every);
            _c.Events.On("battleStart", _ => n = 0);
            _c.Events.On("enemyDeath", _ => { n++; if (n % every == 0) _c.Guard("триггер убийств", () => fn(n)); });
        }

        public void OnHeroDamaged(Action<double> fn)
            => _c.Events.On("heroDamaged", o => { var d = o as EventData; _c.Guard("триггер урона", () => fn(d?.Get<double>("amount") ?? 0)); });

        public void OnLowHp(double frac, double cooldown, Action fn)
        {
            double next = 0;
            _c.Timer.Every(0.5, () =>
            {
                if (!BattleState.InBattle || _c.Timer.Now < next) return;
                double max = _hero.HpMax;
                if (max > 0 && _hero.Hp / max < frac) { next = _c.Timer.Now + cooldown; _c.Guard("триггер малого HP", fn); }
            });
        }

        public void OnManaPickups(int every, Action fn)
        {
            int n = 0; every = Math.Max(1, every);
            _c.Events.On("battleStart", _ => n = 0);
            _c.Events.On("manaPickup", _ => { n++; if (n % every == 0) _c.Guard("триггер маны", fn); });
        }
    }
}
