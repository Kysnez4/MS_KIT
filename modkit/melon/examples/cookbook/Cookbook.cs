using System;
using System.Collections.Generic;
using System.Linq;
using ModKit;
using ModKitData;

namespace Cookbook
{
    /// <summary>Рецепты из docs/COOKBOOK.md. Код собирается вместе с ядром, поэтому примеры в документации не устаревают молча.</summary>
    public class CookbookMod : IKitMod
    {
        public ModDef Define() => new ModDef
        {
            Id = "cookbook", Name = "Книга рецептов", Version = "1.0",
            Options = { new ModOption { Key = "recipe", Label = "Рецепт (1..12, 0 — выключено)", Default = 0, Min = 0, Max = 12, Step = 1 } },
            OnEnable = ctx =>
            {
                switch ((int)ctx.Setting("recipe"))
                {
                    case 1: Hooks(ctx); break;
                    case 2: Events(ctx); break;
                    case 3: Combat(ctx); break;
                    case 4: HeroEffect(ctx); break;
                    case 5: CustomEffect(ctx); break;
                    case 6: NewArtifact(ctx); break;
                    case 7: ActiveSpell(ctx); break;
                    case 8: Companion(ctx); break;
                    case 9: StoreAndToast(ctx); break;
                    case 10: Window(ctx); break;
                    case 11: ResourceAndTriggers(ctx); break;
                    case 12: Registry(ctx); break;
                }
            },
        };

        // 1. Хуки на методы игры
        static void Hooks(ModContext ctx)
        {
            ctx.After("C_Battle_Manager", "Start", 0, (self, args) => ctx.Log("бой начался"));
            ctx.Before("C_Battle_AllyUnit", "Unit_Damage", 3, (self, args) => ctx.Log($"герой получит урон {Convert.ToDouble(args[0]):0}"));
        }

        // 2. События ядра, типизированные события, свои события
        static void Events(ModContext ctx)
        {
            ctx.Events.On("enemyDeath", o => { var d = (EventData)o; ctx.Log("враг умер, тип " + d.Get<int>("unitId")); });
            ctx.Events.On<BattleResult>(r => ctx.Log($"итог боя: {r.Kills} убийств за {r.Time:0} с"));   // нужна библиотека modkit_game
            ctx.Events.On("cookbook:ping", o => ctx.Log("получен ping"));
            ctx.Events.Emit("cookbook:ping", new EventData { ["n"] = 1 });
        }

        // 3. Враги и урон
        static void Combat(ModContext ctx)
        {
            var enemies = ctx.Use<IEnemies>("modkit_combat");
            var combat = ctx.Use<ICombat>("modkit_combat");
            ctx.Timer.Every(2, () =>
            {
                var hp = enemies.HeroPos;
                if (!hp.HasValue) return;                                  // боя нет
                foreach (var e in enemies.Near(hp.Value.x, hp.Value.y, 6).Take(3))
                    combat.Damage(e, 20);
            });
        }

        // 4. Править характеристики героя после каждого пересчёта
        static void HeroEffect(ModContext ctx)
        {
            ctx.Use<IEffects>("modkit_effects").OnHeroPass(view =>
            {
                view.MulPercent("UnitApply_MagicBulletDamage", 25);       // +25% урона Магической пули
                var n = view.Field("UnitApply_MagicBulletNum");           // Value читает базу, запись задаёт результат
                n.Value = Math.Min(n.Value, 5);
            });
        }

        // 5. Свой эффект артефакта (ID >= 10000)
        static void CustomEffect(ModContext ctx)
        {
            var fx = ctx.Use<IEffects>("modkit_effects");
            fx.Define(new EffectDef
            {
                Id = 10100, Name = "Урон пули в %",
                Apply = (view, value, c) => view.MulPercent("UnitApply_MagicBulletDamage", value),
            });
        }

        // 6. Новый артефакт
        static void NewArtifact(ModContext ctx)
        {
            ctx.Use<IData>("modkit_data").WhenReady(() => ctx.Guard("артефакт", () =>
            {
                int id = ctx.Use<IArtifacts>("modkit_artifacts").Add(new ArtifactDef
                {
                    Name = "Рецепт", Rarity = "B", CloneFrom = 42, Weight = 5,
                    Effects = { new Effect(4, 10) },                        // 4 = атака, +10
                    Lines = new List<string> { "Атака +10%" },
                });
                ctx.Log("артефакт добавлен, ID " + id);
            }));
        }

        // 7. Активное заклинание: кнопка на экране боя
        static void ActiveSpell(ModContext ctx)
        {
            ctx.Use<ISpells>("modkit_spells").AddActive(new ActiveSpellDef { SpellId = 2, Label = "Огонь", Cooldown = 8, Burst = 3, OnCast = n => ctx.Log("залпов " + n) });
        }

        // 8. Союзник
        static void Companion(ModContext ctx)
        {
            var spells = ctx.Use<ISpells>("modkit_spells");
            var companions = ctx.Use<ICompanions>("modkit_companions");
            bool spawned = false;
            ctx.Timer.Every(3, () =>
            {
                if (!spells.InBattle || spawned) return;
                spawned = companions.Spawn(1.5, 1, spellId: 2, mode: CompanionMode.Hunt) != null;
            });
            ctx.Events.On("battleStart", _ => spawned = false);
        }

        // 9. Хранилище, таймеры, подпись на экране
        static void StoreAndToast(ModContext ctx)
        {
            int runs = ctx.Store.Get("runs", 0) + 1;
            ctx.Store.Set("runs", runs);
            ctx.Timer.After(5, () => ctx.Toast($"Запусков мода: {runs}", 3));
        }

        // 10. Своё окно
        static void Window(ModContext ctx)
        {
            var win = UiKit.OpenPage("cookbook", "Книга рецептов", "Пример окна");
            UiKit.Label(win.rt, "Привет из мода", 30, win.top, 600, 40, 28);
            UiKit.AddButton(win.rt, "Нажми", 30, win.top + 60, 300, 70, 28, new[] { 0.2, 0.5, 0.9, 1 }, () => ctx.Toast("нажато", 2));
        }

        // 11. Ресурс мода и триггеры
        static void ResourceAndTriggers(ModContext ctx)
        {
            var hope = ctx.Use<IResources>("modkit_resources").Create("hope", 100, 2);
            hope.ShowBar("Надежда", 20, 300, 300, 28, null);
            ctx.Use<ITriggers>("modkit_triggers").OnKills(50, n => hope.Add(10));
            ctx.Use<ITriggers>("modkit_triggers").OnLowHp(0.3, 20, () => { if (hope.TrySpend(30)) ctx.Use<IHero>("modkit_hero").Heal(100); });
        }

        // 12. Общий реестр между модами
        static void Registry(ModContext ctx)
        {
            var reg = ctx.Registry<string>("cookbook.notes");
            reg.Register("hello", "привет");
            ctx.Log("записей: " + reg.All.Count);
        }
    }
}
