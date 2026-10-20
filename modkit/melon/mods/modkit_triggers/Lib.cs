using System;
using System.Collections.Generic;


namespace ModKit
{

    /// <summary>Точка входа библиотеки modkit_triggers: публикует себя для ctx.Use&lt;T&gt;("modkit_triggers").</summary>
    public class TriggersLib : IKitMod, ILibrary<ITriggers>
    {
        public ModDef Define() => new ModDef
        {
            Id = "modkit_triggers", Name = "ModKit: триггеры", Version = "1.0", Library = true,
            OnEnable = ctx => ctx.Expose(this),
        };
        ITriggers ILibrary<ITriggers>.For(ModContext ctx) => new TriggersImpl(ctx);
    }
}
