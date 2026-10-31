using System.Linq;
using Il2Cpp;
using ModKit;

namespace MagicBalance
{
    /// <summary>
    /// Механики артефактов, которые не задаются таблицами (числа и описания — в balance.json, разделы artifacts/synergies):
    ///   ДНК (118) — работает механика игры (эффект 52), мод только убирает снижение эффективности: игра считает
    ///     бонус как сумму 8 × 0.85^i по обелискам (C_Battle_AllyUnit.StartInformation_B, в версии 612 — RVA 0x11c15c8),
    ///     инструкцию загрузки 0.85 меняем на fmov #1.0 (см. <see cref="DnaPatch"/>). Своя копия ДНК (через modkit_effects)
    ///     не учитывала потолок лечения ApplyPHpMaxNum и счётчик обелисков игры — отсюда «урон от лечения»;
    ///   Железная дева (101) / Графиня (211) — боссы (особенность 11) взрываются при смерти: игра их пропускает, взрыв вызываем сами;
    ///   Экскалибур (197) — пока не активна синергия Авалон (5; собрана 197, 109, 73, 181 или включена Магнум Опусом), атака героя −50% (проклятие; аура 300% — в balance.json);
    ///   Гильгамеш (синергия 33) — руна по убийствам: неуязвимость вместо жадности.
    /// </summary>
    internal static class Mechanics
    {
        private const int Excalibur = 197, AvalonSyn = 5;
        private static readonly int[] Avalon = { 197, 109, 73, 181 };
        private const double CursePct = -50;
        private const int BossFlag = 11;      // особенность врага: «не взрывается при смерти» (у всех боссов)
        private const int NoRune = 9999999;   // так игра пишет «условия нет»

        private const int GreedKills = 2500;  // порог синергии 33 (эффект 733) — запасной, если игра не дала нормального значения
        private static int _greedCond, _greedBase, _curse;
        private static C_Battle_AllyUnit _hero;

        public static void Enable(ModContext ctx)
        {
            var fx = ctx.Use<IEffects>("modkit_effects");
            _greedCond = 0; _greedBase = 0; _hero = null;
            DnaPatch.Enable(ctx);

            ctx.Events.On("battleStart", e => { _greedCond = 0; _greedBase = 0; _curse = 0; _hero = null; });
            ctx.Events.On("areaChange", e => ctx.Log($"обелиск (тип {Z_Database.ArtifactType})"));
            ctx.Events.On("enemyDeath", e => ctx.Guard("взрыв босса", () => BossBlast((e as EventData)?.Get<C_Battle_EnemyUnit>("enemy"), ctx)));

            // Гильгамеш: порог игры (ApplyRuneObtainKillCondition) прячем, счётчик убийств игра ведёт сама (Play_Unit_Death),
            // при достижении порога вызываем Unit_Rune_DefenceDivine
            ctx.Every(250, () => ctx.Guard("руна Гильгамеша", () =>
            {
                var hero = _hero;
                if (_greedCond <= 0 || hero == null || C_Battle_AllyUnit.ApplyRuneObtainKillNum <= _greedCond) return;
                C_Battle_AllyUnit.ApplyRuneObtainKillNum = 0;
                ctx.Log($"Гильгамеш: {_greedCond} убийств → руна неуязвимости");
                hero.Unit_Rune_DefenceDivine();
            }));

            fx.OnHeroPass(v =>
            {
                _hero = v.Raw;
                var cond = v.Field("ApplyRuneObtainKillCondition");
                // только при собранной синергии: у порога есть и другие источники (в начале боя был 10000).
                // Игра иногда пересчитывает порог от уже подменённого значения (5000 − 9999999 = −9994999): такие значения
                // не берём, порог помним с последнего нормального пересчёта, иначе срабатывала бы обычная руна жадности.
                if (cond.Value > 0 && cond.Value < NoRune) _greedBase = (int)cond.Value;
                _greedCond = Gilgamesh() ? (_greedBase > 0 ? _greedBase : GreedKills) : 0;
                if (_greedCond > 0) cond.Value = NoRune;
                // как дебафф Плазмы у Андроида: снимается, когда синергия Авалон активна (в т.ч. через Магнум Опус), а не по 4 артефактам
                int curse = !Has(Excalibur) ? 0 : SynergyOn(AvalonSyn) || Avalon.All(Has) ? 2 : 1;
                if (curse == 1) v.MulPercent("UnitApply_Attack", CursePct);
                if (curse != _curse) { _curse = curse; if (curse > 0) ctx.Log(curse == 1 ? "Экскалибур: проклятие (атака −50%)" : $"Экскалибур: проклятие снято (синергия Авалон {(SynergyOn(AvalonSyn) ? "активна" : "не активна, собраны 4 артефакта")})"); }
            });
        }

        /// <summary>Синергия 33 включена (List_ApplySynergyON[ID] = 1, как в журнале auto_artifact «ON[33]=1»).</summary>
        private static bool Gilgamesh() => SynergyOn(33);

        /// <summary>Синергия включена игрой (List_ApplySynergyON[ID] = 1) — ставится и Магнум Опусом.</summary>
        private static bool SynergyOn(int id)
        {
            try { var on = Z_Database.List_ApplySynergyON; return on != null && on.Count > id && on[id] > 0; } catch { return false; }
        }

        private static bool Has(int id)
        {
            try { return Z_Database.List_ArtifactObtain.Contains(id); } catch { return false; }
        }

        /// <summary>Unit_Death вызывается и для живых врагов — берём только момент смерти босса.</summary>
        private static void BossBlast(C_Battle_EnemyUnit u, ModContext ctx)
        {
            if (u == null || !u.Unit_IsLive || u.Unit_Hp > 0 || u.List_UnitSpecialEffect == null || !u.List_UnitSpecialEffect.Contains(BossFlag)) return;
            if (Z_Database.AllyUnitVar_EliteMonsterExplosionON > 0)
            {
                // ветка Графини: внутри бросок Девы — 75% HP, иначе 30%
                u.Unit_DetonateDead_Elite();
            }
            else if (C_Battle_AllyUnit.UnitApply_DetonateDeadRan > 0 && UnityEngine.Random.Range(1, 101) <= C_Battle_AllyUnit.UnitApply_DetonateDeadRan)
            {
                u.DetonateDead_Damage = u.UnitApply_HpMax * C_Battle_AllyUnit.UnitApply_DetonateDeadDamageRatioNum;
                u.Unit_DetonateDead();
            }
        }
    }
}
