using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Il2Cpp;
using ModKit;

namespace ModKitData
{
    public class ComboDef
    {
        public string Name;
        public List<Effect> Effects = new List<Effect>();
        public List<string> Lines;
        /// <summary>ID существующей комбинации — образец (иконка и остальные колонки). По умолчанию 1.</summary>
        public int CloneFrom = 1;
        /// <summary>ID навыков, требуемых для комбинации (до 3). Без этого берутся требования образца.</summary>
        public int[] Requires;
        public int? Icon;
    }

    public class ComboChanges
    {
        public string Name;
        public List<Effect> Effects;
        public List<string> Lines;
        public int[] Requires;
    }
}
