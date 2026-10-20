using System.Collections.Generic;
using ModKit;

namespace PanDemo
{
    /// <summary>Демо: «Настройки» мода открывают карту из 8 узлов (тап = открыть узел, следующие становятся доступны) и листатель страниц.</summary>
    public class PanDemoMod : IKitMod
    {
        static UiKit.PanView _map;
        static UiKit.Pager _pager;

        public ModDef Define() => new ModDef
        {
            Id = "pan_demo",
            Name = "Карта (демо)",
            Description = "Демо виджетов PanView и Pager: карта из 8 узлов и листатель страниц (кнопка настроек).",
            Version = "1.1",
            OpenSettings = ctx => OpenMap(ctx),
            OnEnable = ctx => ctx.Log("pan_demo включён: откройте настройки мода"),
        };

        static void OpenMap(ModContext ctx)
        {
            var o = new UiKit.PanOptions { ContentW = 1300, ContentH = 1100 };
            var L = UiKit.PanState.Locked;
            o.Nodes.Add(new UiKit.PanNode("root", 150, 550, null, "Начало", UiKit.PanState.Done));
            o.Nodes.Add(new UiKit.PanNode("a", 400, 300, null, "Огонь", UiKit.PanState.Available));
            o.Nodes.Add(new UiKit.PanNode("b", 400, 800, null, "Лёд", UiKit.PanState.Available));
            o.Nodes.Add(new UiKit.PanNode("c", 700, 200, null, "Взрыв", L));
            o.Nodes.Add(new UiKit.PanNode("d", 700, 520, null, "Пар", L));
            o.Nodes.Add(new UiKit.PanNode("e", 700, 900, null, "Мороз", L));
            o.Nodes.Add(new UiKit.PanNode("f", 1000, 400, null, "Феникс", L));
            o.Nodes.Add(new UiKit.PanNode("g", 1100, 800, null, "Зима", L));
            foreach (var (f, t) in new[] { ("root", "a"), ("root", "b"), ("a", "c"), ("a", "d"), ("b", "d"), ("b", "e"), ("d", "f"), ("c", "f"), ("e", "g"), ("d", "g") })
                o.Edges.Add(new UiKit.PanEdge(f, t));
            o.OnTap = n =>
            {
                ctx.Log($"тап по узлу {n.Id} ({n.State})");
                if (n.State == UiKit.PanState.Available)
                {
                    _map.SetState(n.Id, UiKit.PanState.Done);
                    foreach (var e in o.Edges)
                        if (e.From == n.Id && _map.Find(e.To).State == UiKit.PanState.Locked) _map.SetState(e.To, UiKit.PanState.Available);
                }
            };
            _map = UiKit.OpenPanWindow("pan_demo_map", "Карта", "Тяните пальцем; тап по синему узлу открывает его", o, 360);
            _map.CenterOn("root");

            // листатель страниц под картой (карта закончена на 360 пикселей выше низа)
            var names = new List<string> { "Огонь", "Лёд", "Молния" };
            _pager = UiKit.AddPager(_map.View.parent, 30, UiKit.ScreenH - 340, UiKit.LogicalW - 60, 310, new UiKit.PagerOptions
            {
                Pages = names,
                Loop = true,
                OnPage = (i, title, page) =>
                {
                    UiKit.Label(page, "Страница " + (i + 1) + ": " + title, 20, 0, UiKit.LogicalW - 100, 200, 28, 4);
                },
            });
        }
    }
}
