using System;
using System.Collections.Generic;


namespace ModKit
{

    /// <summary>Точка входа библиотеки modkit_research: публикует себя для ctx.Use&lt;T&gt;("modkit_research").</summary>
    public class ResearchLib : IKitMod, ILibrary<IResearch>
    {
        public ModDef Define() => new ModDef
        {
            Id = "modkit_research", Name = "ModKit: исследования", Version = "1.0", Library = true,
            OnEnable = ctx => ctx.Expose(this),
        };
        IResearch ILibrary<IResearch>.For(ModContext ctx) => new ResearchImpl(ctx);
    }
}
