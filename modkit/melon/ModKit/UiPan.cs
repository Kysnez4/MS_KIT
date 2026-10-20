using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ModKit
{
    /// <summary>Панорамируемая карта (как экран достижений) и листатель страниц. Только Image/Text/ScrollRect/RectMask2D, уже проверенные в AddScroll.</summary>
    public static partial class UiKit
    {
        public enum PanState { Locked, Available, Done }

        public class PanNode
        {
            public string Id, Label;
            /// <summary>Центр узла в координатах содержимого (логические пиксели от левого верхнего угла).</summary>
            public double X, Y;
            /// <summary>PNG/JPG иконки (необязательно).</summary>
            public byte[] Icon;
            /// <summary>Спрайт игры (указатель, см. UiKit.GameSprite) — рисуется вместо иконки-PNG; у закрытых узлов затемняется.</summary>
            public IntPtr Sprite;
            /// <summary>Степень прокачки 0..1: рамка узла достраивается по сторонам квадрата по часовой стрелке (полный квадрат = 1). −1 — без индикатора.</summary>
            public double Progress = -1;
            public PanState State = PanState.Available;
            public PanNode(string id, double x, double y, byte[] icon = null, string label = null, PanState state = PanState.Available)
            { Id = id; X = x; Y = y; Icon = icon; Label = label; State = state; }
        }

        public class PanEdge { public string From, To; public PanEdge(string from, string to) { From = from; To = to; } }

        public class PanOptions
        {
            public double ContentW = 1400, ContentH = 1400, NodeSize = 96;
            public List<PanNode> Nodes = new List<PanNode>();
            public List<PanEdge> Edges = new List<PanEdge>();
            public Action<PanNode> OnTap;
        }

        public class PanView
        {
            public RectTransform View, Content;
            public ScrollRect Scroll;
            private PanOptions _o;
            private readonly Dictionary<string, Image> _frames = new Dictionary<string, Image>();
            private readonly List<(PanEdge e, Image img)> _lines = new List<(PanEdge, Image)>();

            internal void Init(PanOptions o, RectTransform view, RectTransform content, ScrollRect sr) { _o = o; View = view; Content = content; Scroll = sr; }
            private readonly Dictionary<string, Image> _sprites = new Dictionary<string, Image>();
            internal void AddFrame(string id, Image img) => _frames[id] = img;
            internal void AddSprite(string id, Image img) => _sprites[id] = img;
            private readonly Dictionary<string, Image[]> _bars = new Dictionary<string, Image[]>();
            internal void AddBars(string id, Image[] bars) => _bars[id] = bars;
            internal void AddLine(PanEdge e, Image img) => _lines.Add((e, img));

            public PanNode Find(string id) => _o.Nodes.Find(n => n.Id == id);

            public void SetState(string id, PanState s)
            {
                var n = Find(id);
                if (n == null) return;
                n.State = s;
                Refresh();
            }

            public void Refresh()
            {
                foreach (var n in _o.Nodes)
                {
                    if (_frames.TryGetValue(n.Id, out var f)) f.color = StateColor(n.State);
                    if (_sprites.TryGetValue(n.Id, out var sp)) sp.color = n.State == PanState.Locked ? Col(0.35, 0.35, 0.4) : Col(1, 1, 1);
                }
                double ns = _o.NodeSize, t = Math.Max(6, ns / 14);
                foreach (var n in _o.Nodes)
                {
                    if (!_bars.TryGetValue(n.Id, out var b) || n.Progress < 0) continue;
                    double p = Math.Min(1, n.Progress) * 4;
                    for (int i = 0; i < 4; i++)
                    {
                        double f = Math.Max(0, Math.Min(1, p - i)), len = f * ns;
                        b[i].enabled = len > 0.5;
                        if (len <= 0.5) continue;
                        // верх слева направо, правая сторона сверху вниз, низ справа налево, левая снизу вверх
                        if (i == 0) Place(b[i].rectTransform, 0, 0, len, t);
                        else if (i == 1) Place(b[i].rectTransform, ns - t, 0, t, len);
                        else if (i == 2) Place(b[i].rectTransform, ns - len, ns - t, len, t);
                        else Place(b[i].rectTransform, 0, ns - len, t, len);
                    }
                }
                foreach (var (e, img) in _lines) img.color = LineColor(Find(e.From), Find(e.To));
            }

            /// <summary>Прокрутить так, чтобы узел оказался по центру окна (насколько позволяют границы).</summary>
            public void CenterOn(string id)
            {
                var n = Find(id);
                if (n == null) return;
                var vs = View.rect.size; var cs = Content.rect.size;
                double px = Math.Max(0, Math.Min(cs.x - vs.x, n.X * K - vs.x / 2));
                double py = Math.Max(0, Math.Min(cs.y - vs.y, n.Y * K - vs.y / 2));
                Content.anchoredPosition = V2(-px, py);
            }
        }

        private static Color StateColor(PanState s) =>
            s == PanState.Done ? Col(0.97, 0.85, 0.55) : s == PanState.Available ? Col(0.45, 0.75, 1) : Col(0.28, 0.30, 0.36);

        private static Color LineColor(PanNode a, PanNode b) =>
            a != null && b != null && a.State != PanState.Locked && b.State != PanState.Locked ? Col(0.97, 0.85, 0.55, 0.9) : Col(0.35, 0.38, 0.45, 0.9);

        /// <summary>Карта с панорамированием пальцем в обе стороны (ScrollRect по двум осям + RectMask2D). Окно x,y,w,h — в логических пикселях родителя.</summary>
        public static PanView AddPanView(Transform parent, double x, double y, double w, double h, PanOptions o)
        {
            var pv = new PanView();
            var view = Node("pan", parent);
            Place(view.rt, x, y, w, h);
            AddImage(view.go, 0.03, 0.05, 0.09, 1);                   // фон и приём касаний
            view.go.AddComponent<RectMask2D>();
            var c = Node("content", view.rt);
            Place(c.rt, 0, 0, Math.Max(o.ContentW, w), Math.Max(o.ContentH, h));
            var sr = view.go.AddComponent<ScrollRect>();
            sr.content = c.rt;
            sr.viewport = view.rt;
            sr.horizontal = true;
            sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.scrollSensitivity = 30;
            pv.Init(o, view.rt, c.rt, sr);

            var byId = new Dictionary<string, PanNode>();
            foreach (var n in o.Nodes) byId[n.Id] = n;
            // линии под узлами
            foreach (var e in o.Edges)
            {
                if (!byId.TryGetValue(e.From, out var a) || !byId.TryGetValue(e.To, out var b)) continue;
                double dx = b.X - a.X, dy = b.Y - a.Y, len = Math.Sqrt(dx * dx + dy * dy);
                if (len < 1) continue;
                var ln = Node("edge", c.rt);
                ln.rt.anchorMin = V2(0, 1); ln.rt.anchorMax = V2(0, 1); ln.rt.pivot = V2(0.5, 0.5);
                ln.rt.anchoredPosition = V2((a.X + b.X) / 2 * K, -(a.Y + b.Y) / 2 * K);
                ln.rt.sizeDelta = V2(len * K, 8 * K);
                ln.rt.localEulerAngles = new Vector3(0, 0, (float)(Math.Atan2(-dy, dx) * 180.0 / Math.PI));
                var img = AddImage(ln.go, 1, 1, 1, 1);
                img.raycastTarget = false;
                pv.AddLine(e, img);
            }
            double ns = o.NodeSize;
            foreach (var n in o.Nodes)
            {
                var node = n;
                var nd = Node("node", c.rt);
                Place(nd.rt, n.X - ns / 2, n.Y - ns / 2, ns, ns);
                var frame = AddImage(nd.go, 1, 1, 1, 1);
                var inner = Node("in", nd.rt);
                Place(inner.rt, 6, 6, ns - 12, ns - 12);
                AddImage(inner.go, 0.08, 0.10, 0.15, 1).raycastTarget = false;
                if (n.Sprite != IntPtr.Zero && !Raw.IsDestroyed(n.Sprite))
                {
                    var sp = Node("sprite", inner.rt);
                    Place(sp.rt, 2, 2, ns - 16, ns - 16);
                    var simg = AddImage(sp.go, 1, 1, 1, 1);
                    simg.raycastTarget = false;
                    SetSprite(simg, n.Sprite);
                    simg.preserveAspect = true;
                    pv.AddSprite(n.Id, simg);
                }
                else if (n.Icon != null) Picture(inner.rt, n.Icon, 4, 4, ns - 20, ns - 20);
                var btn = nd.go.AddComponent<Button>();
                btn.targetGraphic = frame;
                btn.onClick.AddListener(Act(new Callback(() => o.OnTap?.Invoke(node), "узел карты").Invoke));
                if (!string.IsNullOrEmpty(n.Label))
                    Label(c.rt, n.Label, n.X - 90, n.Y + ns / 2 + 2, 180, 40, 20, 4).raycastTarget = false;
                if (n.Progress >= 0)
                {
                    var bars = new Image[4];
                    for (int i = 0; i < 4; i++)
                    {
                        var bn = Node("progress", nd.rt);
                        bars[i] = AddImage(bn.go, 0.35, 0.95, 0.45, 1);
                        bars[i].raycastTarget = false;
                    }
                    pv.AddBars(n.Id, bars);
                }
                pv.AddFrame(n.Id, frame);
            }
            pv.Refresh();
            return pv;
        }

        /// <summary>Спрайт из статического массива Z_Database (например SpriteList_ClassPortrait): указатель или Zero, если элемента нет. Вставлять в узлы карты и Image (UiKit.SetSprite) — не оборачивать в Sprite из Il2CppInterop (роняет игру).</summary>
        public static unsafe IntPtr GameSprite(string zdbField, int index)
        {
            try
            {
                var zdb = Raw.Class("Assembly-CSharp.dll", "", "Z_Database");
                IntPtr arr = IntPtr.Zero;
                Il2CppInterop.Runtime.IL2CPP.il2cpp_field_static_get_value(Il2CppInterop.Runtime.IL2CPP.GetIl2CppField(zdb, zdbField), &arr);
                if (arr == IntPtr.Zero || index < 0 || index >= System.Runtime.InteropServices.Marshal.ReadInt32(arr + 0x18)) return IntPtr.Zero;
                var sp = System.Runtime.InteropServices.Marshal.ReadIntPtr(arr + 0x20 + index * IntPtr.Size);
                return Raw.IsDestroyed(sp) ? IntPtr.Zero : sp;
            }
            catch { return IntPtr.Zero; }
        }

        /// <summary>Пустое окно на весь холст (заголовок, подпись, кнопка X): возвращает корень и верхнюю границу области для содержимого.</summary>
        public static (GameObject go, RectTransform rt, double top) OpenPage(string name, string title, string subtitle) => OpenWindow(name, title, subtitle, null);

        /// <summary>То же, с действием при закрытии окна кнопкой X.</summary>
        public static (GameObject go, RectTransform rt, double top) OpenPage(string name, string title, string subtitle, Action onClose) => OpenWindow(name, title, subtitle, onClose);

        /// <summary>Окно на весь холст с картой.</summary>
        public static PanView OpenPanWindow(string name, string title, string subtitle, PanOptions o) => OpenPanWindow(name, title, subtitle, o, 30, null);

        public static PanView OpenPanWindow(string name, string title, string subtitle, PanOptions o, double bottom) => OpenPanWindow(name, title, subtitle, o, bottom, null);

        /// <summary>Карта в окне: bottom — отступ снизу (для своих подписей под картой), onClose — действие при закрытии кнопкой X.</summary>
        public static PanView OpenPanWindow(string name, string title, string subtitle, PanOptions o, double bottom, Action onClose)
        {
            var win = OpenWindow(name, title, subtitle, onClose);
            return AddPanView(win.rt, 30, win.top, LogicalW - 60, ScreenH - win.top - bottom, o);
        }

        // ---------------------------------------------------------------- листатель

        public class PagerOptions
        {
            /// <summary>Заголовки страниц (их число = число страниц).</summary>
            public List<string> Pages = new List<string>();
            public int Start = 0;
            public bool Loop = false;
            /// <summary>Смена страницы: индекс, заголовок, чистый контейнер страницы (его надо заполнить).</summary>
            public Action<int, string, RectTransform> OnPage;
        }

        public class Pager
        {
            public int Page { get; private set; }
            public RectTransform Content;
            private PagerOptions _o;
            private Text _label;

            internal void Init(PagerOptions o, RectTransform content, Text label) { _o = o; Content = content; _label = label; Page = o.Start; }
            public int Count => _o.Pages.Count;

            public void Next() => Go(Page + 1);
            public void Prev() => Go(Page - 1);

            public void Go(int i)
            {
                int n = Count;
                if (n == 0) return;
                if (_o.Loop) i = ((i % n) + n) % n; else i = Math.Max(0, Math.Min(n - 1, i));
                Page = i;
                _label.text = $"{_o.Pages[i]}   {i + 1} / {n}";
                ClearChildren(Content);
                try { _o.OnPage?.Invoke(i, _o.Pages[i], Content); } catch (Exception e) { Log.Write("страница: " + e); }
            }
        }

        /// <summary>Листатель: полоса со стрелками &lt; &gt; и подписью, под ней область страницы высотой h - 80.</summary>
        public static Pager AddPager(Transform parent, double x, double y, double w, double h, PagerOptions o)
        {
            var pg = new Pager();
            var root = Node("pager", parent);
            Place(root.rt, x, y, w, h);
            var label = Label(root.rt, "", 90, 0, w - 180, 70, 26, 4);
            Tint(label, Gold);
            var body = Node("page", root.rt);
            Place(body.rt, 0, 80, w, Math.Max(10, h - 80));
            AddImage(body.go, 0.05, 0.07, 0.12, 0.95).raycastTarget = false;     // фон страницы (рисует листатель; ClearChildren его не трогает)
            pg.Init(o, body.rt, label);
            var bg = new[] { 0.08, 0.10, 0.15, 1.0 };
            AddButton(root.rt, "<", 0, 0, 80, 70, 40, bg, () => pg.Prev());
            AddButton(root.rt, ">", w - 80, 0, 80, 70, 40, bg, () => pg.Next());
            pg.Go(o.Start);
            return pg;
        }
    }
}
