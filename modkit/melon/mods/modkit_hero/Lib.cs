using System;
using System.Collections.Generic;


namespace ModKit
{

    /// <summary>Точка входа библиотеки modkit_hero: публикует себя для ctx.Use&lt;T&gt;("modkit_hero").</summary>
    public class HeroLib : IKitMod, ILibrary<IHero>
    {
        public ModDef Define() => new ModDef
        {
            Id = "modkit_hero", Name = "ModKit: герой", Version = "1.0", Library = true,
            OnEnable = ctx => ctx.Expose(this),
        };
        IHero ILibrary<IHero>.For(ModContext ctx) => HeroApi.Instance();
    }
}
