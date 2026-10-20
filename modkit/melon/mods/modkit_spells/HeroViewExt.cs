using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Il2Cpp;
using UnityEngine;

namespace ModKit
{
    /// <summary>Поле заклинания героя по смыслу: view.Spell(2, SpellStat.Damage).Value *= 1.2.</summary>
    public enum SpellStat { Damage, Num, Cooldown, Area, Size }

    public static class HeroViewExt
    {
        /// <summary>Поле UnitApply_{Имя}{Damage|Num|PSpeed|Area|Size} заклинания id (1..21). Бросает исключение, если у заклинания такого поля нет.</summary>
        public static FieldRef Spell(this IHeroView v, int id, SpellStat stat)
        {
            if (!SpellTable.Names.TryGetValue(id, out var n)) throw new ArgumentException($"заклинание {id}: нет в таблице");
            string suffix = stat == SpellStat.Cooldown ? "PSpeed" : stat.ToString();
            Exception last = null;
            foreach (var name in new[] { $"UnitApply_{n}{suffix}", $"UnitApply_{n.TrimEnd('A')}{suffix}" })
            {
                try { return v.Field(name); } catch (Exception e) { last = e; }
            }
            throw new Exception($"заклинание {id} ({n}): нет поля {suffix}", last);
        }
    }
}
