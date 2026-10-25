using System.Linq;
using Il2Cpp;
using ModKit;

namespace MagicBalance
{
    /// <summary>
    /// Механики артефактов, которые не задаются таблицами (числа и описания — в balance.json, разделы artifacts/synergies):
    ///   ДНК (118) — эффект 52 убран в balance.json; +8% к атаке и макс. HP за каждый обелиск боя, обычный или сломанный
    ///     (оба вызывают C_Battle_Manager.Start_AreaChange — событие areaChange);
    ///   Железная дева (101) / Графиня (211) — боссы (особенность 11) взрываются при смерти: игра их пропускает, взрыв вызываем сами;
    ///   Экскалибур (197) — пока не собран Авалон (197, 109, 73, 181), атака героя −50% (проклятие; аура 300% — в balance.json);
    ///   Гильгамеш (синергия 33) — руна по убийствам: неуязвимость вместо жадности.
    /// </summary>
    internal static class Mechanics
    {
        private const int Dna = 118, Excalibur = 197;
        private static readonly int[] Avalon = { 197, 109, 73, 181 };
        private const double DnaPct = 8, CursePct = -50;
        private const int BossFlag = 11;      // особенность врага: «не взрывается при смерти» (у всех боссов)
        private const int NoRune = 9999999;   // так игра пишет «условия нет»

        private const int GreedKills = 2500;  // порог синергии 33 (эффект 733) — запасной, если игра не дала нормального значения
        private static int _obelisks, _greedCond, _greedBase;
        private static C_Battle_AllyUnit _hero;

        public static void Enable(ModContext ctx)
        {
            var fx = ctx.Use<IEffects>("modkit_effects");
            _obelisks = 0; _greedCond = 0; _greedBase = 0; _hero = null;

            ctx.Events.On("battleStart", e => { _obelisks = 0; _greedCond = 0; _greedBase = 0; _hero = null; });
            ctx.Events.On("areaChange", e =>
            {
                _obelisks++;
                ctx.Log($"обелиск №{_obelisks} (тип {Z_Database.ArtifactType})");
                if (Has(Dna)) fx.Recalc();
            });
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
                if (Has(Dna) && _obelisks > 0)
                {
                    v.MulPercent("UnitApply_Attack", DnaPct * _obelisks);
                    v.MulPercent("UnitApply_HpMax", DnaPct * _obelisks);
                }
                if (Has(Excalibur) && !Avalon.All(Has)) v.MulPercent("UnitApply_Attack", CursePct);
            });
        }

        /// <summary>Синергия 33 включена (List_ApplySynergyON[ID] = 1, как в журнале auto_artifact «ON[33]=1»).</summary>
        private static bool Gilgamesh()
        {
            try { var on = Z_Database.List_ApplySynergyON; return on != null && on.Count > 33 && on[33] > 0; } catch { return false; }
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
