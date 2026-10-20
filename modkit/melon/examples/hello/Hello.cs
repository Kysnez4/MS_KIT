using ModKit;

namespace Hello
{
    /// <summary>Пример внешнего мода: настройка, событие боя, периодический вызов, API для других модов (словарь: типы между модами не делятся).</summary>
    public class HelloMod : IKitMod
    {
        public ModDef Define() => new ModDef
        {
            Id = "hello",
            Name = "Привет",
            Description = "Пример C#-мода: пишет в журнал начало боя и раз в N секунд.",
            Version = "1.0",
            Options = { new ModOption { Key = "every", Label = "Раз в, с", Default = 10, Min = 1, Max = 60, Step = 1 } },
            OnEnable = ctx =>
            {
                ctx.Expose(new System.Collections.Generic.Dictionary<string, object> { ["greeting"] = "привет из hello" });
                ctx.Events.On("battleStart", e => ctx.Log("бой начался"));
                ctx.Every(ctx.Setting("every") * 1000, () => ctx.Log("тик, every=" + ctx.Setting("every")));
                ctx.After("C_Battle_Manager", "Start", 0, (self, args) => ctx.Log("After C_Battle_Manager.Start"));
            },
        };
    }
}
