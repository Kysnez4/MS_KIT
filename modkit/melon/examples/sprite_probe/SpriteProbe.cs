using System;
using Il2CppInterop.Runtime;
using ModKit;

namespace SpriteProbe
{
    /// <summary>Проверка спрайтов из бандла: грузит sprites.bundle (собран pack_mod.py из папки sprites) и читает размеры спрайтов через icall игры.</summary>
    public class SpriteProbeMod : IKitMod
    {
        public ModDef Define() => new ModDef
        {
            Id = "sprite_probe",
            Name = "Проверка спрайтов",
            Description = "Грузит sprites.bundle и пишет в журнал размеры спрайтов.",
            Version = "1.1",
            OnEnable = ctx => { bool done = false; ctx.Every(3000, () => { if (done) return; done = true; Run(ctx); }); },
        };

        private static unsafe void Run(ModContext ctx)
        {
            var getTex = (delegate* unmanaged<IntPtr, IntPtr>)IL2CPP.il2cpp_resolve_icall("UnityEngine.Sprite::get_texture");
            var getRect = (delegate* unmanaged<IntPtr, float*, void>)IL2CPP.il2cpp_resolve_icall("UnityEngine.Sprite::get_rect_Injected");
            UiKit.LoadSpriteBundle("sprite_probe", ctx.ReadAsset("sprites.bundle"), sprites =>
            {
                ctx.Log($"спрайтов из бандла: {sprites.Count}");
                foreach (var kv in sprites)
                {
                    float* r = stackalloc float[4];
                    getRect(kv.Value, r);
                    ctx.Log($"  {kv.Key}: 0x{kv.Value.ToInt64():x}, rect {r[2]}x{r[3]}, текстура 0x{getTex(kv.Value).ToInt64():x}");
                }
            });
        }
    }
}
