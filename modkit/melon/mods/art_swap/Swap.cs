using ModKit;

namespace ArtSwap
{
    /// <summary>Подменяет иконки артефактов файлами icons/&lt;ID&gt;.png из zip мода (ID 41..222). Чтобы сменить арт, достаточно подложить PNG.</summary>
    public class SwapMod : IKitMod
    {
        public ModDef Define() => new ModDef { Id = "art_swap", Name = "Свои иконки артефактов", Version = "1.0", OnEnable = Enable };

        private static void Enable(ModContext ctx)
        {
            int n = 0;
            for (int id = 41; id <= 222; id++)
            {
                var png = ctx.ReadAsset($"icons/{id}.png");
                if (png == null) continue;
                if (ctx.Use<IArt>("modkit_art").SetIcon(id, png)) n++;
            }
            ctx.Log($"иконок артефактов подменено: {n}");
        }
    }
}
