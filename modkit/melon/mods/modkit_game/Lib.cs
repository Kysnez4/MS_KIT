using System;
using System.Collections.Generic;


namespace ModKit
{

    /// <summary>Точка входа библиотеки modkit_game: публикует себя для ctx.Use&lt;T&gt;("modkit_game").</summary>
    public class GameLib : IKitMod, ILibrary<IGame>
    {
        public ModDef Define() => new ModDef
        {
            Id = "modkit_game", Name = "ModKit: состояние игры", Version = "1.0", Library = true,
            OnEnable = ctx => ctx.Expose(this),
        };
        IGame ILibrary<IGame>.For(ModContext ctx) => GameApi.Instance();
    }
}
