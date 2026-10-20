using System;
using System.Collections.Generic;
using System.Linq;
using ModKit;

namespace ModKitData
{

    public interface IArtifacts
    {
        /// <summary>Добавляет новый артефакт целиком (строка, иконка, пулы выпадения, окно описания, плитка в энциклопедии). Возвращает ID.</summary>
        int Add(ArtifactDef def);
        /// <summary>Несколько артефактов за один вызов (быстрее). Возвращает ID по порядку.</summary>
        List<int> AddMany(List<ArtifactDef> defs);
        /// <summary>Изменяет артефакт: имя, эффекты, описание, редкость.</summary>
        void Edit(int id, ArtifactChanges changes);
        /// <summary>Убирает артефакт из выпадения (сундуки, торговец).</summary>
        void Remove(int id);
        /// <summary>Возвращает в выпадение артефакт, убранный Remove.</summary>
        void Restore(int id);
    }

    internal class ArtifactsFor : IArtifacts
    {
        private readonly ModContext _c;
        public ArtifactsFor(ModContext c) { _c = c; }
        private void Undo(Action a) => _c.OnCleanup(a);
        public int Add(ArtifactDef def) => Artifacts.Add(def, Undo, _c.Id);
        public List<int> AddMany(List<ArtifactDef> defs) => Artifacts.AddMany(defs, Undo, _c.Id);
        public void Edit(int id, ArtifactChanges c) => ArtifactCommands.EditArtifact(id, c, Undo, _c.Id);
        public void Remove(int id) => ArtifactCommands.RemoveArtifact(id, Undo, _c.Id);
        public void Restore(int id) => ArtifactCommands.RestoreArtifact(id, _c.Id);
    }

    public class ArtifactsLib : IKitMod, ILibrary<IArtifacts>
    {
        public ModDef Define() => new ModDef
        {
            Id = "modkit_artifacts", Name = "ModKit: артефакты", Version = "1.0", Library = true,
            OnEnable = ctx =>
            {
                Artifacts.Install(ctx);
                ctx.Expose(this);
            },
        };

        IArtifacts ILibrary<IArtifacts>.For(ModContext ctx) => new ArtifactsFor(ctx);
    }
}
