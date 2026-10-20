using System;
using System.Collections.Generic;


namespace ModKit
{

    /// <summary>Точка входа библиотеки modkit_companions: публикует себя для ctx.Use&lt;T&gt;("modkit_companions").</summary>
    public class CompanionsLib : IKitMod, ILibrary<ICompanions>
    {
        public ModDef Define() => new ModDef
        {
            Id = "modkit_companions", Name = "ModKit: союзники", Version = "1.0", Library = true,
            OnEnable = ctx => ctx.Expose(this),
        };
        ICompanions ILibrary<ICompanions>.For(ModContext ctx) => new CompanionsImpl(ctx.Every, ctx.Guard, ctx.Timer, ctx.Use<IEnemies>("modkit_combat"), CooldownOf(ctx), ctx.CleanupList());

        private static Func<int, double> CooldownOf(ModContext ctx)
        {
            var spells = ctx.Use<ISpells>("modkit_spells");
            return id => spells.Cooldown(id);
        }
    }
}
