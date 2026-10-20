using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Il2Cpp;
using ModKit;

namespace ModKitData
{
    public class SynergyDef
    {
        public string Name;
        /// <summary>До 3 эффектов — столько пар в таблице синергий.</summary>
        public List<Effect> Effects = new List<Effect>();
        public List<string> Lines;
        /// <summary>ID артефактов в синергии (до 5).</summary>
        public int[] Members = new int[0];
        public int CloneFrom = 3;
        public int? Icon;
    }
}
