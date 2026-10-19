using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Il2Cpp;
using ModKit;
using UnityEngine;
using UnityEngine.UI;

namespace ClassMenu
{
    /// <summary>
    /// Карта классов: все 24 класса игры (таблица Class, школы 1..24) и новые классы модов (реестр "classes") в одном окне на PanView.
    /// Настоящий класс выбирается записью A_MainMenuInformation.SelectClassID (в бой его копирует Button_AreaStart). Класс мода даёт эффекты
    /// через ctx.SetGlobalEffects и складывается с выбранным классом игры. Выбор класса мода хранится в настройке sel.
    /// </summary>
    public class MenuMod : IKitMod
    {
        private static Action _open, _openReplace, _restore, _patch;
        /// <summary>Практика класса, при которой он «докачан» (последний порог слота, разведка CLASSES.md §7: 1, 4, 10, 22, 46).</summary>
        private const int MaxPractice = 46;

        private class Entry
        {
            public string Key, Name; public bool Real, Unlocked; public int ClassId; public byte[] Icon; public double Progress = -1;
            public List<string> Lines = new List<string>(); public List<(int Id, double Value)> Effects = new List<(int, double)>();
        }

        private static readonly Regex Tags = new Regex("<[^>]*>");
        private static string Clean(string s) => Tags.Replace(s ?? "", "").Trim();

        public ModDef Define() => new ModDef
        {
            Id = "class_menu", Name = "Классы мода", Version = "4.1",
            Description = "Карта классов: все классы игры и новые классы модов. Кнопка на экране классов или «Настройки» мода.",
            Options =
            {
                new ModOption { Key = "replace", Label = "Заменить меню классов игры (1/0)", Default = 0, Min = 0, Max = 1, Step = 1 },
                new ModOption { Key = "sel", Label = "Выбранный класс мода (номер)", Default = 0, Min = 0, Max = 9999, Step = 1, Hidden = true },
            },
            OnEnable = ctx =>
            {
                var reg = ctx.Registry<CustomClass>("classes");
                string selCustom = "";
                var customOrder = new List<string>();

                void Refresh()
                {
                    customOrder = reg.All.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
                    int n = (int)ctx.Setting("sel");
                    selCustom = n >= 1 && n <= customOrder.Count ? customOrder[n - 1] : "";
                    ctx.SetGlobalEffects(new (int, double)[0]);       // класс мода не складывается с классом игры, а подменяет его строку (Patch)
                    ctx.Log(selCustom == "" ? "класс мода не выбран" : $"класс мода «{reg.All[selCustom].Name}»: эффектов {reg.All[selCustom].Effects.Count}");
                }

                // кнопка на игровом экране классов: открывает карту
                ctx.After("A_MainMenuInformation", "Create_Class", 0, (self, args) =>
                {
                    try
                    {
                        var field = ((A_MainMenuInformation)self).Obj_FieldClass;
                        if (field == null || field.transform.Find("ModKitClassBtn") != null) return;
                        var holder = UiKit.Node("ModKitClassBtn", field.transform);
                        UiKit.Stretch(holder.rt);
                        UiKit.AddButton(holder.rt, "Классы мода", UiKit.LogicalW - 270, 150, 240, 80, 26, new[] { 0.22, 0.30, 0.45, 0.95 }, () => _open?.Invoke());
                        ctx.Log("кнопка «Классы мода» добавлена на экран классов");
                    }
                    catch (Exception e) { ctx.Log("кнопка на экране классов: " + e.Message); }
                });
                // режим замены: экран классов игры прячем и показываем свою карту (после Create_Class ждём, пока экран реально откроется)
                double wantAt = -100; var clock = System.Diagnostics.Stopwatch.StartNew();
                ctx.After("A_MainMenuInformation", "Create_Class", 0, (self, args) => { if (ctx.Setting("replace") >= 1) wantAt = clock.Elapsed.TotalSeconds; });
                ctx.Every(200, () =>
                {
                    if (wantAt < 0) return;
                    double now = clock.Elapsed.TotalSeconds;
                    if (now - wantAt > 3) { wantAt = -100; return; }
                    var m = FindMenu();
                    var f = m?.Obj_FieldClass;
                    if (f == null || !f.activeInHierarchy) return;
                    wantAt = -100;
                    f.SetActive(false);
                    ctx.Log("экран классов игры скрыт, открываю карту");
                    _openReplace?.Invoke();
                });
                ctx.Events.On<RegistryChanged>(e => { if (e.Registry == "classes") Refresh(); });
                ctx.Every(500, () => { if (customOrder.Count == 0 && reg.All.Count > 0) Refresh(); });
                Refresh();
                // замена класса игры: перед стартом боя эффекты и имя строки выбранного класса игры подменяются эффектами класса мода,
                // после возврата в главное меню (и при выключении мода) возвращаются
                var saved = new List<(int Row, string Col, object Old)>();
                void Restore()
                {
                    if (saved.Count == 0) return;
                    try { var t = new Table("Class"); foreach (var s in saved) t.Set(s.Row, s.Col, s.Old); ctx.Log("строка класса игры возвращена"); }
                    catch (Exception e) { ctx.Log("откат строки класса: " + e.Message); }
                    saved.Clear();
                }
                void Patch()
                {
                    Restore();
                    if (selCustom == "" || !reg.TryGet(selCustom, out var c)) return;
                    var m = FindMenu(); int cid = m != null ? m.SelectClassID : Z_Database.Game_BattleClassID;
                    if (cid < 1 || cid > 24) return;
                    try
                    {
                        var t = new Table("Class"); int row = t.IndexOf(cid); if (row < 0) { ctx.Log("строка класса не найдена"); return; }
                        void W(string col, object v) { saved.Add((row, col, t.Set(row, col, v))); }
                        for (int k = 1; k <= 6; k++)
                        {
                            var ef = k <= c.Effects.Count ? c.Effects[k - 1] : (1, 0.0);
                            W($"효과ID_{k:00}", ef.Item1); W($"효과ID_{k:00}값", ef.Item2);
                        }
                        W("이름", c.Name);
                        // описание: строки класса мода (подпись, текст, эффекты) вместо строк класса игры; видно на родном экране классов
                        var lines = new List<string>();
                        if (!string.IsNullOrEmpty(c.Subtitle)) lines.Add(c.Subtitle);
                        lines.AddRange(c.Lines);
                        foreach (var f in c.Effects) lines.Add($"эффект {f.Id}: {UiKit.Fmt(f.Value)}");
                        for (int k = 1; k <= 6; k++)
                        {
                            W($"효과 설명줄{k:00}", k <= lines.Count ? lines[k - 1] : "`");
                            W($"설명줄{k:00} 색깔", k == 1 ? "<color=#FFD966>" : "<color=#FFFFFF>");
                        }
                        W("효과 설명줄 개수", Math.Max(1, Math.Min(6, lines.Count)));
                        ctx.Log($"класс игры {cid} заменён классом мода «{c.Name}»: {Math.Min(6, c.Effects.Count)} эфф.");
                    }
                    catch (Exception e) { ctx.Log("подмена класса: " + e.Message); Restore(); }
                }
                ctx.Before("A_MainMenuInformation", "Button_AreaStart", 0, (self, args) => Patch());
                ctx.Before("A_MainMenuInformation", "Button_AreaStart_Loop", 0, (self, args) => Patch());
                ctx.Before("A_MainMenuInformation", "Button_AreaSpecialSheet_Start", 0, (self, args) => Patch());
                ctx.After("A_MainMenuInformation", "MainMenuInformation", 0, (self, args) => Restore());
                _restore = Restore; _patch = Patch;
                // диагностика: что реально получилось в характеристиках героя после пересчёта (первые 8 раз)
                int diag = 8;
                ctx.After("V_Ability_Manager", "AbilityStatApply", 1, (self, args) =>
                {
                    if (diag-- <= 0) return;
                    try { ctx.Log($"после пересчёта: класс игры {Z_Database.Game_BattleClassID}, мод «{selCustom}», HpMaxU={Z_Database.AllyUnitVar_HpMaxRatioU}, HitDmgD={Z_Database.AllyUnitVar_HitDamageRatioD}, Regen={Z_Database.AllyUnitVar_HpRegenRateNum}, Evasion={Z_Database.AllyUnitVar_EvasionChanceRatio}, ManaObtU={Z_Database.AllyUnitVar_ManaObtainRatioU}"); }
                    catch (Exception e) { ctx.Log("диагностика: " + e.Message); }
                });

                void Pick(string id)
                {
                    int idx = customOrder.IndexOf(id) + 1;
                    ctx.SetSetting("sel", selCustom == id ? 0 : idx);       // повторное нажатие снимает выбор
                    Refresh();
                    Patch();                                                  // подмена видна сразу, не только при старте боя
                }
                _open = () => Guard(ctx, () => OpenMap(ctx, reg, () => customOrder, () => selCustom, Pick));
                _openReplace = () => Guard(ctx, () => OpenMap(ctx, reg, () => customOrder, () => selCustom, Pick, true));
            },
            OnDisable = ctx => _restore?.Invoke(),
            OpenSettings = ctx => _open?.Invoke(),
        };

        private static void Guard(ModContext ctx, Action fn) => ctx.Guard("карта классов", fn);

        /// <summary>Прячет окна ModKit (список модов, диалоги): GameObject.Find в этой сборке недоступен, ищем по типу Transform.</summary>
        private static void CloseModWindows()
        {
            foreach (var o in UnityEngine.Object.FindObjectsOfType(Il2CppInterop.Runtime.Il2CppType.Of<Transform>()))
            {
                var t = o.TryCast<Transform>();
                if (t != null && (t.name == "ModKitWindow" || t.name == "ModKitDialog" || t.name == "ModKitPicker")) t.gameObject.SetActive(false);
            }
        }

        private static A_MainMenuInformation FindMenu()
        {
            foreach (var o in UnityEngine.Object.FindObjectsOfType(Il2CppInterop.Runtime.Il2CppType.Of<A_MainMenuInformation>()))
            {
                var m = o.TryCast<A_MainMenuInformation>();
                if (m != null) return m;
            }
            return null;
        }

        private static List<Entry> RealClasses(ModContext ctx)
        {
            var list = new List<Entry>();
            var t = new Table("Class");
            int[] practice = null;
            try { var a = Z_Database.Array_AbilityClassPractice; practice = new int[a.Length]; for (int i = 0; i < a.Length; i++) practice[i] = a[i]; } catch { /* нет массива */ }
            for (int i = 0; i < t.Length; i++)
            {
                int id = (int)t.Num(i, "ID");
                if (id < 1 || id > 24 || !(t.Get(i, "유형") is string type) || type != "학파") continue;
                var e = new Entry { Key = "real" + id, Real = true, ClassId = id, Name = Clean(t.Get(i, "이름") as string) };
                for (int k = 2; k <= 5; k++) { var l = Clean(t.Get(i, $"효과 설명줄{k:00}") as string); if (l != "" && l != "`") e.Lines.Add(l); }
                e.Unlocked = practice == null || id - 1 >= practice.Length || practice[id - 1] > 0;
                if (practice != null && id - 1 < practice.Length) e.Progress = Math.Min(1.0, practice[id - 1] / (double)MaxPractice);
                list.Add(e);
            }
            return list;
        }

        private static void OpenMap(ModContext ctx, IRegistry<CustomClass> reg, Func<List<string>> order, Func<string> selCustom, Action<string> pickCustom, bool replacing = false)
        {
            _restore?.Invoke();                                   // читаем настоящие имена, а не подменённые
            var entries = RealClasses(ctx);
            int realCount = entries.Count;
            foreach (var id in order())
            {
                var c = reg.All[id];
                var e = new Entry { Key = "mod:" + id, Name = c.Name, Real = false, Unlocked = true, Effects = c.Effects, Icon = c.Icon };
                if (!string.IsNullOrEmpty(c.Subtitle)) e.Lines.Add(c.Subtitle);
                e.Lines.AddRange(c.Lines);
                entries.Add(e);
            }
            var menu = FindMenu();
            int selReal = menu != null ? menu.SelectClassID : 0;
            // в игре мог остаться выбор несуществующего класса (например 25 после теста): тогда выбираем первый открытый настоящий
            if (menu != null && (selReal < 1 || selReal > 24 || !entries.Any(e => e.Real && e.ClassId == selReal && e.Unlocked)))
            {
                var first = entries.FirstOrDefault(e => e.Real && e.Unlocked);
                if (first != null) { ctx.Log($"выбранный класс игры {selReal} недопустим, ставлю {first.ClassId} «{first.Name}»"); menu.SelectClassID = first.ClassId; selReal = first.ClassId; }
            }

            // раскладка: сетка по 4 в ряд, классы мод — ниже, отдельной строкой
            var o = new UiKit.PanOptions { NodeSize = 110 };
            int cols = 4;
            double gx = 120, gy = 165, x0 = 170, y0 = 130;     // зазор между плитками ~10 px (NodeSize 110), снизу место под подпись
            for (int i = 0; i < entries.Count; i++)
            {
                bool isMod = !entries[i].Real;
                int idx = isMod ? i - realCount : i;
                double rowShift = isMod ? ((realCount + cols - 1) / cols) * gy + 60 : 0;
                double x = x0 + (idx % cols) * gx, y = y0 + (idx / cols) * gy + rowShift;
                var node = new UiKit.PanNode(entries[i].Key, x, y, entries[i].Icon, entries[i].Name, UiKit.PanState.Locked);
                if (entries[i].Real) node.Sprite = UiKit.GameSprite("SpriteList_ClassPortrait", entries[i].ClassId);     // портрет класса из игры
                node.Progress = entries[i].Progress;                                                // рамка достраивается по мере прокачки
                o.Nodes.Add(node);
            }
            o.ContentW = x0 * 2 + (cols - 1) * gx + 110;
            o.ContentH = o.Nodes.Max(n => n.Y) + 200;

            Text name = null, body = null, hint = null;
            UiKit.PanView map = null;
            Entry shown = null;

            UiKit.PanState StateOf(Entry e) =>
                e.Real ? (e.ClassId == selReal ? UiKit.PanState.Done : e.Unlocked ? UiKit.PanState.Available : UiKit.PanState.Locked)
                       : (selCustom() == e.Key.Substring(4) ? UiKit.PanState.Done : UiKit.PanState.Available);

            void Repaint() { foreach (var e in entries) map.SetState(e.Key, StateOf(e)); }

            void Show(Entry e)
            {
                shown = e;
                name.text = e.Name + (e.Real ? "" : "   (заменяет класс игры)");
                var sb = new System.Text.StringBuilder();
                foreach (var l in e.Lines) sb.AppendLine(l);
                foreach (var f in e.Effects) sb.AppendLine($"эффект {f.Id}: {UiKit.Fmt(f.Value)}");
                body.text = sb.ToString();
                hint.text = e.Real
                    ? (!e.Unlocked ? "Класс закрыт (нужна практика в игре)" : e.ClassId == selReal ? "Выбран. Нажмите «Выбрать» на другом классе, чтобы сменить" : "Нажмите ещё раз, чтобы выбрать")
                    : (selCustom() == e.Key.Substring(4) ? "Выбран. Нажмите ещё раз, чтобы снять" : "Нажмите ещё раз, чтобы выбрать");
            }

            o.OnTap = n =>
            {
                var e = entries.First(x => x.Key == n.Id);
                if (e.Real)
                {
                    if (e.Unlocked)
                    {
                        var m = FindMenu();
                        if (m == null) { hint.text = "Меню игры не найдено"; return; }
                        m.SelectClassID = e.ClassId;
                        selReal = e.ClassId;
                        ctx.Log($"выбран класс игры {e.ClassId} «{e.Name}»"); _patch?.Invoke();
                    }
                }
                else { pickCustom(e.Key.Substring(4)); ctx.Log($"выбран класс мода «{e.Name}»"); }   // повторное нажатие снимает выбор
                Repaint(); Show(e);
            };

            map = UiKit.OpenPanWindow("class_map", "Классы", "Тяните карту; нажмите класс — он выбран; затем «В бой»", o, 330, replacing ? () => { try { FindMenu()?.Button_Class_OUT(); } catch (Exception e) { ctx.Log("Button_Class_OUT: " + e.Message); } } : null);
            Repaint();
            var parent = map.View.parent;
            double top = UiKit.ScreenH - 320;
            name = UiKit.Tint(UiKit.Label(parent, "Нажмите на класс", 40, top, UiKit.LogicalW - 280, 50, 32, 3), UiKit.Gold);
            body = UiKit.Label(parent, "", 40, top + 54, UiKit.LogicalW - 80, 170, 22, 0);
            hint = UiKit.Tint(UiKit.Label(parent, "", 40, top + 228, UiKit.LogicalW - 80, 40, 20, 3), UiKit.Col(0.7, 0.75, 0.85));
            // «В бой»: закрывает карту классов и меню модов и открывает родной экран зон («Войти в зону»)
            UiKit.AddButton(parent, "В бой", UiKit.LogicalW - 220, top - 4, 180, 64, 28, new[] { 0.20, 0.45, 0.22, 1 }, () =>
            {
                try
                {
                    var m = FindMenu();
                    if (m == null) { hint.text = "Меню игры не найдено"; return; }
                    parent.gameObject.SetActive(false);
                    CloseModWindows();
                    m.Obj_FieldClass?.SetActive(false);
                    m.Button_FieldArea_IN();
                    ctx.Log("«В бой»: окна модов закрыты, открыт родной экран зон");
                }
                catch (Exception ex) { ctx.Log("В бой: " + ex.Message); parent.gameObject.SetActive(true); }
            });
            var cur = entries.FirstOrDefault(e => e.Real && e.ClassId == selReal);
            if (cur != null) { Show(cur); map.CenterOn(cur.Key); }
        }
    }
}
