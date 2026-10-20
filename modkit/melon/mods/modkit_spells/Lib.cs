using System;
using System.Collections.Generic;


namespace ModKit
{

    /// <summary>Точка входа библиотеки modkit_spells: публикует себя для ctx.Use&lt;T&gt;("modkit_spells").</summary>
    public class SpellsLib : IKitMod, ILibrary<ISpells>
    {
        public ModDef Define() => new ModDef
        {
            Id = "modkit_spells", Name = "ModKit: заклинания", Version = "1.0", Library = true,
            OnEnable = ctx => ctx.Expose(this),
        };
        ISpells ILibrary<ISpells>.For(ModContext ctx) => new SpellApi(ctx.Id, ctx.CleanupList(), ctx.Every);
    }
}
