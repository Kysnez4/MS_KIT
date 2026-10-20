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
    // ------------------------------------------------------------ Hero

    /// <summary>Здоровье героя и простой щит. Щит поглощает урон: после Unit_Damage потерянное здоровье возвращается за счёт щита.</summary>
    public interface IHero
    {
        double Hp { get; }
        double HpMax { get; }
        /// <summary>Лечит на amount, не выше максимума. Возвращает, сколько вылечено.</summary>
        double Heal(double amount);
        /// <summary>Очки щита мода (общие для всех модов). Добавление не ограничено.</summary>
        double Shield { get; }
        void AddShield(double amount);
    }

    internal static class HeroApi
    {
        private static double _shield, _hpBefore;
        private static bool _hooked;
        private static readonly List<Action> Cleanups = new List<Action>();

        private static void EnsureHooks()
        {
            if (_hooked) return;
            _hooked = true;
            try
            {
                Hooks.AddBefore("hero", "C_Battle_Manager", "Start", 0, (s, a) => _shield = 0, Cleanups);
                Hooks.AddBefore("hero", "C_Battle_AllyUnit", "Unit_Damage", 3, (s, a) => _hpBefore = C_Battle_AllyUnit.Unit_Hp, Cleanups);
                Hooks.AddAfter("hero", "C_Battle_AllyUnit", "Unit_Damage", 3, (s, a) =>
                {
                    if (_shield <= 0) return;
                    double lost = _hpBefore - C_Battle_AllyUnit.Unit_Hp;
                    if (lost <= 0) return;
                    double take = Math.Min(lost, _shield);
                    _shield -= take;
                    C_Battle_AllyUnit.Unit_Hp = (float)(C_Battle_AllyUnit.Unit_Hp + take);
                }, Cleanups);
            }
            catch (Exception e) { Log.Write("hero: хуки: " + e.Message); _hooked = false; }
        }

        private class Hero : IHero
        {
            public double Hp => C_Battle_AllyUnit.Unit_Hp;
            public double HpMax => C_Battle_AllyUnit.UnitApply_HpMax;
            public double Heal(double amount)
            {
                double cur = Hp, max = HpMax, add = Math.Max(0, Math.Min(amount, max - cur));
                if (add > 0) C_Battle_AllyUnit.Unit_Hp = (float)(cur + add);
                return add;
            }
            public double Shield => _shield;
            public void AddShield(double a) { _shield = Math.Max(0, _shield + a); }
        }

        internal static void ResetBattle() { _shield = 0; }
        internal static IHero Instance() { EnsureHooks(); return new Hero(); }
    }
}
