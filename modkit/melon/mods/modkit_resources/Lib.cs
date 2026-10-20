using System;
using System.Collections.Generic;


namespace ModKit
{

    public interface IResources
    {
        /// <summary>Свой ресурс мода: восстановление, трата, полоса на экране. Заполняется до максимума на каждый battleStart.</summary>
        IResource Create(string name, double max, double regenPerSec);
    }

    internal class ResourcesFor : IResources
    {
        private readonly ModContext _c; private readonly List<Action> _cl;
        public ResourcesFor(ModContext c) { _c = c; _cl = c.CleanupList(); }
        public IResource Create(string name, double max, double regenPerSec) => new ResourceImpl(_c.Id + "_" + name, max, regenPerSec, _c.Every, _c.Timer, _c.Events, _cl);
    }

    /// <summary>Точка входа библиотеки modkit_resources: публикует себя для ctx.Use&lt;T&gt;("modkit_resources").</summary>
    public class ResourcesLib : IKitMod, ILibrary<IResources>
    {
        public ModDef Define() => new ModDef
        {
            Id = "modkit_resources", Name = "ModKit: ресурсы", Version = "1.0", Library = true,
            OnEnable = ctx => ctx.Expose(this),
        };
        IResources ILibrary<IResources>.For(ModContext ctx) => new ResourcesFor(ctx);
    }
}
