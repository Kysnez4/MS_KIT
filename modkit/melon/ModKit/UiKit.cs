using System;
using System.Collections.Generic;
using System.Linq;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace ModKit
{
    /// <summary>
    /// Общие инструменты построения Unity uGUI (холст игры, позиции в «логических пикселях» от левого верхнего угла).
    /// Используются окном «Моды» и модами с собственными экранами.
    /// </summary>
    public static partial class UiKit
    {
        /// <summary>Ширина «логического» экрана; всё остальное масштабируется по ней.</summary>
        public const float LogicalW = 720;
        public const float ButtonRadius = 16;

        /// <summary>Единиц холста на логический пиксель (выставляется при построении меню).</summary>
        public static float K = 1;
        public static Font Font;
        public static float ScreenH = 1280;
        public static Transform CanvasTf;

        public static readonly Color Gold = new Color(0.97f, 0.85f, 0.55f, 1);

        public static Color Col(double r, double g, double b, double a = 1) => new Color((float)r, (float)g, (float)b, (float)a);
        public static Vector2 V2(double x, double y) => new Vector2((float)x, (float)y);

        /// <summary>Обработчик с перехватом исключений: делегаты для Unity создаём из методов открытого класса (замыкания на устройстве падают).</summary>
        public sealed class Callback
        {
            private readonly Action _fn; private readonly string _what;
            public Callback(Action fn, string what) { _fn = fn; _what = what; }
            public void Invoke()
            {
                try { _fn(); } catch (Exception e) { Log.Write($"ошибка ({_what}): {e}"); }
            }
        }

        public static UnityAction Act(Action a) => DelegateSupport.ConvertDelegate<UnityAction>(a);

        public static UnityAction<T> Act<T>(Action<T> a) => DelegateSupport.ConvertDelegate<UnityAction<T>>(a);

        public static void SafeDestroy(GameObject go)
        {
            if (go == null) return;
            try { UnityEngine.Object.Destroy(go); } catch { /* уже удалён */ }
        }

        public static (GameObject go, RectTransform rt) Node(string name, Transform parent)
        {
            var go = new GameObject(name);
            var rt = go.AddComponent<RectTransform>();
            if (parent != null) rt.SetParent(parent, false);
            return (go, rt);
        }

        /// <summary>Позиция/размер в логических пикселях от левого верхнего угла родителя (якорь и pivot — левый верхний угол).</summary>
        public static void Place(RectTransform rt, double x, double y, double w, double h)
        {
            rt.anchorMin = V2(0, 1);
            rt.anchorMax = V2(0, 1);
            rt.pivot = V2(0, 1);
            rt.anchoredPosition = V2(x * K, -y * K);
            rt.sizeDelta = V2(w * K, h * K);
        }

        /// <summary>Позиция по центру элемента в логических пикселях экрана; для родителя с центральным якорем в центре холста.</summary>
        public static void PlaceAtScreenCenter(RectTransform rt, double cx, double cy, double w, double h)
        {
            rt.anchorMin = V2(0.5, 0.5);
            rt.anchorMax = V2(0.5, 0.5);
            rt.pivot = V2(0.5, 0.5);
            rt.anchoredPosition = V2((cx - LogicalW / 2) * K, (ScreenH / 2 - cy) * K);
            rt.sizeDelta = V2(w * K, h * K);
        }

        public static void Stretch(RectTransform rt)
        {
            rt.anchorMin = V2(0, 0);
            rt.anchorMax = V2(1, 1);
            rt.offsetMin = V2(0, 0);
            rt.offsetMax = V2(0, 0);
        }

        public static Image AddImage(GameObject go, double r, double g, double b, double a)
        {
            var img = go.AddComponent<Image>();
            img.color = Col(r, g, b, a);
            return img;
        }

        /// <summary>align: 0 = UpperLeft, 3 = MiddleLeft, 4 = MiddleCenter, 5 = MiddleRight.</summary>
        public static Text Label(Transform parent, string text, double x, double y, double w, double h, double size, int align = 3)
        {
            var n = Node("txt", parent);
            Place(n.rt, x, y, w, h);
            var t = n.go.AddComponent<Text>();
            if (Font != null) t.font = Font;
            t.text = text;
            t.fontSize = (int)Math.Round(size * K);
            t.alignment = (TextAnchor)align;
            t.color = Col(1, 1, 1);
            t.raycastTarget = false;
            return t;
        }

        public static Text Tint(Text t, Color c) { t.color = c; return t; }

        // ---------------------------------------------------------------- кнопки

        /// <summary>Кнопка с подписью. Непрозрачные кнопки скруглены (текстура через <see cref="Raw"/>), прозрачные — «хитбоксы».</summary>
        public static Button AddButton(Transform parent, string text, double x, double y, double w, double h, double size, double[] bg, Action onClick)
        {
            var n = Node("btn", parent);
            Place(n.rt, x, y, w, h);
            Graphic img = null;
            if (bg[3] >= 0.5)
            {
                var tex = RoundedTexture((int)Math.Round(w), (int)Math.Round(h), (int)Math.Min(ButtonRadius, Math.Floor(Math.Min(w, h) / 2)));
                if (tex != null)
                {
                    var raw = n.go.AddComponent<RawImage>();
                    SetTexture(raw, tex);
                    raw.color = Col(bg[0], bg[1], bg[2], bg[3]);
                    img = raw;
                }
            }
            img ??= AddImage(n.go, bg[0], bg[1], bg[2], bg[3]);
            var btn = n.go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(Act(new Callback(onClick, "клик").Invoke));
            Label(n.rt, text, 0, 0, w, h, size, 4);
            return btn;
        }

        /// <summary>
        /// Поле ввода числа: по касанию открывается цифровая клавиатура. onCommit вызывается по завершении ввода
        /// (кнопка «Готово» или потеря фокуса) с введённым текстом.
        /// </summary>
        public static InputField AddNumberField(Transform parent, string value, double x, double y, double w, double h, double size, bool decimalValue, Action<string> onCommit)
        {
            var n = Node("input", parent);
            Place(n.rt, x, y, w, h);
            var bg = AddImage(n.go, 0.14, 0.14, 0.10, 1);
            var txt = Label(n.rt, value, 8, 0, w - 16, h, size, 4);
            Tint(txt, Gold);
            var input = n.go.AddComponent<InputField>();
            input.targetGraphic = bg;
            input.textComponent = txt;
            input.contentType = decimalValue ? InputField.ContentType.DecimalNumber : InputField.ContentType.IntegerNumber;
            input.lineType = InputField.LineType.SingleLine;
            input.characterLimit = 10;
            input.text = value;
            input.onEndEdit.AddListener(Act<string>(s =>
            {
                try { onCommit(s ?? ""); } catch (Exception e) { Log.Write("ввод числа: " + e); }
            }));
            return input;
        }

        public static void ClearChildren(Transform rt)
        {
            for (int i = rt.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(rt.GetChild(i).gameObject);
        }

        public static string Fmt(double v) => (Math.Round(v * 100) / 100).ToString(System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>Область с прокруткой пальцем: возвращает RectTransform содержимого (его высоту задаёт вызывающий через SetContentHeight).</summary>
        public static RectTransform AddScroll(Transform parent, double x, double y, double w, double h)
        {
            var view = Node("view", parent);
            Place(view.rt, x, y, w, h);
            AddImage(view.go, 0, 0, 0, 0.01);                        // нужен для приёма касаний
            view.go.AddComponent<RectMask2D>();
            var c = Node("content", view.rt);
            Place(c.rt, 0, 0, w, 100);
            var sr = view.go.AddComponent<ScrollRect>();
            sr.content = c.rt;
            sr.viewport = view.rt;
            sr.horizontal = false;
            sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.scrollSensitivity = 30;
            return c.rt;
        }

        public static void SetContentHeight(RectTransform content, double w, double h) => content.sizeDelta = V2(w * K, h * K);

        // ---------------------------------------------------------------- окна

        public class ChecklistItem { public string Key, Label; }
        public class ChecklistSection { public string Header; public List<ChecklistItem> Items = new List<ChecklistItem>(); }
        public class ChecklistOptions
        {
            /// <summary>Имя объекта окна: повторное открытие с тем же именем заменяет прежнее окно.</summary>
            public string Name, Title, Subtitle;
            public List<ChecklistSection> Sections = new List<ChecklistSection>();
            public Func<string, bool> Get;
            public Action<string, bool> Set;
        }

        public class NumberFormField { public string Key; public double Min, Max; public bool Decimal; }
        public class NumberFormRow { public string Label; public List<NumberFormField> Fields = new List<NumberFormField>(); }
        public class NumberFormOptions
        {
            public string Name, Title, Subtitle;
            /// <summary>Подписи над столбцами полей (по одной на поле строки).</summary>
            public List<string> Columns;
            public List<NumberFormRow> Rows = new List<NumberFormRow>();
            public Func<string, double> Get;
            public Action<string, double> Set;
        }

        private static readonly Dictionary<string, GameObject> Windows = new Dictionary<string, GameObject>();

        private static (GameObject go, RectTransform rt, double top) OpenWindow(string name, string title, string subtitle, Action onClose = null)
        {
            if (Windows.TryGetValue(name, out var prev)) SafeDestroy(prev);
            var win = Node(name, CanvasTf);
            Windows[name] = win.go;
            Stretch(win.rt);
            AddImage(win.go, 0.02, 0.03, 0.06, 1);
            Tint(Label(win.rt, title, 0, 90, LogicalW, 80, 46, 4), Gold);
            if (!string.IsNullOrEmpty(subtitle)) Tint(Label(win.rt, subtitle, 30, 170, LogicalW - 60, 60, 21, 4), Col(0.7, 0.75, 0.85));
            AddButton(win.rt, "X", LogicalW - 100, 60, 80, 80, 54, new[] { 0, 0, 0, 0.01 }, () => { win.go.SetActive(false); try { onClose?.Invoke(); } catch (Exception e) { Log.Write("закрытие окна: " + e.Message); } });
            return (win.go, win.rt, string.IsNullOrEmpty(subtitle) ? 190 : 240);
        }

        /// <summary>Окно «список галочек» на весь холст, с прокруткой. Нажатие на строку переключает галочку и сразу вызывает Set.</summary>
        public static GameObject OpenChecklist(ChecklistOptions o)
        {
            var win = OpenWindow(o.Name, o.Title, o.Subtitle);
            double W = LogicalW - 60;
            var c = AddScroll(win.rt, 30, win.top, W, ScreenH - win.top - 30);
            double y = 0;
            foreach (var sec in o.Sections)
            {
                Tint(Label(c, sec.Header, 10, y, W - 20, 56, 24, 3), Col(0.55, 0.8, 1));
                y += 60;
                foreach (var it in sec.Items)
                {
                    var item = it;
                    var row = Node("row", c);
                    Place(row.rt, 0, y, W, 76);
                    var bg = AddImage(row.go, 0.08, 0.10, 0.15, 1);
                    var btn = row.go.AddComponent<Button>();
                    btn.targetGraphic = bg;
                    var box = Node("box", row.rt);
                    Place(box.rt, 14, 12, 52, 52);
                    AddImage(box.go, 0.35, 0.4, 0.5, 1);
                    var mark = Node("mark", box.rt);
                    Place(mark.rt, 8, 8, 36, 36);
                    AddImage(mark.go, 0.97, 0.85, 0.55, 1);
                    mark.go.SetActive(o.Get(item.Key));
                    Tint(Label(row.rt, item.Label, 84, 0, W - 100, 76, 23, 3), Col(0.92, 0.92, 0.92));
                    btn.onClick.AddListener(Act(() =>
                    {
                        try
                        {
                            bool v = !o.Get(item.Key);
                            o.Set(item.Key, v);
                            mark.go.SetActive(v);
                        }
                        catch (Exception e) { Log.Write("галочка: " + e); }
                    }));
                    y += 82;
                }
                y += 14;
            }
            SetContentHeight(c, W, y + 40);
            return win.go;
        }

        /// <summary>Окно «таблица чисел»: строка = подпись + несколько полей ввода. Значение зажимается в [Min, Max] и сразу передаётся в Set.</summary>
        public static GameObject OpenNumberForm(NumberFormOptions o)
        {
            var win = OpenWindow(o.Name, o.Title, o.Subtitle);
            double W = LogicalW - 60;
            var c = AddScroll(win.rt, 30, win.top, W, ScreenH - win.top - 30);
            int nf = Math.Max(1, o.Rows.Select(r => r.Fields.Count).DefaultIfEmpty(1).Max());
            double labelW = 220, gap = 16;
            double fw = (W - labelW - 20 - gap * nf) / nf;
            double Fx(int i) => labelW + 20 + i * (fw + gap);
            double y = 0;
            if (o.Columns != null)
            {
                for (int i = 0; i < o.Columns.Count; i++) Tint(Label(c, o.Columns[i], Fx(i), y, fw, 50, 24, 4), Col(0.55, 0.8, 1));
                y += 56;
            }
            foreach (var r in o.Rows)
            {
                var row = Node("row", c);
                Place(row.rt, 0, y, W, 76);
                AddImage(row.go, 0.08, 0.10, 0.15, 1);
                Tint(Label(row.rt, r.Label, 20, 0, labelW, 76, 24, 3), Col(0.92, 0.92, 0.92));
                for (int i = 0; i < r.Fields.Count; i++)
                {
                    var f = r.Fields[i];
                    InputField input = null;
                    input = AddNumberField(row.rt, Fmt(o.Get(f.Key)), Fx(i), 8, fw, 60, 32, f.Decimal, text =>
                    {
                        if (!double.TryParse(text.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)) v = o.Get(f.Key);
                        v = Math.Min(f.Max, Math.Max(f.Min, f.Decimal ? v : Math.Round(v)));
                        if (v != o.Get(f.Key)) o.Set(f.Key, v);
                        input.text = Fmt(v);
                    });
                }
                y += 82;
            }
            SetContentHeight(c, W, y + 40);
            return win.go;
        }
    }
}
