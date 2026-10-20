using System;
using System.Collections.Generic;
using System.Linq;
using Il2Cpp;
using UnityEngine;
using UnityEngine.UI;
using static ModKit.UiKit;

namespace ModKit
{
    /// <summary>
    /// Окно «Моды» на Unity uGUI: запись на главном экране (над иконкой «Статистика») и панель со списком модов.
    /// Карточка: нажатие включает/выключает мод, долгое нажатие — удаление zip-мода. «+» вверху — поиск архивов в Download.
    /// </summary>
    public static class MenuUi
    {
        private static GameObject _panel, _rootGo, _entryGo, _bannerGo;
        private static RectTransform _content, _winRt;
        private static string _status = "";
        private static bool _dialogShown;
        private static IntPtr _lastSelf = IntPtr.Zero;
        private static int _failures, _calls;
        private static string _stage = "";
        private static void Stage(string s) { _stage = s; Log.Write("этап: " + s); }

        /// <summary>Изменения, сделанные в окне «Моды» и ещё не подтверждённые перезапуском: мод -> прежнее состояние.</summary>
        private static readonly Dictionary<ModState, (bool enabled, Dictionary<string, double> settings)> Pending =
            new Dictionary<ModState, (bool, Dictionary<string, double>)>();

        /// <summary>Моды, обновлённые или откатанные через «+»: код загруженной сборки на лету не заменяется, нужен перезапуск игры.</summary>
        private static readonly List<string> Updated = new List<string>();

        private static void Track(ModState st)
        {
            if (!st.Def.RestartRequired || Pending.ContainsKey(st)) return;
            Pending[st] = (st.Enabled, new Dictionary<string, double>(st.Settings));
        }

        // ---------------------------------------------------------------- диалог

        /// <summary>Окно-вопрос поверх всего холста: затемнение, заголовок, текст и кнопки. Закрывается любой кнопкой.</summary>
        private static GameObject ShowDialog(string title, string text, (string name, Action fn)[] buttons, double tall = 0)
        {
            var dim = Node("ModKitDialog", CanvasTf);
            Stretch(dim.rt);
            var dimImg = AddImage(dim.go, 0, 0, 0, 0.8);
            dim.go.AddComponent<Button>().targetGraphic = dimImg;                 // глотает касания под окном
            double h = 420 + tall;
            double top = Math.Max(40, (ScreenH - h) / 2);
            var box = Node("box", dim.rt);
            Place(box.rt, 40, top, LogicalW - 80, h);
            AddImage(box.go, 0.09, 0.09, 0.11, 1);
            Tint(Label(box.rt, title, 20, 16, LogicalW - 120, 70, 38, 4), Gold);
            Tint(Label(box.rt, text, 30, 96, LogicalW - 140, h - 96 - 110, 25, 0), Col(0.9, 0.9, 0.9));
            double bw = (LogicalW - 80 - 30 * (buttons.Length + 1)) / buttons.Length;
            for (int i = 0; i < buttons.Length; i++)
            {
                var fn = buttons[i].fn;
                AddButton(box.rt, buttons[i].name, 30 + i * (bw + 30), h - 100, bw, 80, 28, new[] { 0.22, 0.25, 0.30, 1 }, () =>
                {
                    SafeDestroy(dim.go);
                    fn();
                });
            }
            return dim.go;
        }

        // ---------------------------------------------------------------- плашка «Выйти и применить»

        private static void ShowBanner()
        {
            SafeDestroy(_bannerGo);
            _bannerGo = null;
            if (_winRt == null || (Pending.Count == 0 && Updated.Count == 0)) return;
            var b = Node("banner", _winRt);
            _bannerGo = b.go;
            double h = 210;
            Place(b.rt, 20, ScreenH - h - 20, LogicalW - 40, h);
            AddImage(b.go, 0.10, 0.12, 0.18, 0.98);
            string note = Updated.Count > 0
                ? $"Обновлено модов: {Updated.Count} ({string.Join(", ", Updated.Take(3))}{(Updated.Count > 3 ? "…" : "")}). Новый код заработает после перезапуска игры."
                : $"Изменено модов: {Pending.Count}. Часть правок (пулы выпадения, синергии) полностью заработает после перезапуска.";
            Tint(Label(b.rt, note, 20, 8, LogicalW - 80, 100, 22, 0), Col(0.95, 0.9, 0.7));
            double bw = (LogicalW - 40 - 90) / 2;
            AddButton(b.rt, "Выйти и применить", 30, h - 90, bw, 76, 26, new[] { 0.20, 0.38, 0.22, 1 }, () =>
            {
                Core.MarkSession();
                Log.Write("применение: выход из игры по запросу пользователя");
                Application.Quit();
            });
            AddButton(b.rt, Updated.Count > 0 ? "Позже" : "Отмена", 60 + bw, h - 90, bw, 76, 26, new[] { 0.38, 0.20, 0.20, 1 }, () =>
            {
                if (Updated.Count > 0 && Pending.Count == 0) { Updated.Clear(); Rebuild(); return; }     // «Позже»: убрать плашку
                foreach (var kv in Pending.ToList())
                {
                    Core.SetEnabled(kv.Key, kv.Value.enabled);
                    foreach (var s in kv.Value.settings)
                        if (!kv.Key.Settings.TryGetValue(s.Key, out var cur) || cur != s.Value) Core.SetSetting(kv.Key, s.Key, s.Value);
                }
                Pending.Clear();
                Rebuild();
            });
        }

        // ---------------------------------------------------------------- долгое нажатие на карточку

        private static readonly Dictionary<IntPtr, ModState> CardButtons = new Dictionary<IntPtr, ModState>();
        private const double HoldSec = 0.6;
        private static ModState _pressSt;
        private static double _pressT0, _pressY0;

        private static double ScrollY() { try { return _content.anchoredPosition.y; } catch { return 0; } }

        internal static void OnPointerDown(Selectable s)
        {
            if (CardButtons.TryGetValue(s.Pointer, out var st))
            {
                _pressSt = st; _pressT0 = Scheduler.Now; _pressY0 = ScrollY();
                Log.Write($"нажатие на карточку «{st.Def.Id}»");
            }
            else _pressSt = null;
        }

        internal static void OnPointerUp() => _pressSt = null;

        /// <summary>Каждый кадр: если карточку держат дольше HoldSec и список не прокручивался — окно удаления.</summary>
        internal static void Tick()
        {
            if (_pressSt == null || Scheduler.Now - _pressT0 < HoldSec) return;
            var st = _pressSt;
            _pressSt = null;
            if (Math.Abs(ScrollY() - _pressY0) > 8) return;           // палец тащит список, это прокрутка
            Log.Write($"удержание карточки «{st.Def.Id}»: окно удаления");
            try { AskRemove(st); } catch (Exception e) { Log.Write("ошибка окна удаления: " + e); }
        }

        /// <summary>Окно подтверждения удаления zip-мода.</summary>
        private static void AskRemove(ModState st)
        {
            if (st.Def.Meta == null)
            {
                ShowDialog("Встроенный мод", $"«{st.Def.Name}» встроен в загрузчик, удалить его нельзя — только выключить.", new (string, Action)[] { ("Закрыть", () => { }) }, -120);
                return;
            }
            var deps = Core.DependentNames(st);
            var warn = deps.Count > 0 ? $"\n\nОт него зависят: {string.Join(", ", deps)}. Они будут выключены." : "";
            ShowDialog("Удалить мод?", $"«{st.Def.Name}»\n\nМод будет выключен, а его файл удалён с телефона.{warn}", new (string, Action)[]
            {
                ("Удалить", () =>
                {
                    try { Pending.Remove(st); Core.RemoveMod(st); } catch (Exception e) { Log.Write("ошибка удаления: " + e); }
                    Rebuild();
                }),
                ("Отмена", () => { }),
            }, deps.Count > 0 ? 60 : 0);
        }

        // ---------------------------------------------------------------- список модов

        /// <summary>Пересобирает карточки модов (после любого изменения).</summary>
        private static void Rebuild()
        {
            if (_content == null) return;
            ClearChildren(_content);
            CardButtons.Clear();
            double W = LogicalW - 60;
            double y = 0;
            if (_status != "")
            {
                Tint(Label(_content, _status, 10, 0, W - 20, 96, 22, 0), Col(0.97, 0.85, 0.55));
                y += 106;
            }
            foreach (var st in Core.Mods.Where(m => !m.Def.Library).ToList())
            {
                var opts = st.Def.Options.Where(o => !o.Hidden).ToList();
                bool hasSettings = st.Def.OpenSettings != null && st.Active && st.Ctx != null;
                bool blocked = st.Enabled && !st.Active;
                var notes = new List<string>();
                if (blocked) notes.Add(st.Problem ?? st.Fault ?? "не запущен");
                notes.AddRange(st.Warnings.Select(w => "⚠ " + w));
                var needs = string.Join(", ", st.Def.Depends.Select(kv => kv.Key + (kv.Value == "*" ? "" : " " + kv.Value)));
                var shots = (st.Def.Meta?.Screenshots ?? new List<byte[]>()).Take(3).ToList();
                var ver = Core.ModVersion(st);
                double h = 78 + (!string.IsNullOrEmpty(st.Def.Description) ? 62 : 0) + (shots.Count > 0 ? 170 : 0)
                           + (ver != null || st.Def.Meta?.Author != null ? 40 : 0) + (needs != "" ? 40 : 0) + notes.Count * 40 + opts.Count * 76 + (hasSettings ? 80 : 0) + 16;

                var card = Node("card", _content);
                Place(card.rt, 0, y, W, h);
                var cardImg = st.Enabled ? AddImage(card.go, 0.10, 0.20, 0.12, 1) : AddImage(card.go, 0.09, 0.09, 0.09, 1);
                var cardBtn = card.go.AddComponent<Button>();
                cardBtn.targetGraphic = cardImg;
                CardButtons[cardBtn.Pointer] = st;
                var captured = st;
                cardBtn.onClick.AddListener(Act(() =>
                {
                    try { Track(captured); Core.SetEnabled(captured, !captured.Enabled); Rebuild(); } catch (Exception e) { Log.Write("ошибка переключения: " + e); }
                }));

                var icon = st.Def.Meta?.Icon;
                if (icon != null) Picture(card.rt, icon, 14, 8, 62, 62);
                Tint(Label(card.rt, st.Def.Name, icon != null ? 88 : 20, 8, W - (icon != null ? 258 : 190), 62, 32), st.Enabled ? Col(1, 1, 1) : Col(0.85, 0.85, 0.85));
                var state = Label(card.rt, blocked ? "ЖДЁТ" : st.Enabled ? "ВКЛ" : "ВЫКЛ", W - 160, 8, 140, 62, 30, 5);
                Tint(state, blocked ? Col(1, 0.55, 0.35) : st.Enabled ? Col(0.45, 1, 0.45) : Col(0.55, 0.55, 0.55));
                double ly = 74;
                if (!string.IsNullOrEmpty(st.Def.Description))
                {
                    Tint(Label(card.rt, st.Def.Description, 20, ly - 4, W - 40, 58, 23, 0), Col(0.7, 0.7, 0.7));
                    ly += 62;
                }
                var info = string.Join(" · ", new[] { ver != null ? "v" + ver : null, st.Def.Meta?.Author }.Where(s => !string.IsNullOrEmpty(s)));
                if (info != "")
                {
                    Tint(Label(card.rt, info, 20, ly - 4, W - 40, 36, 21, 0), Col(0.5, 0.5, 0.5));
                    ly += 40;
                }
                if (needs != "")
                {
                    Tint(Label(card.rt, "Требует: " + needs, 20, ly - 4, W - 40, 36, 21, 0), Col(0.5, 0.5, 0.5));
                    ly += 40;
                }
                foreach (var n in notes)
                {
                    Tint(Label(card.rt, n, 20, ly - 4, W - 40, 36, 21, 0), Col(1, 0.7, 0.35));
                    ly += 40;
                }
                if (shots.Count > 0)
                {
                    double sw = (W - 40 - (shots.Count - 1) * 10) / shots.Count;
                    for (int i = 0; i < shots.Count; i++) Picture(card.rt, shots[i], 20 + i * (sw + 10), ly - 4, sw, 160);
                    ly += 170;
                }
                foreach (var opt in opts)
                {
                    var o = opt;
                    Tint(Label(_content, o.Label, 40, y + ly, 250, 64, 26), Col(0.9, 0.9, 0.9));
                    Action<int> step = dir =>
                    {
                        double next = Math.Min(o.Max, Math.Max(o.Min, captured.Settings[o.Key] + dir * o.Step));
                        Track(captured);
                        Core.SetSetting(captured, o.Key, Math.Round(next * 1000) / 1000);
                        Rebuild();
                    };
                    AddButton(_content, "−", 300, y + ly, 80, 64, 38, new[] { 0.22, 0.22, 0.22, 1 }, () => step(-1));
                    bool dec = o.Step != Math.Floor(o.Step) || o.Min != Math.Floor(o.Min);
                    AddNumberField(_content, Fmt(st.Settings[o.Key]), 396, y + ly + 2, 112, 60, 32, dec, text =>
                    {
                        if (!double.TryParse(text.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)) { Rebuild(); return; }
                        double next = Math.Round(Math.Min(o.Max, Math.Max(o.Min, v)) * 1000) / 1000;
                        if (next != captured.Settings[o.Key]) { Track(captured); Core.SetSetting(captured, o.Key, next); }
                        Rebuild();
                    });
                    AddButton(_content, "+", 524, y + ly, 80, 64, 38, new[] { 0.22, 0.22, 0.22, 1 }, () => step(1));
                    ly += 76;
                }
                if (hasSettings)
                {
                    AddButton(_content, "Настройки", 40, y + ly, 260, 64, 28, new[] { 0.18, 0.28, 0.42, 1 }, () => captured.Def.OpenSettings(captured.Ctx));
                    ly += 80;
                }
                y += h + 14;
            }
            Tint(Label(_content, "Добавить мод: скачайте архив .zip с mod.json и нажмите «+» вверху — он найдётся в Download. Удалить мод: удерживайте его карточку.",
                10, y, W - 20, 60, 21, 0), Col(0.5, 0.5, 0.5));
            SetContentHeight(_content, W, y + 90 + (Pending.Count > 0 ? 220 : 0));
            ShowBanner();
        }

        // ---------------------------------------------------------------- поиск архивов («+»)

        private class PickGroup { public string Id, Name, InstalledVersion; public List<Candidate> Items = new List<Candidate>(); }

        /// <summary>Группирует найденные архивы по id мода: одна запись на мод, версии от новой к старой.</summary>
        private static List<PickGroup> GroupCandidates(List<Candidate> found)
        {
            var map = new Dictionary<string, PickGroup>();
            foreach (var c in found)
            {
                if (!map.TryGetValue(c.Id, out var g)) map[c.Id] = g = new PickGroup { Id = c.Id, Name = c.Name, InstalledVersion = c.InstalledVersion };
                if (!g.Items.Any(x => (x.Version ?? "") == (c.Version ?? ""))) g.Items.Add(c);
            }
            foreach (var g in map.Values) g.Items.Sort((x, y) => Core.CompareVersions(y.Version ?? "0", x.Version ?? "0"));
            int Rank(PickGroup g) => g.Items.Any(i => i.Relation == "update") ? 0 : g.Items.Any(i => i.Relation == "new") ? 1 : 2;
            return map.Values.Where(g => g.Items.Any(i => i.Relation != "same") || g.Items.Count > 1)
                .OrderBy(Rank).ThenBy(g => g.Name, StringComparer.CurrentCulture).ToList();
        }

        /// <summary>Окно поиска по «+»: моды из Download/ModKit и Download. У мода с несколькими версиями — стрелка и список версий.</summary>
        private static void ShowPicker(string message = "", IEnumerable<string> expandedIds = null)
        {
            var cands = External.FindCandidates();
            var groups = GroupCandidates(cands);
            var expanded = new HashSet<string>(expandedIds ?? Array.Empty<string>());
            var dim = Node("ModKitPicker", CanvasTf);
            Stretch(dim.rt);
            var dimImg = AddImage(dim.go, 0, 0, 0, 0.8);
            dim.go.AddComponent<Button>().targetGraphic = dimImg;
            double h = ScreenH - 120;
            var box = Node("box", dim.rt);
            Place(box.rt, 40, 60, LogicalW - 80, h);
            AddImage(box.go, 0.09, 0.09, 0.11, 1);
            Tint(Label(box.rt, "Найденные моды", 20, 16, LogicalW - 120, 70, 38, 4), Gold);
            Tint(Label(box.rt, message != "" ? message : groups.Count > 0
                ? "Download и Download/ModKit. Стрелка ▸ раскрывает список версий. Список можно листать."
                : "Новых архивов с mod.json в Download не найдено. Скопируйте .zip мода в Download/ModKit.", 30, 90, LogicalW - 140, 70, 21, 0), Col(0.7, 0.7, 0.7));

            double viewTop = 170, viewH = h - viewTop - 100, W = LogicalW - 140;
            var list = AddScroll(box.rt, 30, viewTop, W, viewH);

            void Install(Candidate cand)
            {
                var open = expanded.ToList();
                SafeDestroy(dim.go);
                string msg;
                try
                {
                    msg = External.InstallCandidate(cand);
                    if ((cand.Relation == "update" || cand.Relation == "downgrade") && !Updated.Contains(cand.Name ?? cand.Id)) Updated.Add(cand.Name ?? cand.Id);
                }
                catch (Exception e) { msg = $"Ошибка ({cand.File}): {e.Message}"; }
                Log.Write("«+»: " + msg);
                _status = msg;
                Rebuild();
                ShowPicker(msg, open);
            }
            string Verb(Candidate i) => i.Relation == "update" ? "обновить" : i.Relation == "downgrade" ? "откатить" : i.Relation == "same" ? "установлена" : "установить";

            void Render()
            {
                ClearChildren(list);
                double rowH = 92, gap = 14, y = 0;
                foreach (var g in groups)
                {
                    var group = g;
                    var cur = g.InstalledVersion != null ? $"установлена v{g.InstalledVersion}" : "не установлен";
                    if (g.Items.Count == 1)
                    {
                        var i = g.Items[0];
                        var text = i.Relation == "new" ? $"Установить: {g.Name}  (v{i.Version ?? "?"})"
                            : $"{(i.Relation == "update" ? "Обновить" : "Откатить")}: {g.Name}  (v{g.InstalledVersion ?? "?"} → v{i.Version ?? "?"})";
                        AddButton(list, text, 0, y, W, rowH, 25, i.Relation == "downgrade" ? new[] { 0.40, 0.30, 0.16, 1 } : new[] { 0.18, 0.28, 0.42, 1 }, () => Install(i));
                        y += rowH + gap;
                        continue;
                    }
                    bool open = expanded.Contains(g.Id);
                    AddButton(list, $"{(open ? "▾" : "▸")} {g.Name}  ({cur}, версий: {g.Items.Count})", 0, y, W, rowH, 25, new[] { 0.14, 0.22, 0.34, 1 }, () =>
                    {
                        if (!expanded.Remove(group.Id)) expanded.Add(group.Id);
                        Render();
                    });
                    y += rowH + gap;
                    if (!open) continue;
                    foreach (var i in g.Items)
                    {
                        var cand = i;
                        var rel = i.Relation;
                        var col = rel == "same" ? new[] { 0.20, 0.20, 0.20, 1 } : rel == "downgrade" ? new[] { 0.40, 0.30, 0.16, 1 } : new[] { 0.18, 0.38, 0.30, 1 };
                        AddButton(list, $"v{i.Version ?? "?"}  —  {Verb(i)}", 60, y, W - 60, rowH - 8, 24, col, () => { if (rel != "same") Install(cand); });
                        y += rowH - 8 + gap;
                    }
                }
                if (groups.Count == 0) y = 0;
                SetContentHeight(list, W, Math.Max(y, viewH));
            }
            Render();
            AddButton(box.rt, "Закрыть", 30, h - 90, LogicalW - 140, 76, 28, new[] { 0.38, 0.20, 0.20, 1 }, () => SafeDestroy(dim.go));
        }

        // ---------------------------------------------------------------- построение окна

        /// <summary>Подходящий экранный холст игры (не WorldSpace, корневой, активный) — к нему подвешиваем меню.</summary>
        internal static Canvas FindGameCanvas()
        {
            Canvas best = null;
            int bestOrder = int.MinValue;
            foreach (var obj in UnityEngine.Object.FindObjectsOfType(Il2CppInterop.Runtime.Il2CppType.Of<Canvas>()))
            {
                try
                {
                    var c = obj.TryCast<Canvas>();
                    if (c == null) continue;
                    bool on = c.enabled && c.gameObject.activeInHierarchy;
                    Log.Write($"холст «{c.gameObject.name}»: mode={c.renderMode} root={c.isRootCanvas} active={on} order={c.sortingOrder} scale={c.scaleFactor}");
                    if (c.renderMode != RenderMode.WorldSpace && c.isRootCanvas && on && c.sortingOrder >= bestOrder) { best = c; bestOrder = c.sortingOrder; }
                }
                catch (Exception e) { Log.Write("холст: " + e.Message); }
            }
            return best ?? throw new Exception("подходящий Canvas игры не найден");
        }

        /// <summary>Иконка гаечного ключа из прямоугольников uGUI (текстуры недоступны, см. UiKit.PicturesSupported).</summary>
        private static void BuildWrench(RectTransform parent)
        {
            var ink = new[] { 0.96, 0.96, 0.88, 1 };
            void Bar(double cx, double cy, double w, double h, double angle, double[] c)
            {
                var n = Node("w", parent);
                n.rt.anchorMin = n.rt.anchorMax = n.rt.pivot = V2(0.5, 0.5);
                n.rt.anchoredPosition = V2(cx * K, cy * K);
                n.rt.sizeDelta = V2(w * K, h * K);
                n.rt.localEulerAngles = new Vector3(0, 0, (float)angle);
                var im = AddImage(n.go, c[0], c[1], c[2], c[3]);
                im.raycastTarget = false;
            }
            Bar(-4, -4, 14, 56, -45, ink);             // рукоятка по диагонали
            Bar(14, 14, 34, 34, 45, ink);               // головка
            Bar(21, 21, 14, 22, 45, new[] { 0.0, 0, 0, 1 });   // зев (чёрный, как фон)
        }

        private static void BuildUi(A_MainMenuInformation menu)
        {
            Stage("шрифт");
            UiKit.Font = Z_Database.Font_Apply_Medium;

            var gameCanvas = FindGameCanvas();
            Stage("параметры холста");
            float scale = gameCanvas.scaleFactor;
            K = Screen.width / scale / LogicalW;
            ScreenH = Screen.height / scale / K;
            CanvasTf = gameCanvas.transform;

            Stage("окно");
            // окно — растянутый на весь холст дочерний объект (рисуется поверх всего)
            var win = Node("ModKitWindow", CanvasTf);
            _rootGo = win.go;
            Stretch(win.rt);
            _panel = win.go;
            _winRt = win.rt;
            AddImage(win.go, 0, 0, 0, 1);                                    // чёрный фон, как у энциклопедии

            Stage("заголовок и кнопки");
            Tint(Label(win.rt, "Моды", 0, 100, LogicalW, 80, 52, 4), Gold);
            AddButton(win.rt, "X", LogicalW - 100, 70, 80, 80, 54, new[] { 0, 0, 0, 0.01 }, () => _panel.SetActive(false));

            // «+»: подключить новые моды из папки. В шрифте игры может не быть «+», поэтому крест — две белые полоски на зелёной кнопке
            AddButton(win.rt, "", 20, 70, 80, 80, 64, new[] { 0.1, 0.65, 0.25, 1 }, () =>
            {
                try { ShowPicker(); } catch (Exception e) { Log.Write("поиск модов: " + e); }
            });
            foreach (var bar in new[] { new[] { 34, 106, 52, 8 }, new[] { 56, 84, 8, 52 } })
            {
                var n = Node("plus", win.rt);
                Place(n.rt, bar[0], bar[1], bar[2], bar[3]);
                AddImage(n.go, 1, 1, 1, 1).raycastTarget = false;
            }

            Stage("список");
            double viewTop = 210;
            _content = AddScroll(win.rt, 30, viewTop, LogicalW - 60, ScreenH - viewTop - 30);
            Rebuild();
            _panel.SetActive(false);

            Stage("запись на главной");
            // запись «Моды» на главной странице — внутри поля главного меню, над иконкой «Статистика» (на экране 720x1640 она в (610, 1460))
            var fieldTf = menu.Obj_FieldMainMenu.transform;
            var entry = Node("ModKitEntry", fieldTf);
            _entryGo = entry.go;
            PlaceAtScreenCenter(entry.rt, 610, 1335 * ScreenH / 1640, 100, 100);
            var eImg = AddImage(entry.go, 0, 0, 0, 0.01);
            var eBtn = entry.go.AddComponent<Button>();
            eBtn.targetGraphic = eImg;
            eBtn.onClick.AddListener(Act(() =>
            {
                try { Rebuild(); _panel.SetActive(true); } catch (Exception e) { Log.Write("ошибка открытия: " + e); }
            }));
            BuildWrench(entry.rt);

            Stage("notifyMainMenu и диалог");
            Core.NotifyMainMenu(menu);
            var diff = Core.SessionDiff();
            if (diff != null && !_dialogShown)
            {
                _dialogShown = true;
                string L(List<string> a) => a.Count > 0 ? string.Join(", ", a) : "без модов";
                ShowDialog("Моды изменились",
                    $"Прошлая игра шла с: {L(diff.Before)}.\n\nСейчас включены: {L(diff.Now)}.\n\nПродолжить с прежним набором модов или начать новую сессию с текущим?",
                    new (string, Action)[] { ("Как раньше", () => { Core.RestoreSession(); Rebuild(); }), ("Как сейчас", () => Core.MarkSession()) }, 120);
            }
            Log.Write($"меню «Моды» создано (экран {Math.Round(LogicalW)}x{Math.Round(ScreenH)}, k={K:0.00})");
        }

        private static bool EntryAlive(A_MainMenuInformation self)
        {
            try
            {
                if (_entryGo == null) return false;                        // Unity-сравнение: уничтоженный объект == null
                var parent = _entryGo.transform.parent;
                return parent != null && parent.Pointer == self.Obj_FieldMainMenu.transform.Pointer;
            }
            catch { return false; }
        }

        /// <summary>
        /// Строит окно из A_MainMenuInformation.Update — он вызывается каждый кадр на главном потоке и только на главном экране.
        /// При каждом новом экземпляре (возврат в меню) окно пересоздаётся.
        /// </summary>
        internal static void OnMenuUpdate(A_MainMenuInformation self)
        {
            if (_failures >= 3) return;
            // Адрес объекта меню после боя может совпасть со старым, а сцена главного меню пересоздаётся — тогда запись «Моды»
            // уничтожена вместе со старой сценой. Поэтому раз в ~30 кадров проверяем, что кнопка жива и висит в нынешнем меню.
            if (self.Pointer == _lastSelf)
            {
                if (++_calls % 30 != 0 || EntryAlive(self)) return;
                Log.Write("меню MODS: кнопка пропала (возврат из боя) — строю заново");
            }
            _lastSelf = self.Pointer;
            try
            {
                SafeDestroy(_rootGo);
                SafeDestroy(_entryGo);
                BuildUi(self);
            }
            catch (Exception e)
            {
                _failures++;
                Log.Write($"меню: {e} (этап: {_stage})");
                SafeDestroy(_rootGo);
                SafeDestroy(_entryGo);
            }
        }
    }
}
