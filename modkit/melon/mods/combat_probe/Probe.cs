using System;
using System.Linq;
using ModKit;

namespace CombatProbe
{
    public class ProbeMod : IKitMod
    {
        public ModDef Define() => new ModDef { Id = "combat_probe", Name = "Проба боевого API", Version = "1.4", DefaultEnabled = true, OnEnable = Enable };

        private static void Enable(ModContext ctx)
        {
            var hero = ctx.Use<IHero>("modkit_hero");
            var spells = ctx.Use<ISpells>("modkit_spells");
            var enemies = ctx.Use<IEnemies>("modkit_combat");
            var effects = ctx.Use<IEffects>("modkit_effects");
            var combat = ctx.Use<ICombat>("modkit_combat");
            var zones = ctx.Use<IZones>("modkit_zones");
            var game = ctx.Use<IGame>("modkit_game");
            int dmgEv = 0, manaEv = 0, itemEv = 0, deaths = 0, cycle = 0;
            ctx.Events.On("heroDamaged", o => dmgEv++);
            ctx.Events.On("manaPickup", o => manaEv++);
            ctx.Events.On("itemPickup", o => itemEv++);
            ctx.Events.On("enemyDeath", o => deaths++);
            var res = ctx.Use<IResources>("modkit_resources").Create("hope", 100, 5);
            res.ShowBar("Надежда", 20, 300, 300, 28, new[] { 0.2, 0.5, 0.95, 0.9 });
            int runs = ctx.Store.Get("runs", 0);
            ctx.Log($"store: запусков раньше {runs}");
            ctx.Store.Set("runs", runs + 1);
            ctx.Timer.Every(4, () => ctx.Guard("проба", () =>
            {
                bool ib = spells.InBattle; ctx.Log($"тик: бой={ib}, время {ctx.Timer.Now:0.0}"); if (!ib) return;
                cycle++;
                var all = enemies.Alive; var hp = enemies.HeroPos;
                ctx.Log($"#{cycle} врагов {all.Count}, герой {(hp.HasValue ? hp.Value.ToString() : "нет")}, HP {hero.Hp:0}/{hero.HpMax:0}, событий: урон {dmgEv} мана {manaEv} предметы {itemEv} смертей {deaths}, ресурс {res.Value:0}");
                if (!hp.HasValue || all.Count == 0) return;
                var e = enemies.Nearest(hp.Value.x, hp.Value.y);
                if (e == null) return;
                double h0 = e.Hp; var p0 = e.Pos;
                bool ok = combat.Damage(e, 8);
                ctx.Log($"  Damage(8) ok={ok} HP врага {h0:0} -> {e.Hp:0} (id {e.Id}, жив {e.Alive})");
                if (!e.Alive) return;
                ctx.Log($"  Freeze ok={combat.Freeze(e, 1.5)}, Stun ok={combat.Stun(e, 0.5)}");
                combat.Pull(e, hp.Value.x, hp.Value.y, 1.0);
                ctx.Log($"  Pull: {p0} -> {e.Pos}");
                ctx.Log($"  near(3)={enemies.Near(hp.Value.x, hp.Value.y, 3).Count}, near(8)={enemies.Near(hp.Value.x, hp.Value.y, 8).Count}");
                int hits = 0;
                zones.Create(hp.Value.x, hp.Value.y, 6, 3, 1, list => { hits += list.Count; foreach (var z in list) combat.Damage(z, 5); });
                ctx.Timer.After(3.5, () => ctx.Log($"  зона: суммарно задето {hits}"));
                ctx.Log($"  Heal(10)={hero.Heal(10):0.#}, AddShield(20) -> {hero.Shield + 20}"); hero.AddShield(20);
                ctx.Log($"  Resource spend 10 -> {res.TrySpend(10)}, осталось {res.Value:0}");
                if (cycle % 5 == 0) ctx.Log($"  Recalc ok={effects.Recalc()}, артефактов {game.Artifacts.Count}");
            }));
        }
    }
}
