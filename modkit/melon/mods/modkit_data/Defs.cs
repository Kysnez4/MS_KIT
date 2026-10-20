using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Il2Cpp;
using ModKit;

namespace ModKitData
{
    /// <summary>Эффект: ID эффекта игры (docs/EFFECTS.md) и значение.</summary>
    public struct Effect
    {
        public int Id; public double Value;
        public Effect(int id, double value) { Id = id; Value = value; }
        public static implicit operator Effect((int id, double value) t) => new Effect(t.id, t.value);
    }
}
