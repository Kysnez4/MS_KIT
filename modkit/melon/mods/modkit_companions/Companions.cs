using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Il2Cpp;
using UnityEngine;

namespace ModKit
{
    /// <summary>Поведение союзника.</summary>
    public enum CompanionMode
    {
        /// <summary>Держится рядом с героем в точке Offset (плавно, со скоростью Speed).</summary>
        Follow,
        /// <summary>Охраняет: кружит возле героя, а когда враг подходит ближе GuardRadius к герою, встаёт между героем и ближайшим врагом.</summary>
        Guard,
        /// <summary>Охотится: идёт к самому «жирному» врагу (много HP, но не слишком далеко) в пределах Leash от героя и держится на StandOff от него.</summary>
        Hunt,
    }

    /// <summary>Союзник на основе «двойника» героя: готовый спрайт игры (копия героя), следует за героем и стреляет его пулями.</summary>
    public interface ICompanion
    {
        /// <summary>Слот двойника 1..9.</summary>
        int Slot { get; }
        /// <summary>Смещение от героя в мировых единицах.</summary>
        Vector2 Offset { get; set; }
        bool Alive { get; }
        /// <summary>Заклинание союзника (ID Ability, см. SpellTable: 1 пуля, 2 огненный шар, 5 ледяные иглы, 7 молния…); 0 — стрельба двойника игры. Героя учить заклинанию не нужно.</summary>
        int Spell { get; set; }
        CompanionMode Mode { get; set; }
        /// <summary>Положение союзника (x, y) в мире.</summary>
        Vector2 Pos { get; }
        /// <summary>Скорость, единиц в секунду (по умолчанию 8).</summary>
        double Speed { get; set; }
        /// <summary>Как далеко от героя можно отходить (Hunt/Guard), единиц (по умолчанию 9).</summary>
        double Leash { get; set; }
        /// <summary>Дистанция до цели при охоте (по умолчанию 2).</summary>
        double StandOff { get; set; }
        /// <summary>Радиус вокруг героя, в котором Guard считает врага угрозой (по умолчанию 5).</summary>
        double GuardRadius { get; set; }
        /// <summary>Убирает союзника: останавливает стрельбу, прячет объект.</summary>
        void Remove();
    }

    /// <summary>
    /// Союзники на двойниках героя (9 готовых объектов Obj_UnitDoppelganger01..09). Двойник копирует спрайт героя, окрашивается и стреляет
    /// корутиной игры Play_Doppelganger_Attack: это стрельба Магической пулей героя (её Num/ShotNum/TargetNum и шанс RandomRan подмены снаряда
    /// на Огненный шар, Энергопулю, Духа или Молнию — см. docs/COMPANIONS.md). Слоты берутся с 9-го вниз, чтобы не мешать собственным двойникам игры.
    /// </summary>
    public interface ICompanions
    {
        int MaxSlots { get; }
        IReadOnlyList<ICompanion> Active { get; }
        /// <summary>
        /// Создаёт союзника со смещением (dx, dy) и одним заклинанием spellId: он сам бьёт ближайшего врага снарядом этого заклинания с перезарядкой героя.
        /// spellId 0 — стрельба двойника игры (Магическая пуля с подменой RandomRan). color — RGBA 0..1 (по умолчанию голубой), scale — размер. null — нет слота или боя нет.
        /// </summary>
        ICompanion Spawn(double dx, double dy, int spellId = 0, double[] color = null, double scale = 0.7, CompanionMode mode = CompanionMode.Follow);
        /// <summary>Убирает всех союзников этого мода.</summary>
        void Clear();
    }

    internal class CompanionsImpl : ICompanions
    {
        private static readonly BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly HashSet<int> Taken = new HashSet<int>();
        private readonly List<Comp> _list = new List<Comp>();
        private readonly Action<string, Action> _guard;
        private readonly IGameTimer _timer; private readonly IEnemies _enemies; private readonly Func<int, double> _cooldown;
        private static int _fails;

        public CompanionsImpl(Action<double, Action> every, Action<string, Action> guard, IGameTimer timer, IEnemies enemies, Func<int, double> cooldown, List<Action> cleanups)
        {
            _guard = guard; _timer = timer; _enemies = enemies; _cooldown = cooldown;
            every(50, () => _guard("союзники", Tick));
            cleanups.Add(Clear);
        }

        public int MaxSlots => 9;
        public IReadOnlyList<ICompanion> Active => _list.Where(c => c.Alive).ToList<ICompanion>();

        private static T Prop<T>(C_Battle_AllyUnit h, string name) where T : class
            => typeof(C_Battle_AllyUnit).GetProperty(name, All)?.GetValue(h) as T;

        private class Comp : ICompanion
        {
            public int Slot { get; set; }
            public Vector2 Offset { get; set; }
            public bool Alive { get; set; } = true;
            public GameObject Obj; public Transform Tr; public SpriteRenderer Sr;
            public Color Col; public float Scale; public bool Attack;
            public int Spell { get; set; }
            public CompanionMode Mode { get; set; }
            public double Speed { get; set; } = 8;
            public double Leash { get; set; } = 9;
            public double StandOff { get; set; } = 2;
            public double GuardRadius { get; set; } = 5;
            public double NextShot, NextThink, Angle;
            public Vector2 Pos { get; set; } public Vector2? Goal; public IEnemy Target;
            public Coroutine AttackCo;
            public Action<Comp> OnRemove;
            public void Remove() { if (Alive) OnRemove(this); }
        }

        public ICompanion Spawn(double dx, double dy, int spellId = 0, double[] color = null, double scale = 0.7, CompanionMode mode = CompanionMode.Follow)
        {
            var hero = CombatApi.Hero; if (hero == null || !BattleState.InBattle) { Log.Write($"[companions] нет боя: герой {(hero == null ? "нет" : "есть")}, бой {BattleState.InBattle}"); return null; }
            bool gameOwns = false; try { gameOwns = hero.IsDoppelganger_P; } catch { }
            for (int slot = 9; slot >= 1; slot--)
            {
                if (Taken.Contains(slot)) continue;
                string n = "0" + slot;
                var go = Prop<GameObject>(hero, "Obj_UnitDoppelganger" + n);
                var sr = Prop<SpriteRenderer>(hero, "SpriteRenderer_Doppelganger" + n);
                var tr = Prop<Transform>(hero, "Transform_ObjUnitDoppelganger" + n);
                if (slot == 9 || slot == 1) Log.Write($"[companions] слот {slot}: объект {(go != null)}, спрайт {(sr != null)}, трансформ {(tr != null)}, activeSelf {go?.activeSelf}, IsDoppelganger_P {gameOwns}");
                if (go == null || sr == null || tr == null || (go.activeSelf && gameOwns)) continue;     // слот занят самой игрой
                var c = new Comp
                {
                    Slot = slot, Offset = new Vector2((float)dx, (float)dy), Obj = go, Tr = tr, Sr = sr, Spell = spellId, Mode = mode, Attack = spellId == 0, Scale = (float)scale,
                    Col = color == null ? new Color(0.45f, 0.65f, 1f, 0.7f) : new Color((float)color[0], (float)color[1], (float)color[2], color.Length > 3 ? (float)color[3] : 1f),
                    OnRemove = Kill
                };
                { var hp0 = hero.Transform_ObjUnit.position; c.Pos = new Vector2(hp0.x + c.Offset.x, hp0.y + c.Offset.y); c.Angle = slot; }
                Taken.Add(slot);
                go.SetActive(true);
                tr.localScale = new Vector3(c.Scale, c.Scale, 1);
                Place(hero, c, 0);
                Log.Write($"[companions] слот {slot}: родитель «{tr.parent?.name}», позиция {tr.position}, масштаб героя {hero.Transform_ObjUnit?.localScale}");
                if (c.Attack) c.AttackCo = hero.StartCoroutine(hero.Play_Doppelganger_Attack(tr));
                _list.Add(c);
                return c;
            }
            return null;
        }

        private double _last;

        private void Place(C_Battle_AllyUnit hero, Comp c, double dt)
        {
            var hp = hero.Transform_ObjUnit.position;
            var heroXY = new Vector2(hp.x, hp.y);
            Think(heroXY, c);
            var goal = c.Goal ?? heroXY + c.Offset;
            var d = goal - c.Pos; float len = d.magnitude;
            float step = (float)(c.Speed * dt);
            c.Pos = len <= step || len < 1e-4f ? goal : c.Pos + d / len * step;
            // поводок: дальше Leash от героя не уходим (Follow: Offset может быть больше — тогда поводок не ограничивает)
            double leash = Math.Max(c.Leash, c.Offset.magnitude + 0.5);
            var fromHero = c.Pos - heroXY;
            if (fromHero.magnitude > leash) c.Pos = heroXY + fromHero.normalized * (float)leash;
            c.Tr.position = new Vector3(c.Pos.x, c.Pos.y, hp.z);
            var src = hero.Motion_Unit;
            if (src != null && c.Sr != null) { c.Sr.sprite = src.sprite; c.Sr.flipX = d.x < -0.01f ? true : d.x > 0.01f ? false : src.flipX; c.Sr.color = c.Col; }
        }

        /// <summary>Выбор цели движения (не чаще 3 раз в секунду).</summary>
        private void Think(Vector2 hero, Comp c)
        {
            if (c.Mode == CompanionMode.Follow) { c.Goal = null; return; }
            double now = _timer.Now;
            c.Angle += 0.02;                                                  // медленный круг вокруг героя
            var orbit = hero + new Vector2((float)Math.Cos(c.Angle), (float)Math.Sin(c.Angle)) * 2.2f;
            if (now < c.NextThink) { if (c.Mode == CompanionMode.Guard && c.Target == null) c.Goal = orbit; return; }
            c.NextThink = now + 0.33;
            if (c.Mode == CompanionMode.Guard)
            {
                var near = _enemies.Nearest(hero.x, hero.y);
                if (near != null && Vector2.Distance(new Vector2(near.Pos.x, near.Pos.y), hero) <= c.GuardRadius)
                {
                    c.Target = near;                                           // встаём между героем и врагом
                    var ep = new Vector2(near.Pos.x, near.Pos.y);
                    var dir = (ep - hero); float dl = dir.magnitude;
                    c.Goal = dl < 0.01f ? orbit : hero + dir / dl * (float)Math.Min(dl * 0.6, c.Leash);
                }
                else { c.Target = null; c.Goal = orbit; }
                return;
            }
            // Hunt: сильный и здоровый — много HP, ближе — лучше
            IEnemy best = null; double bs = -1;
            foreach (var e in _enemies.Near(hero.x, hero.y, c.Leash + c.StandOff))
            {
                double dist = Vector2.Distance(new Vector2(e.Pos.x, e.Pos.y), c.Pos);
                double score = e.Hp / (1.0 + 0.15 * dist);
                if (score > bs) { bs = score; best = e; }
            }
            c.Target = best;
            if (best == null) { c.Goal = orbit; return; }
            var bp = new Vector2(best.Pos.x, best.Pos.y);
            var back = c.Pos - bp; float bl = back.magnitude;
            c.Goal = bl < 0.01f ? bp : bp + back / bl * (float)c.StandOff;      // не вплотную, а на дистанции StandOff
        }

        private void Tick()
        {
            var hero = CombatApi.Hero;
            bool battle = hero != null && BattleState.InBattle;
            double nowT = _timer.Now, dt = Math.Min(0.25, Math.Max(0, nowT - _last)); _last = nowT;
            foreach (var c in _list.ToArray())
            {
                if (!c.Alive) continue;
                if (!battle) { Kill(c); continue; }                         // бой кончился: объекты героя пересоздаются
                if (!c.Obj.activeSelf) { c.Obj.SetActive(true); if (c.Attack) c.AttackCo = hero.StartCoroutine(hero.Play_Doppelganger_Attack(c.Tr)); }
                Place(hero, c, dt);
                if (c.Spell > 0) Fire(hero, c);
            }
            _list.RemoveAll(c => !c.Alive);
        }

        private static readonly BindingFlags All2 = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        private static double Num(C_Battle_AllyUnit h, string name, double def)
        {
            try { var p = typeof(C_Battle_AllyUnit).GetProperty(name, All2); return p == null ? def : Convert.ToDouble(p.GetValue(h)); } catch { return def; }
        }

        /// <summary>Свой цикл стрельбы союзника: ближайший враг в радиусе стрельбы героя, снаряд заклинания, перезарядка как у заклинания героя.</summary>
        private void Fire(C_Battle_AllyUnit hero, Comp c)
        {
            if (_timer.Now < c.NextShot || BattleState.Paused) return;
            if (!SpellTable.Names.TryGetValue(c.Spell, out var name)) { if (_fails++ < 3) Log.Write($"[companions] заклинание {c.Spell} нет в таблице"); c.NextShot = _timer.Now + 5; return; }
            var pos = c.Tr.position;
            double range = Num(hero, "UnitApply_ShootingRange", 12);
            var e = _enemies.Nearest(pos.x, pos.y);
            double cd = Math.Max(0.4, _cooldown(c.Spell));
            if (cd < 0.4 || cd > 30) cd = 1.5;
            if (e == null || Math.Sqrt((e.Pos.x - pos.x) * (e.Pos.x - pos.x) + (e.Pos.y - pos.y) * (e.Pos.y - pos.y)) > range) { c.NextShot = _timer.Now + 0.3; return; }
            int bullet = 0;
            foreach (var n in new[] { name, name.TrimEnd('A') })
                if (bullet == 0) bullet = (int)Num(hero, "BulletID_" + n, 0);
            if (bullet == 0) { if (_fails++ < 3) Log.Write($"[companions] нет BulletID для {name}"); c.NextShot = _timer.Now + 5; return; }
            int count = Math.Max(1, (int)Math.Round(Num(hero, $"UnitApply_{name}Num", 1)));
            if (count > 5) count = 5;
            var ep = e.Pos;
            for (int i = 0; i < count; i++)
                hero.Bullet_Create(c.Tr, bullet, ep.x, ep.y, ep.z, i * 0.5f, 1f, 0f, 0f, 0f, 0f, 0f, 1f);
            c.NextShot = _timer.Now + cd;
        }

        private void Kill(Comp c)
        {
            c.Alive = false; Taken.Remove(c.Slot);
            try { var h = CombatApi.Hero; if (h != null && c.AttackCo != null) h.StopCoroutine(c.AttackCo); } catch { }
            try { if (c.Obj != null) c.Obj.SetActive(false); } catch { }
        }

        public void Clear() { foreach (var c in _list.ToArray()) Kill(c); _list.Clear(); }
    }
}
