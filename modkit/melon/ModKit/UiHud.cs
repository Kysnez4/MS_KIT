using System;
using UnityEngine;
using UnityEngine.UI;

namespace ModKit
{
    public static partial class UiKit
    {
        /// <summary>Кнопка поверх экрана (в том числе в бою): живёт на игровом холсте, пока её не уберут.</summary>
        public class HudButton
        {
            internal GameObject Root;
            internal Text Label;
            public bool Alive => Root != null;
            public void SetText(string text) { if (Label != null && Label.text != text) Label.text = text; }
            public void SetVisible(bool on) { if (Root != null && Root.activeSelf != on) Root.SetActive(on); }
            public void Destroy() { SafeDestroy(Root); Root = null; Label = null; }
        }

        /// <summary>
        /// Кнопка на экране в логических пикселях (экран LogicalW × ScreenH, от левого верхнего угла). Видна и в бою, если игровой холст активен.
        /// Мод создаёт и убирает её сам (при выключении мода — через ctx.OnCleanup(btn.Destroy)).
        /// </summary>
        public static HudButton AddHudButton(string name, string text, double x, double y, double w, double h, double size, double[] bg, Action onClick)
        {
            if (CanvasTf == null || !CanvasTf.gameObject.activeInHierarchy)
            {
                var c = MenuUi.FindGameCanvas();
                float scale = c.scaleFactor;
                K = Screen.width / scale / LogicalW;
                ScreenH = Screen.height / scale / K;
                CanvasTf = c.transform;
            }
            var root = Node("ModKitHud_" + name, CanvasTf);
            Stretch(root.rt);
            var btn = AddButton(root.rt, text, x, y, w, h, size, bg, onClick);
            return new HudButton { Root = root.go, Label = btn.GetComponentInChildren<Text>() };
        }
    }
}
