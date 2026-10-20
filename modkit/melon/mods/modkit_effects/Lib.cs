using System;
using System.Collections.Generic;
using System.Linq;

namespace ModKit
{

    public interface IEffects
    {
        /// <summary>Регистрирует свой эффект артефакта/комбинации (ID >= 10000). Снимается при выключении мода.</summary>
        void Define(EffectDef def);
        /// <summary>Эффекты мода без привязки к артефакту: пары (ID эффекта игры, значение). Пустой массив снимает.</summary>
        void SetGlobal((int Id, double Value)[] pairs);
        /// <summary>Подключает встроенные эффекты 10001–10004 (урон пули %, перезарядка −%, снарядов +N, радиус сбора %).</summary>
        void UseBuiltin();
        /// <summary>
        /// Проход по герою: fn вызывается после каждого пересчёта характеристик и раз в ~0,7 с, пока идёт бой. Через view.Field(...).Value
        /// читается значение игры (база), запись задаёт результат; поля, которых проход перестал касаться, возвращаются к базе.
        /// </summary>
        void OnHeroPass(Action<IHeroView> fn);
        /// <summary>Пересчитывает характеристики героя сразу (после SetGlobal). false — менеджер способностей ещё не известен (не было пересчёта в этом бою).</summary>
        bool Recalc();
    }

    internal class EffectsFor : IEffects
    {
        private readonly ModContext _c; private readonly List<Action> _cl;
        public EffectsFor(ModContext c) { _c = c; _cl = c.CleanupList(); }
        public void Define(EffectDef def) => Effects.Define(def, _c, _cl);
        public void SetGlobal((int Id, double Value)[] pairs) => Effects.SetGlobal(_c.Id, pairs.Select(p => (p.Id, p.Value)).ToArray(), _cl);
        public void UseBuiltin() => Effects.UseBuiltin(_c, _cl);
        public void OnHeroPass(Action<IHeroView> fn) => Effects.AddPass(_c.Id, fn, _cl);
        public bool Recalc() => Effects.Recalc();
    }

    /// <summary>Точка входа библиотеки modkit_effects: публикует себя для ctx.Use&lt;T&gt;("modkit_effects").</summary>
    public class EffectsLib : IKitMod, ILibrary<IEffects>
    {
        public ModDef Define() => new ModDef
        {
            Id = "modkit_effects", Name = "ModKit: эффекты", Version = "1.0", Library = true,
            OnEnable = ctx => ctx.Expose(this),
        };
        IEffects ILibrary<IEffects>.For(ModContext ctx) => new EffectsFor(ctx);
    }
}
