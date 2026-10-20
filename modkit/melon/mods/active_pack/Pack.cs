using System;
using System.Collections.Generic;
using System.Linq;
using ModKit;

namespace ActivePack
{
    /// <summary>
    /// Ручной каст для заклинаний, у которых в игре есть корутина Play_X (14 из 17 заклинаний таблицы Spells.cs).
    /// Авто-каст выбранных заклинаний отключается, вместо него на экране боя появляются кнопки (до 4 в ряд: Slot 0..3).
    /// На устройстве проверен только Огненный шар; остальные кнопки собраны по тому же шаблону, но не проверены.
    /// </summary>
    public class PackMod : IKitMod
    {
        private class Sp { public int Id; public string Name, Label; public bool Verified; public double[] Color; }

        // Магическая пуля (1), Электрозона (8) и Армагеддон (14) не имеют Play_X в C_Battle_AllyUnit (проверено по dump.cs): CanCast = false, в мод не включены.
        private static readonly Sp[] All =
        {
            new Sp { Id = 2, Name = "Огненный шар", Label = "Огонь", Verified = true, Color = new[] { 0.75, 0.25, 0.05, 0.9 } },
            new Sp { Id = 5, Name = "Ледяные иглы", Label = "Лёд", Color = new[] { 0.2, 0.5, 0.8, 0.9 } },
            new Sp { Id = 7, Name = "Молния", Label = "Гром", Color = new[] { 0.25, 0.35, 0.85, 0.9 } },
            new Sp { Id = 9, Name = "Цунами", Label = "Волна", Color = new[] { 0.1, 0.45, 0.6, 0.9 } },
            new Sp { Id = 10, Name = "Метеорит", Label = "Метеор", Color = new[] { 0.45, 0.2, 0.15, 0.9 } },
            new Sp { Id = 12, Name = "Вихрь", Label = "Вихрь", Color = new[] { 0.4, 0.55, 0.4, 0.9 } },
            new Sp { Id = 13, Name = "Электрический удар", Label = "Удар", Color = new[] { 0.55, 0.45, 0.9, 0.9 } },
            new Sp { Id = 15, Name = "Испепеление", Label = "Жар", Color = new[] { 0.85, 0.4, 0.1, 0.9 } },
            new Sp { Id = 16, Name = "Энергетическая пуля", Label = "Энергия", Color = new[] { 0.3, 0.7, 0.75, 0.9 } },
            new Sp { Id = 17, Name = "Метель", Label = "Метель", Color = new[] { 0.5, 0.7, 0.85, 0.9 } },
            new Sp { Id = 18, Name = "Таинственный луч", Label = "Луч", Color = new[] { 0.6, 0.25, 0.7, 0.9 } },
            new Sp { Id = 19, Name = "Магический круг", Label = "Круг", Color = new[] { 0.5, 0.35, 0.6, 0.9 } },
            new Sp { Id = 20, Name = "Лавовая область", Label = "Лава", Color = new[] { 0.7, 0.2, 0.1, 0.9 } },
            new Sp { Id = 21, Name = "Световая вспышка", Label = "Свет", Color = new[] { 0.8, 0.75, 0.3, 0.9 } },
        };

        private const int MaxButtons = 4;       // кнопки идут справа налево по 170 px: при ширине 720 влезает ровно 4 (ядро не умеет второй ряд, см. API_WISHLIST.md)
        private static Action _apply;
        private static Action _openCfg;

        private static string Key(Sp s) => "s" + s.Id;

        public ModDef Define()
        {
            var d = new ModDef
            {
                Id = "active_pack", Name = "Активные заклинания: набор", Version = "1.0",
                Description = "Ручной каст: кнопки на экране боя вместо авто-каста для выбранных заклинаний (до 4). «Настройки» — выбор заклинаний. Проверен только Огненный шар, остальное на устройстве не проверено.",
                OpenSettings = ctx => _openCfg?.Invoke(),
                OnEnable = Enable,
                OnDisable = ctx => { _apply = null; _openCfg = null; },
            };
            d.Options.Add(new ModOption { Key = "cooldown", Label = "Перезарядка кнопки, с (0 — как у заклинания в игре)", Default = 8, Min = 0, Max = 60, Step = 1 });
            d.Options.Add(new ModOption { Key = "burst", Label = "Залпов за нажатие", Default = 3, Min = 1, Max = 10, Step = 1 });
            foreach (var s in All) d.Options.Add(new ModOption { Key = Key(s), Label = s.Name, Default = s.Id == 2 ? 1 : 0, Min = 0, Max = 1, Step = 1, Hidden = true });
            return d;
        }

        private static void Enable(ModContext ctx)
        {
            var live = new List<IActiveSpell>();
            var sp = ctx.Use<ISpells>("modkit_spells");

            List<Sp> Selected() => All.Where(s => ctx.Setting(Key(s)) >= 1).ToList();

            void Apply()
            {
                foreach (var a in live) { try { a.Remove(); } catch (Exception e) { ctx.Log("снятие кнопки: " + e.Message); } }
                live.Clear();
                var sel = Selected();
                if (sel.Count > MaxButtons)
                {
                    ctx.Warn($"Выбрано {sel.Count} заклинаний, кнопок может быть {MaxButtons}: лишние пропущены");
                    sel = sel.Take(MaxButtons).ToList();
                }
                int slot = 0, logged = 0;
                var names = new List<string>();
                foreach (var s in sel)
                {
                    var spell = s;
                    if (!sp.CanCast(spell.Id)) { ctx.Log($"{spell.Name} (id {spell.Id}): нет корутины Play_X/флага у героя, пропущено"); continue; }
                    try
                    {
                        live.Add(sp.AddActive(new ActiveSpellDef
                        {
                            SpellId = spell.Id, Label = spell.Label, Slot = slot++, Color = spell.Color,
                            Cooldown = ctx.Setting("cooldown"), Burst = (int)ctx.Setting("burst"),
                            OnCast = n => { if (logged++ < 60) ctx.Log($"{spell.Name}: залпов {n}, уровень {sp.Level(spell.Id)}, перезарядка игры {sp.Cooldown(spell.Id):0.00} с"); },
                        }));
                        names.Add($"{spell.Name}#{spell.Id}{(spell.Verified ? "" : "?")}");
                    }
                    catch (Exception e) { ctx.Log($"{spell.Name}: кнопка не создана: {e.Message}"); }
                }
                ctx.Log($"активных кнопок: {live.Count} [{string.Join(", ", names)}], перезарядка {ctx.Setting("cooldown")} с, залпов {ctx.Setting("burst")}");
            }
            _apply = Apply;

            void OpenCfg(string warn)
            {
                var o = new UiKit.ChecklistOptions
                {
                    Name = "active_pack_cfg", Title = "Активные заклинания",
                    Subtitle = warn ?? $"Отметьте до {MaxButtons} заклинаний: их кнопки появятся в бою. Магическая пуля, Электрозона и Армагеддон не поддерживаются.",
                    Get = k => ctx.Setting(k) >= 1,
                    Set = (k, v) =>
                    {
                        if (v && Selected().Count >= MaxButtons)
                        {
                            ctx.Log($"выбор {k} отклонён: уже {MaxButtons} заклинаний");
                            OpenCfg($"Уже выбрано {MaxButtons} — снимите другое заклинание.");     // окно с тем же именем заменяет прежнее
                            return;
                        }
                        ctx.SetSetting(k, v ? 1 : 0);
                        ctx.Log($"настройка {k} = {(v ? 1 : 0)}");
                        Apply();
                    },
                };
                var verified = new UiKit.ChecklistSection { Header = "Проверено на устройстве" };
                var unverified = new UiKit.ChecklistSection { Header = "Не проверено на устройстве" };
                foreach (var s in All)
                    (s.Verified ? verified : unverified).Items.Add(new UiKit.ChecklistItem { Key = Key(s), Label = $"{s.Name} (кнопка «{s.Label}»)" });
                o.Sections.Add(verified);
                o.Sections.Add(unverified);
                UiKit.OpenChecklist(o);
            }
            _openCfg = () => ctx.Guard("окно настроек active_pack", () => OpenCfg(null));

            ctx.Guard("кнопки активных заклинаний", Apply);
        }
    }
}
