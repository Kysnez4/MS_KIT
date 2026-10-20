using System;
using System.Collections.Generic;
using System.Linq;
using ModKit;

namespace ModKitData
{

    public interface ISpellsData
    {
        /// <summary>Читает заклинание (ID 1..21): имя, описание, уровни, слоты эффектов.</summary>
        SpellInfo Get(int id);
        /// <summary>Меняет заклинание: имя, описание, слоты эффектов по уровням, масштаб и сдвиг значений по ID эффекта. Числа в тексте уровней («효과 설명줄05/06») обновляются вслед за значениями слотов. Откатывается при выключении мода.</summary>
        void Edit(int id, SpellChanges changes);
        /// <summary>Коротко: умножить значения эффекта effectId у заклинания (например x2 к урону пули: Scale(1, 151, 2)).</summary>
        void Scale(int id, int effectId, double factor);
    }

    internal class SpellsDataFor : ISpellsData
    {
        private readonly ModContext _c;
        public SpellsDataFor(ModContext c) { _c = c; }
        private void Undo(Action a) => _c.OnCleanup(a);
        public SpellInfo Get(int id) => Spells.Get(id);
        public void Edit(int id, SpellChanges c) => Spells.Edit(id, c, Undo, _c.Id);
        public void Scale(int id, int effectId, double factor) => Spells.Edit(id, new SpellChanges { Scale = { [effectId] = factor } }, Undo, _c.Id);
    }

    public class SpellsDataLib : IKitMod, ILibrary<ISpellsData>
    {
        public ModDef Define() => new ModDef
        {
            Id = "modkit_spells_data", Name = "ModKit: данные заклинаний", Version = "1.0", Library = true,
            OnEnable = ctx =>
            {
                ctx.Expose(this);
            },
        };

        ISpellsData ILibrary<ISpellsData>.For(ModContext ctx) => new SpellsDataFor(ctx);
    }
}
