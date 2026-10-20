using ModKit;

namespace DemoLib
{
    /// <summary>Общие типы библиотеки: события и записи реестра, которые видят все моды-пользователи.</summary>
    public class Ping { public string From; public int N; }
    public class Greeting { public string Text; }

    public class LibMod : IKitMod
    {
        public ModDef Define() => new ModDef
        {
            Id = "demo_lib", Name = "Демо: библиотека", Version = "1.0", Library = true,
            OnEnable = ctx =>
            {
                ctx.Log("библиотека включена");
                ctx.Registry<Greeting>("greetings").Register("lib", new Greeting { Text = "привет от библиотеки" });
                ctx.Events.On<Ping>(p => ctx.Log($"библиотека получила Ping от {p.From} №{p.N}"));
            },
        };
    }
}
