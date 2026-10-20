using ModKit;

namespace HelloAddon
{
    public class AddonMod : IKitMod
    {
        public ModDef Define() => new ModDef
        {
            Id = "hello_addon",
            Name = "Привет: дополнение",
            Description = "Зависит от мода «Привет» и берёт у него приветствие.",
            Version = "1.0",
            Depends = { ["hello"] = ">=1.0" },
            OnEnable = ctx =>
            {
                var api = ctx.Mod<System.Collections.Generic.Dictionary<string, object>>("hello");
                ctx.Log("API hello: " + (api == null ? "нет" : api["greeting"]));
            },
        };
    }
}
