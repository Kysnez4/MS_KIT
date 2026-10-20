using System;
using System.Collections.Generic;


namespace ModKit
{

    /// <summary>Точка входа библиотеки modkit_art: публикует себя для ctx.Use&lt;T&gt;("modkit_art").</summary>
    public class ArtLib : IKitMod, ILibrary<IArt>
    {
        public ModDef Define() => new ModDef
        {
            Id = "modkit_art", Name = "ModKit: иконки артефактов", Version = "1.0", Library = true,
            OnEnable = ctx => ctx.Expose(this),
        };
        IArt ILibrary<IArt>.For(ModContext ctx) => new ArtApi(ctx.Id, ctx.CleanupList());
    }
}
