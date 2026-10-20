using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Il2Cpp;
using ModKit;

namespace ModKitData
{
    public class ArtifactDef
    {
        /// <summary>Название (показывается в игре).</summary>
        public string Name;
        /// <summary>Строки описания: каждая выводится зелёным в окне артефакта. Не больше 6. Если не заданы — «Эффект ID: значение» на каждый эффект.</summary>
        public List<string> Lines;
        /// <summary>Редкость: C обычное, B редкое, A героическое, P особое, S легендарное. По умолчанию — как у образца.</summary>
        public string Rarity;
        /// <summary>ID существующего артефакта, чью иконку взять (по умолчанию иконка образца CloneFrom).</summary>
        public int? Icon;
        /// <summary>Путь к PNG/JPG на телефоне со своей иконкой (рекомендуется 248x288, с прозрачностью). Показывается только в энциклопедии.</summary>
        public string IconFile;
        /// <summary>Эффекты, не больше 9.</summary>
        public List<Effect> Effects = new List<Effect>();
        /// <summary>ID существующего артефакта-образца: от него берутся иконка, редкость и прочие колонки. По умолчанию 42 («Алмаз», класс A).</summary>
        public int CloneFrom = 42;
        /// <summary>Вес выпадения: число копий в пуле своей редкости. По умолчанию 5.</summary>
        public int Weight = 5;
        /// <summary>Тест: положить во все пулы с очень большим весом, чтобы артефакт выпадал почти всегда.</summary>
        public bool Guaranteed;
    }

    public class ArtifactChanges
    {
        public string Name;
        /// <summary>Заменяют все эффекты записи.</summary>
        public List<Effect> Effects;
        public List<string> Lines;
        /// <summary>Перенос в пул другой редкости: C, B, A, P, S.</summary>
        public string Rarity;
    }
}
