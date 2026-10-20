using System;
using System.Collections.Generic;
using System.Linq;
using Il2Cpp;
using UnityEngine;

namespace ModKit
{
    /// <summary>Живой враг боя. Объекты врагов в игре переиспользуются (пул), поэтому держать ссылку между боями нельзя.</summary>
    public interface IEnemy
    {
        /// <summary>ID типа врага (строка таблицы врагов).</summary>
        int Id { get; }
        double Hp { get; }
        /// <summary>Положение в мире (x, y; z — как у объекта).</summary>
        Vector3 Pos { get; }
        bool Alive { get; }
        /// <summary>Il2Cpp-объект C_Battle_EnemyUnit.</summary>
        object Raw { get; }
    }

    /// <summary>Враги боя: поиск по расстоянию. Список ведётся хуками Start_Unit / Unit_Death, обход кучи не используется.</summary>
    public interface IEnemies
    {
        /// <summary>Все живые враги сейчас.</summary>
        IReadOnlyList<IEnemy> Alive { get; }
        /// <summary>Враги в радиусе r от точки (x, y), ближайшие первыми.</summary>
        IReadOnlyList<IEnemy> Near(double x, double y, double r);
        /// <summary>Ближайший к точке враг или null.</summary>
        IEnemy Nearest(double x, double y);
        /// <summary>Положение героя (x, y) или null, если боя нет.</summary>
        Vector3? HeroPos { get; }
    }

    /// <summary>Действия над врагами и снарядами. Все методы возвращают false, если боя нет или цель мертва.</summary>
    public interface ICombat
    {
        /// <summary>Урон врагу. magicId — ID заклинания игры для учёта в статистике (0 — без учёта), criRan — шанс крита, %.</summary>
        bool Damage(IEnemy e, double amount, int magicId = 0, int criRan = 0, double criMultiplier = 1);
        bool Freeze(IEnemy e, double seconds);
        bool Stun(IEnemy e, double seconds);
        /// <summary>Сдвигает врага на dist к точке (x, y) (отрицательное — от неё). Игра двигает врагов сама, сдвиг разовый.</summary>
        bool Pull(IEnemy e, double x, double y, double dist);
        /// <summary>Снаряд игры bulletId из положения героя в точку (x, y, z); ratio — множитель урона. Урон и крит считает снаряд по статам героя.</summary>
        bool Shoot(int bulletId, double x, double y, double z = 0, double ratio = 1);
    }

    public static class CombatApi
    {
        private static readonly HashSet<C_Battle_EnemyUnit> Live = new HashSet<C_Battle_EnemyUnit>();
        private static readonly List<Action> Cleanups = new List<Action>();   // хуки ядра живут до выхода из игры
        private static C_Battle_AllyUnit _hero;
        /// <summary>Герой боя: из прохода по герою, а если прохода ещё не было — поиск по сцене (один раз).</summary>
        public static C_Battle_AllyUnit Hero
        {
            get
            {
                if (_hero == null)
                {
                    try { foreach (var o in UnityEngine.Object.FindObjectsOfType(Il2CppInterop.Runtime.Il2CppType.Of<C_Battle_AllyUnit>())) { _hero = o.TryCast<C_Battle_AllyUnit>(); break; } }
                    catch { }
                }
                return _hero;
            }
        }
        private static bool _hooked;

        private class Enemy : IEnemy
        {
            private readonly C_Battle_EnemyUnit _e;
            public Enemy(C_Battle_EnemyUnit e) { _e = e; }
            public int Id { get { try { return _e.UnitID; } catch { return 0; } } }
            public double Hp { get { try { return _e.Unit_Hp; } catch { return 0; } } }
            public Vector3 Pos { get { try { return _e.Transform_ObjUnit.position; } catch { return Vector3.zero; } } }
            public bool Alive { get { try { return _e != null && Live.Contains(_e) && _e.gameObject.activeInHierarchy && _e.Unit_Hp > 0; } catch { return false; } } }
            public object Raw => _e;
            internal C_Battle_EnemyUnit E => _e;
        }

        private static void EnsureHooks()
        {
            if (_hooked) return;
            _hooked = true;
            try
            {
                Hooks.AddAfter("combat", "C_Battle_EnemyUnit", "Start_Unit", 3, (s, a) => Live.Add((C_Battle_EnemyUnit)s), Cleanups);
                // враг может уйти не только через Unit_Death: снимаем его и в остальных способах уничтожения (иначе набор растёт — лаги)
                foreach (var m in new[] { "Unit_Death", "Unit_Destroy", "Unit_Destroy_Simple", "Unit_Destroy_Damage", "Unit_Destroy_Ghost", "Unit_Destroy_Armageddon", "Unit_Destroy_DeathBell", "Unit_InstantDeath", "Unit_Disappear_Peace" })
                {
                    try { Hooks.AddBefore("combat", "C_Battle_EnemyUnit", m, 0, (s, a) => Live.Remove((C_Battle_EnemyUnit)s), Cleanups); }
                    catch (Exception e) { Log.Write($"combat: нет метода {m}: {e.Message}"); }
                }
                Hooks.AddBefore("combat", "C_Battle_Manager", "Start", 0, (s, a) => Live.Clear(), Cleanups);
                Effects.AddPass("combat", h => _hero = h.Raw, Cleanups);
            }
            catch (Exception e) { Log.Write("combat: хуки врагов: " + e.Message); _hooked = false; }
        }

        private static C_Battle_EnemyUnit Raw(IEnemy e) => e is Enemy en && en.Alive ? en.E : null;

        private class Enemies : IEnemies
        {
            public IReadOnlyList<IEnemy> Alive
            {
                get
                {
                    var l = new List<IEnemy>();
                    foreach (var e in Live.ToArray())
                    {
                        var w = new Enemy(e);
                        if (w.Alive) l.Add(w); else Live.Remove(e);       // мёртвых и выключенных убираем насовсем
                    }
                    return l;
                }
            }
            public Vector3? HeroPos { get { try { var h = _hero; return h == null ? (Vector3?)null : h.Transform_ObjUnit.position; } catch { return null; } } }
            public IReadOnlyList<IEnemy> Near(double x, double y, double r)
            {
                double r2 = r * r;
                var res = new List<(IEnemy e, double d)>();
                foreach (var e in Live.ToArray())
                {
                    var w = new Enemy(e);
                    if (!w.Alive) { Live.Remove(e); continue; }
                    double d = Sq(w.Pos, x, y);
                    if (d <= r2) res.Add((w, d));
                }
                return res.OrderBy(t => t.d).Select(t => t.e).ToList();
            }
            public IEnemy Nearest(double x, double y)
            {
                IEnemy best = null; double bd = double.MaxValue;
                foreach (var e in Live.ToArray())
                {
                    var w = new Enemy(e);
                    if (!w.Alive) { Live.Remove(e); continue; }
                    double d = Sq(w.Pos, x, y);
                    if (d < bd) { bd = d; best = w; }
                }
                return best;
            }
            private static double Sq(Vector3 p, double x, double y) { double dx = p.x - x, dy = p.y - y; return dx * dx + dy * dy; }
        }

        private class Combat : ICombat
        {
            public bool Damage(IEnemy e, double amount, int magicId = 0, int criRan = 0, double criMultiplier = 1)
            {
                var r = Raw(e); if (r == null) return false;
                try { r.Unit_Damage(magicId, (float)amount, criRan, (float)criMultiplier, 0, 0, 0, 0, 0, 0); return true; }
                catch (Exception ex) { Log.Write("combat: Damage: " + ex.Message); return false; }
            }
            public bool Freeze(IEnemy e, double s) { var r = Raw(e); if (r == null) return false; try { r.Status_Freeze((float)s); return true; } catch { return false; } }
            public bool Stun(IEnemy e, double s) { var r = Raw(e); if (r == null) return false; try { r.Status_Stun((float)s); return true; } catch { return false; } }
            public bool Pull(IEnemy e, double x, double y, double dist)
            {
                var r = Raw(e); if (r == null) return false;
                try
                {
                    var t = r.Transform_ObjUnit; var p = t.position;
                    double dx = x - p.x, dy = y - p.y, len = Math.Sqrt(dx * dx + dy * dy);
                    if (len < 1e-4) return true;
                    double step = Math.Min(dist, len);
                    if (dist < 0) step = dist;
                    t.position = new Vector3((float)(p.x + dx / len * step), (float)(p.y + dy / len * step), p.z);
                    return true;
                }
                catch { return false; }
            }
            public bool Shoot(int bulletId, double x, double y, double z = 0, double ratio = 1)
            {
                var h = _hero; if (h == null) return false;
                try { h.Bullet_Create(h.Transform_ObjUnit, bulletId, (float)x, (float)y, (float)z, 0f, 1f, 0f, 0f, 0f, 0f, 0f, (float)ratio); return true; }
                catch (Exception ex) { Log.Write("combat: Shoot: " + ex.Message); return false; }
            }
        }

        public static IEnemies Enemy_() { EnsureHooks(); return new Enemies(); }
        public static ICombat Combat_() { EnsureHooks(); return new Combat(); }
    }
}
