using System;
using ModKit;

namespace ApiProbe
{
    public class ProbeMod : IKitMod
    {
        public ModDef Define() => new ModDef { Id = "api_probe", Name = "Проба API 2", Version = "1.7", DefaultEnabled = true, OnEnable = Enable };

        private static void Enable(ModContext ctx)
        {
            var triggers = ctx.Use<ITriggers>("modkit_triggers");
            var hero = ctx.Use<IHero>("modkit_hero");
            var research = ctx.Use<IResearch>("modkit_research");
            var spells = ctx.Use<ISpells>("modkit_spells");
            var companions = ctx.Use<ICompanions>("modkit_companions");
            var enemies = ctx.Use<IEnemies>("modkit_combat");
            var effects = ctx.Use<IEffects>("modkit_effects");
            int hits = 0; double dmg = 0;
            var items = new System.Collections.Generic.Dictionary<string, int>();
            ctx.Events.On("itemPickup", o => { var d = o as EventData; string k = $"type={d?.Get<int>("type")}/obj={d?.Get<int>("objType")}"; items[k] = items.TryGetValue(k, out var n) ? n + 1 : 1; if (items[k] == 1) ctx.Log("подбор предмета " + k); });
            triggers.OnKills(100, n => { ctx.Log($"триггер убийств: {n}"); ctx.Toast($"Убийств: {n}", 2); });
            triggers.OnHeroDamaged(a => { hits++; dmg += a; if (hits <= 5) ctx.Log($"герой получил урон {a:0.#} (HP {hero.Hp:0}, щит {hero.Shield:0})"); });
            triggers.OnLowHp(0.8, 15, () => { double h = hero.Heal(50); ctx.Log($"малое HP: вылечено {h:0}, HP {hero.Hp:0}/{hero.HpMax:0}"); ctx.Toast("Надежда!", 2); });
            triggers.OnManaPickups(200, () => ctx.Log("триггер маны: ещё 200 шариков"));

            research.Define("a", 1); research.Define("b", 2, "a");
            research.AddPoints(3);
            ctx.Log($"исследования: очки {research.Points}, a={research.CanUnlock("a")}, b до a={research.CanUnlock("b")}");
            ctx.Log($"unlock a={research.Unlock("a")}, unlock b={research.Unlock("b")}, очки {research.Points}");
            ctx.Log($"ReadAsset(нет файла) = {(ctx.ReadAsset("none.png") == null ? "null" : "есть")}, ReadAsset(mod.json) = {(ctx.ReadAsset("mod.json") == null ? "null" : "есть")}");

            var seen = new System.Collections.Generic.Dictionary<int, int>(); int fromComp = 0, total = 0; bool spawned = false; int attempts = 0;
            ctx.After("C_Battle_AllyUnit", "Bullet_Create", 13, (self, a) =>
            {
                total++;
                var tr = a[0] as UnityEngine.Transform;
                if (tr != null && tr.name.IndexOf("oppel", StringComparison.OrdinalIgnoreCase) >= 0) { fromComp++; int id = Convert.ToInt32(a[1]); seen[id] = seen.TryGetValue(id, out var c) ? c + 1 : 1; }
            });
            ctx.Timer.Every(8, () => ctx.Guard("companions", () =>
            {
                if (!spells.InBattle) return;
                if (!spawned && attempts++ < 4) { for (int i = 0; i < 3; i++) { var c = companions.Spawn(-1.5 + i * 1.5, 1.2, new[] { 2, 7, 5 }[i], null, 0.7, new[] { CompanionMode.Follow, CompanionMode.Guard, CompanionMode.Hunt }[i]); if (c != null) spawned = true; ctx.Log($"companion {i}: {(c == null ? "нет слота" : "слот " + c.Slot)}"); } }
                var hpos = enemies.HeroPos;
                foreach (var cc in companions.Active) if (hpos.HasValue) ctx.Log($"  союзник слот {cc.Slot} {cc.Mode}: до героя {UnityEngine.Vector2.Distance(cc.Pos, new UnityEngine.Vector2(hpos.Value.x, hpos.Value.y)):0.0}, врагов рядом {enemies.Near(cc.Pos.x, cc.Pos.y, 3).Count}");
                ctx.Log($"снаряды: всего {total}, от двойников {fromComp}, ID снарядов двойников: {string.Join(", ", System.Linq.Enumerable.Select(seen, kv => kv.Key + "x" + kv.Value))}, активных {companions.Active.Count}");
            }));
            bool logged = false;
            effects.OnHeroPass(v => { if (logged) return; logged = true; ctx.Guard("Spell stat", () => ctx.Log($"Spell(1,Damage)={v.Spell(1, SpellStat.Damage).Value:0}, Spell(1,Num)={v.Spell(1, SpellStat.Num).Value:0}, Spell(2,Cooldown)={v.Spell(2, SpellStat.Cooldown).Value:0.00}")); });
            ctx.Timer.Every(20, () => { if (spells.InBattle) { hero.AddShield(30); ctx.Log($"итог: ударов по герою {hits}, урона {dmg:0}, щит {hero.Shield:0}"); } });
        }
    }
}
