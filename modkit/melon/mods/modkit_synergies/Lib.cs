using System;
using System.Collections.Generic;
using System.Linq;
using ModKit;

namespace ModKitData
{

    public interface ISynergies
    {
        int Add(SynergyDef def);
        List<int> AddMany(List<SynergyDef> defs);
        /// <summary>Очищает синергию: артефактов в ней не остаётся, она не срабатывает.</summary>
        void Remove(int id);
        /// <summary>Добавляет артефакт в синергию (максимум 5).</summary>
        void AddArtifact(int synergyId, int artifactId);
        void RemoveArtifact(int synergyId, int artifactId);
    }

    internal class SynergiesFor : ISynergies
    {
        private readonly ModContext _c;
        public SynergiesFor(ModContext c) { _c = c; }
        private void Undo(Action a) => _c.OnCleanup(a);
        public int Add(SynergyDef def) => SynergyCommands.AddSynergy(def, Undo, _c.Id);
        public List<int> AddMany(List<SynergyDef> defs) => defs.Select(Add).ToList();
        public void Remove(int id) => SynergyCommands.RemoveSynergy(id, Undo, _c.Id);
        public void AddArtifact(int s, int a) => SynergyCommands.SynergyAddArtifact(s, a, Undo, _c.Id);
        public void RemoveArtifact(int s, int a) => SynergyCommands.SynergyRemoveArtifact(s, a, Undo, _c.Id);
    }

    public class SynergiesLib : IKitMod, ILibrary<ISynergies>
    {
        public ModDef Define() => new ModDef
        {
            Id = "modkit_synergies", Name = "ModKit: синергии", Version = "1.0", Library = true,
            OnEnable = ctx =>
            {
                ctx.Expose(this);
            },
        };

        ISynergies ILibrary<ISynergies>.For(ModContext ctx) => new SynergiesFor(ctx);
    }
}
