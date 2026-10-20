using System;
using System.Collections.Generic;


namespace ModKit
{

    /// <summary>Точка входа библиотеки modkit_combat: публикует себя для ctx.Use&lt;T&gt;("modkit_combat").</summary>
    public class CombatLib : IKitMod, ILibrary<IEnemies>, ILibrary<ICombat>
    {
        public ModDef Define() => new ModDef
        {
            Id = "modkit_combat", Name = "ModKit: бой", Version = "1.0", Library = true,
            OnEnable = ctx => ctx.Expose(this),
        };
        IEnemies ILibrary<IEnemies>.For(ModContext ctx) => CombatApi.Enemy_();
        ICombat ILibrary<ICombat>.For(ModContext ctx) => CombatApi.Combat_();
    }
}
