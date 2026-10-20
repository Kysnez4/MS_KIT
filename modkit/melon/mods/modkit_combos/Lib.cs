using System;
using System.Collections.Generic;
using System.Linq;
using ModKit;

namespace ModKitData
{

    public interface ICombos
    {
        int Add(ComboDef def);
        List<int> AddMany(List<ComboDef> defs);
        void Edit(int id, ComboChanges changes);
    }

    internal class CombosFor : ICombos
    {
        private readonly ModContext _c;
        public CombosFor(ModContext c) { _c = c; }
        private void Undo(Action a) => _c.OnCleanup(a);
        public int Add(ComboDef def) => ComboCommands.AddCombo(def, Undo, _c.Id);
        public List<int> AddMany(List<ComboDef> defs) => defs.Select(Add).ToList();
        public void Edit(int id, ComboChanges c) => ComboCommands.EditCombo(id, c, Undo, _c.Id);
    }

    public class CombosLib : IKitMod, ILibrary<ICombos>
    {
        public ModDef Define() => new ModDef
        {
            Id = "modkit_combos", Name = "ModKit: комбинации", Version = "1.0", Library = true,
            OnEnable = ctx =>
            {
                ctx.Expose(this);
            },
        };

        ICombos ILibrary<ICombos>.For(ModContext ctx) => new CombosFor(ctx);
    }
}
