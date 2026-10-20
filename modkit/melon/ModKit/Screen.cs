using System;
using System.Collections.Generic;
using System.Linq;
using Il2Cpp;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.UI;

namespace ModKit
{
    /// <summary>Кнопка или подпись на экране. Координаты — пиксели экрана от левого верхнего угла (те же, что у adb input tap).</summary>
    public class UiElement
    {
        public string Name, Path, Text;
        /// <summary>"button" | "text".</summary>
        public string Kind;
        public double X, Y, W, H;
        public double CenterX => X + W / 2;
        public double CenterY => Y + H / 2;
        /// <summary>Кнопку можно нажать (interactable и видна на экране).</summary>
        public bool Clickable;
        public GameObject Raw;
        public override string ToString() => $"{Kind} [{X:0},{Y:0} {W:0}x{H:0}] «{Text}» {Name}{(Clickable || Kind == "text" ? "" : " (недоступна)")}";
    }

    /// <summary>Где сейчас игра и что на экране.</summary>
    public interface IScreen
    {
        /// <summary>Экран: "battle", "paused" (бой на паузе), "spellSelect" (выбор заклинания), "artifactSelect" (выбор артефакта), "mainMenu", "dead" (герой мёртв), "loading" (ничего из перечисленного).</summary>
        string Name { get; }
        /// <summary>Размер экрана в пикселях.</summary>
        Vector2 Size { get; }
        /// <summary>Активные кнопки (Button) с положением; visibleOnly — только те, что на экране и доступны.</summary>
        IReadOnlyList<UiElement> Buttons(bool visibleOnly = true);
        /// <summary>Видимые надписи (Text) с положением.</summary>
        IReadOnlyList<UiElement> Texts();
        /// <summary>Первая кнопка, у которой текст или имя содержит s (без учёта регистра); null — нет.</summary>
        UiElement FindButton(string s);
        /// <summary>Нажимает кнопку как палец (выбирает её в EventSystem и вызывает onClick).</summary>
        bool Click(UiElement e);
        /// <summary>Нажимает кнопку, найденную по тексту или имени.</summary>
        bool Click(string textOrName);
    }

    internal static class ScreenApi
    {
        private static double _menuAt = -100;
        private static bool _hooked;
        private static readonly List<Action> Cleanups = new List<Action>();
        private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();

        private static void EnsureHooks()
        {
            if (_hooked) return;
            _hooked = true;
            try { Hooks.AddAfter("screen", "A_MainMenuInformation", "Update", 0, (s, a) => _menuAt = Clock.Elapsed.TotalSeconds, Cleanups); }
            catch (Exception e) { Log.Write("screen: хук меню: " + e.Message); _hooked = false; }
        }

        private class ScreenImpl : IScreen
        {
            public Vector2 Size => new Vector2(UnityEngine.Screen.width, UnityEngine.Screen.height);

            public string Name
            {
                get
                {
                    if (Clock.Elapsed.TotalSeconds - _menuAt < 0.7) return "mainMenu";
                    bool battle = BattleState.InBattle;
                    try { if (V_Ability_Manager.Ability_SelectON) return "spellSelect"; } catch { }
                    if (ArtifactOffer()) return "artifactSelect";
                    if (battle) return BattleState.Paused ? "paused" : "battle";
                    try { if (C_Battle_AllyUnit.Unit_Hp <= 0 && BattleState.HeroKnown) return "dead"; } catch { }
                    return "loading";
                }
            }

            private static bool ArtifactOffer()
            {
                try
                {
                    Crash.Enter("screen", "окно артефактов");
                    foreach (var o in UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<V_Artifact_Manager>()))
                    {
                        var f = o.TryCast<V_Artifact_Manager>()?.Obj_Field_ArtifactSelect;
                        if (f != null && f.activeInHierarchy) return true;
                    }
                }
                catch { }
                return false;
            }

            private static (double x, double y, double w, double h)? Rect(RectTransform rt)
            {
                try
                {
                    // холст Overlay: мировые координаты = пиксели экрана (начало слева внизу). Считаем по rect и масштабу, без GetWorldCorners
                    var r = rt.rect; var pos = rt.position; var sc = rt.lossyScale; var pv = rt.pivot;
                    double w = Math.Abs(r.width * sc.x), h = Math.Abs(r.height * sc.y);
                    double left = pos.x - pv.x * w, bottom = pos.y - pv.y * h;
                    return (left, UnityEngine.Screen.height - (bottom + h), w, h);
                }
                catch { return null; }
            }

            private static string PathOf(Transform t)
            {
                var parts = new List<string>();
                for (int i = 0; t != null && i < 6; i++) { parts.Add(t.name); t = t.parent; }
                parts.Reverse();
                return string.Join("/", parts);
            }

            private static bool OnScreen((double x, double y, double w, double h) r)
                => r.w > 1 && r.h > 1 && r.x + r.w > 0 && r.y + r.h > 0 && r.x < UnityEngine.Screen.width && r.y < UnityEngine.Screen.height;

            public IReadOnlyList<UiElement> Buttons(bool visibleOnly = true)
            {
                var res = new List<UiElement>();
                try
                {
                    Crash.Enter("screen", "поиск кнопок");
                    foreach (var o in UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<Button>()))
                    {
                        var b = o.TryCast<Button>();
                        if (b == null || !b.gameObject.activeInHierarchy) continue;
                        Crash.Enter("screen", "кнопка " + b.gameObject.name);
                        var rt = b.GetComponent<RectTransform>();
                        var r = rt == null ? null : Rect(rt);
                        if (r == null) continue;
                        bool vis = OnScreen(r.Value) && b.interactable;
                        if (visibleOnly && !vis) continue;
                        string text = "";
                        try { var t = b.GetComponentInChildren<Text>(); if (t != null) text = t.text; } catch { }
                        res.Add(new UiElement { Kind = "button", Name = b.gameObject.name, Path = PathOf(b.transform), Text = text, X = r.Value.x, Y = r.Value.y, W = r.Value.w, H = r.Value.h, Clickable = vis, Raw = b.gameObject });
                    }
                }
                catch (Exception e) { Log.Write("screen: Buttons: " + e.Message); }
                return res.OrderBy(e => e.Y).ThenBy(e => e.X).ToList();
            }

            public IReadOnlyList<UiElement> Texts()
            {
                var res = new List<UiElement>();
                try
                {
                    Crash.Enter("screen", "поиск надписей");
                    foreach (var o in UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<Text>()))
                    {
                        var t = o.TryCast<Text>();
                        if (t == null || !t.gameObject.activeInHierarchy || string.IsNullOrWhiteSpace(t.text)) continue;
                        Crash.Enter("screen", "надпись " + t.gameObject.name);
                        var r = Rect(t.rectTransform);
                        if (r == null || !OnScreen(r.Value)) continue;
                        res.Add(new UiElement { Kind = "text", Name = t.gameObject.name, Path = PathOf(t.transform), Text = t.text, X = r.Value.x, Y = r.Value.y, W = r.Value.w, H = r.Value.h, Raw = t.gameObject });
                    }
                }
                catch (Exception e) { Log.Write("screen: Texts: " + e.Message); }
                return res.OrderBy(e => e.Y).ThenBy(e => e.X).ToList();
            }

            public UiElement FindButton(string s)
                => Buttons().FirstOrDefault(b => (b.Text ?? "").IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0 || b.Name.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0);

            public bool Click(UiElement e)
            {
                try
                {
                    var btn = e?.Raw?.GetComponent<Button>() ?? e?.Raw?.GetComponentInChildren<Button>();
                    if (btn == null || !btn.gameObject.activeInHierarchy) return false;
                    var es = UnityEngine.EventSystems.EventSystem.current;
                    if (es != null) es.SetSelectedGameObject(btn.gameObject);      // игра читает currentSelectedGameObject
                    btn.onClick.Invoke();
                    return true;
                }
                catch (Exception ex) { Log.Write("screen: Click: " + ex.Message); return false; }
            }

            public bool Click(string s) { var b = FindButton(s); return b != null && Click(b); }
        }

        internal static IScreen Instance() { EnsureHooks(); return new ScreenImpl(); }
    }
}
