using System;
using Il2Cpp;
using ModKit;

namespace AreaMechanics
{
    /// <summary>
    /// Механики по зонам: в каждой зоне (1..14) герой получает свой модификатор скорости передвижения. В зонах 12–14 подзоны меняются прямо в бою
    /// (C_Battle_Manager.AreaChangeID), поэтому ключ — AreaChangeID, он опрашивается: событие areaChange подзоны не ловит. Модификатор задаётся
    /// своего эффекта 10101 (MulPercent по UnitApply_MoveSpeed), ctx.SetGlobalEffects; после смены мод просит пересчёт.
    /// Добавить свою механику — расширить Apply(): значение по зоне читается через Setting("ключ" + зона).
    /// Собрано по шаблону kill_streak, на устройстве не проверено.
    /// </summary>
    public class AreaMod : IKitMod
    {
        private static int _zone = -1, _applied = int.MinValue;
        private static V_Ability_Manager _mgr;

        public ModDef Define() => new ModDef
        {
            Id = "area_mechanics", Name = "Механики зон", Version = "1.0",
            Description = "Своя скорость героя в каждой зоне.",
            OnEnable = Enable,
            OnDisable = ctx => Cleanup(ctx),
        };

        private static void Enable(ModContext ctx)
        {
            var spells = ctx.Use<ISpells>("modkit_spells");
            _zone = -1; _applied = int.MinValue;
            // свой эффект: скорость героя ±% (отрицательное — замедление); движок повторяет Apply после каждого пересчёта и раз в ~0,7 с,
            // поэтому сброс статов при Play_AreaChange не снимает эффект
            ctx.Use<IEffects>("modkit_effects").Define(new EffectDef
            {
                Id = 10101, Name = "Скорость героя ±%",
                Apply = (h, v, c) => h.MulPercent("UnitApply_MoveSpeed", v),
            });
            ctx.Events.On("battleEnd", e => { _zone = -1; Apply(ctx, 0); });
            ctx.Every(500, () =>
            {
                if (!spells.InBattle) return;
                int z = CurrentZone();
                if (z < 1 || z > 14 || z == _zone) return;
                _zone = z;
                int pct = (int)ctx.Setting("speed" + z);
                ctx.Log($"зона {z}: скорость героя {pct:+0;-0;0}%");
                Apply(ctx, pct);
            });
            ctx.Log("механики зон включены");
        }

        private static int CurrentZone()
        {
            try { return C_Battle_Manager.AreaChangeID; } catch { return -1; }
        }

        private static void Apply(ModContext ctx, int pct)
        {
            if (pct == _applied) return;
            _applied = pct;
            ctx.Use<IEffects>("modkit_effects").SetGlobal(pct != 0 ? new[] { (10101, (double)pct) } : new (int, double)[0]);
            if (ctx.Setting("recalc") >= 1) Recalc(ctx);
        }

        private static void Recalc(ModContext ctx)
        {
            try
            {
                if (_mgr == null || _mgr.gameObject == null) _mgr = FindMgr();
                if (_mgr == null) { ctx.Log("пересчёт: V_Ability_Manager не найден"); return; }
                ctx.Mark("area_mechanics: AbilityStatApply(2)");
                _mgr.AbilityStatApply(2);
            }
            catch (Exception e) { _mgr = null; ctx.Log("пересчёт не удался: " + e.Message); }
        }

        private static V_Ability_Manager FindMgr()
        {
            foreach (var o in UnityEngine.Object.FindObjectsOfType(Il2CppInterop.Runtime.Il2CppType.Of<V_Ability_Manager>()))
            {
                var m = o.TryCast<V_Ability_Manager>();
                if (m != null) return m;
            }
            return null;
        }

        private static void Cleanup(ModContext ctx)
        {
            try { ctx.Use<IEffects>("modkit_effects").SetGlobal(new (int, double)[0]); if (_applied != 0 && ctx.Setting("recalc") >= 1) Recalc(ctx); }
            catch (Exception e) { ctx.Log("снятие модификатора: " + e.Message); }
            _applied = 0; _zone = -1;
        }
    }
}
