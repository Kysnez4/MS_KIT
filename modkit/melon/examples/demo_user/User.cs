using System.Linq;
using DemoLib;
using ModKit;

namespace DemoUser
{
    public class UserMod : IKitMod
    {
        public ModDef Define() => new ModDef
        {
            Id = "demo_user", Name = "Демо: мод с библиотекой", Version = "1.0",
            Description = "Берёт общий тип и реестр из библиотеки demo_lib.",
            Depends = { ["demo_lib"] = ">=1.0" },
            OnEnable = ctx =>
            {
                var reg = ctx.Registry<Greeting>("greetings");
                reg.Register("user", new Greeting { Text = "привет от мода" });
                ctx.Log("реестр greetings: " + string.Join(" | ", reg.All.Values.Select(g => g.Text)));
                ctx.Events.On<RegistryChanged>(e => ctx.Log($"реестр {e.Registry}: {(e.Added ? "+" : "-")}{e.Id}"));
                ctx.Events.Emit(new Ping { From = "demo_user", N = 1 });
            },
        };
    }
}
