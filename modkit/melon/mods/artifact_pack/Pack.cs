using System;
using System.Collections.Generic;
using System.Linq;
using ModKit;
using ModKitData;

namespace ArtifactPack
{
    /// <summary>
    /// 12 новых артефактов (огонь, лёд, сбор, крит, танк) и 2 синергии. Всё через modkit_data: AddArtifacts (строки Ability, иконка образца,
    /// пулы выпадения, запись Dictionary_ApplySynergy, плитки в энциклопедии) и AddSynergy. Эффекты — только настоящие ID игры из docs/EFFECTS.md.
    /// </summary>
    public class PackMod : IKitMod
    {
        private class Item
        {
            public string Key, Name, Rarity; public int Clone; public Effect[] Fx; public string[] Lines;
        }

        private static Item A(string key, string name, string rarity, int clone, Effect[] fx, params string[] lines) =>
            new Item { Key = key, Name = name, Rarity = rarity, Clone = clone, Fx = fx, Lines = lines };

        // Образцы (CloneFrom) дают только иконку и редкость по умолчанию: 132 Факел, 124 Котел, 148 Сера, 134 Снеговик, 80 Лунный кристалл,
        // 61 Заговоренный рюкзак, 92 Фонарь, 64 Мишень, 111 Топор палача, 103 Чешуя дракона, 91 Второе сердце, 90 Крест.
        private static readonly Item[] Items =
        {
            // огонь
            A("coal", "Угольный амулет", "C", 132, new Effect[] { (181, 25), (461, 25) },
                "Урон огненных шаров +25%", "Урон испепеления +25%"),
            A("lava", "Лавовое сердце", "A", 148, new Effect[] { (581, 40), (583, 1), (590, 10) },
                "Урон лавовой области +40%", "Число лавовых областей +1", "Время перезарядки лавовой области -10%"),
            A("bellows", "Кузнечные меха", "B", 124, new Effect[] { (187, 20), (324, 20), (321, 20) },
                "Размер огненных шаров +20%", "Размер метеоритов +20%", "Урон метеоритов +20%"),
            // лёд
            A("frost", "Иней на ладони", "C", 134, new Effect[] { (371, 30), (511, 30) },
                "Урон ледяных игл +30%", "Урон метели +30%"),
            A("polar", "Кристалл полярной ночи", "A", 80, new Effect[] { (375, 25), (514, 25), (519, 10) },
                "Размер ледяных игл +25%", "Число метелей +25%", "Время перезарядки метели -10%"),
            // сбор
            A("sieve", "Сито старателя", "C", 61, new Effect[] { (40, 40) },
                "Радиус сбора предметов +40%"),
            A("magnet", "Звёздный магнит", "B", 92, new Effect[] { (40, 25), (38, 15), (45, 10) },
                "Радиус сбора предметов +25%", "Получение маны +15%", "Получение крупиц маны +10%"),
            // крит
            A("edge", "Остриё удачи", "B", 64, new Effect[] { (14, 8), (15, 20) },
                "Шанс критического удара +8%", "Множитель критического урона +20%"),
            A("seal", "Палаческая печать", "A", 111, new Effect[] { (15, 60), (14, 5) },
                "Множитель критического урона +60%", "Шанс критического удара +5%"),
            // танк
            A("skin", "Каменная кожа", "C", 103, new Effect[] { (23, 12), (20, 10) },
                "Получаемый урон -12%", "Максимальное здоровье +10%"),
            A("heart", "Сердце стража", "B", 91, new Effect[] { (20, 35), (24, 30) },
                "Максимальное здоровье +35%", "Доля восстановления здоровья в сек. +0,3%"),
            A("bastion", "Щит бастиона", "A", 90, new Effect[] { (23, 20), (25, 8), (20, 20) },
                "Получаемый урон -20%", "Шанс уклонения +8%", "Максимальное здоровье +20%"),
        };

        public ModDef Define() => new ModDef
        {
            Id = "artifact_pack", Name = "Набор артефактов", Version = "1.0",
            Description = "12 новых артефактов (огонь, лёд, сбор, крит, танк) и 2 синергии. Собрано, на устройстве не проверено.",
            Depends = { ["modkit_data"] = ">=1.5", ["modkit_artifacts"] = ">=1.0", ["modkit_synergies"] = ">=1.0" },
            Options =
            {
                new ModOption { Key = "weight", Label = "Вес выпадения (копий в пуле)", Default = 4, Min = 1, Max = 10, Step = 1 },
                new ModOption { Key = "synergies", Label = "Добавить 2 синергии (1/0)", Default = 1, Min = 0, Max = 1, Step = 1 },
                new ModOption { Key = "test_drop", Label = "Тест: выпадают почти всегда (1/0)", Default = 0, Min = 0, Max = 1, Step = 1 },
            },
            OnEnable = ctx =>
            {
                var data = ctx.Use<IData>("modkit_data");
                data.WhenReady(() => ctx.Guard("добавление артефактов", () => Build(ctx, data)));
            },
        };

        private static void Build(ModContext ctx, IData data)
        {
            int weight = (int)ctx.Setting("weight");
            bool test = ctx.Setting("test_drop") > 0;
            var defs = Items.Select(it => new ArtifactDef
            {
                Name = it.Name, Rarity = it.Rarity, CloneFrom = it.Clone, Weight = weight, Guaranteed = test,
                Effects = it.Fx.ToList(), Lines = it.Lines.ToList(),
            }).ToList();
            var ids = ctx.Use<IArtifacts>("modkit_artifacts").AddMany(defs);
            var byKey = new Dictionary<string, int>();
            for (int i = 0; i < Items.Length; i++)
            {
                byKey[Items[i].Key] = ids[i];
                ctx.Log($"артефакт «{Items[i].Name}» ({Items[i].Rarity}): ID {ids[i]}, эффекты {string.Join(", ", Items[i].Fx.Select(e => $"{e.Id}={e.Value}"))}, вес {(test ? "тест" : weight.ToString())}");
            }

            var synIds = new List<int>();
            if (ctx.Setting("synergies") > 0)
            {
                synIds.Add(ctx.Use<ISynergies>("modkit_synergies").Add(new SynergyDef
                {
                    Name = "Огненная кузница",
                    Members = new[] { byKey["coal"], byKey["lava"], byKey["bellows"] },
                    Effects = { new Effect(181, 25), new Effect(581, 25), new Effect(461, 25) },
                    Lines = new List<string> { "Урон огненных шаров +25%", "Урон лавовой области +25%", "Урон испепеления +25%" },
                }));
                synIds.Add(ctx.Use<ISynergies>("modkit_synergies").Add(new SynergyDef
                {
                    Name = "Несокрушимый",
                    Members = new[] { byKey["skin"], byKey["heart"], byKey["bastion"] },
                    Effects = { new Effect(23, 8), new Effect(20, 15), new Effect(4, 10) },
                    Lines = new List<string> { "Получаемый урон -8%", "Максимальное здоровье +15%", "Атака +10%" },
                }));
                ctx.Log($"синергии: «Огненная кузница» ID {synIds[0]} (артефакты {byKey["coal"]}, {byKey["lava"]}, {byKey["bellows"]}), «Несокрушимый» ID {synIds[1]} (артефакты {byKey["skin"]}, {byKey["heart"]}, {byKey["bastion"]})");
            }
            Verify(ctx, data, ids, synIds);
        }

        /// <summary>Читает добавленные строки обратно из таблиц и пишет итог в журнал.</summary>
        private static void Verify(ModContext ctx, IData data, List<int> ids, List<int> synIds)
        {
            int ok = 0;
            var t = data.Table("Ability");
            for (int i = 0; i < ids.Count; i++)
            {
                int row = t.IndexOf(ids[i], t.IdColumn);
                if (row < 0) { ctx.Error($"артефакт {ids[i]} не найден в таблице Ability"); continue; }
                var fx = Items[i].Fx;
                bool good = (t.Get(row, "이름") as string) == Items[i].Name;
                for (int n = 1; n <= fx.Length && good; n++)
                    good = (int)t.Num(row, $"효과ID_{n:00}") == fx[n - 1].Id && Math.Abs(t.Num(row, $"효과ID_{n:00}값") - fx[n - 1].Value) < 1e-6;
                if (good) ok++; else ctx.Error($"артефакт {ids[i]} «{Items[i].Name}»: ячейки не совпали с описанием");
            }
            var s = data.Table("Synergy");
            int sOk = 0;
            foreach (var id in synIds)
            {
                int row = s.IndexOf(id, s.IdColumn);
                if (row >= 0 && s.Num(row, "개수") == 3) sOk++; else ctx.Error($"синергия {id}: не найдена или не 3 артефакта");
            }
            ctx.Log($"проверка по таблицам: артефактов {ok}/{ids.Count}, синергий {sOk}/{synIds.Count}; ID артефактов {ids.First()}..{ids.Last()}");
        }
    }
}
