using System;
using System.Collections.Generic;


namespace ModKit
{

    /// <summary>Точка входа библиотеки modkit_zones: публикует себя для ctx.Use&lt;T&gt;("modkit_zones").</summary>
    public class ZonesLib : IKitMod, ILibrary<IZones>
    {
        public ModDef Define() => new ModDef
        {
            Id = "modkit_zones", Name = "ModKit: зоны", Version = "1.0", Library = true,
            OnEnable = ctx => ctx.Expose(this),
        };
        IZones ILibrary<IZones>.For(ModContext ctx) => new ZonesImpl(ctx.Timer, ctx.Use<IEnemies>("modkit_combat"), ctx.Guard, ctx.CleanupList());
    }
}
