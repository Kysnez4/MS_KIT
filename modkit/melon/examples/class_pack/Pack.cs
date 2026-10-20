using System;
using System.Collections.Generic;
using System.Linq;
using ModKit;

namespace ClassPack
{
    /// <summary>
    /// 8 классов мода по школам игры: огонь, лёд, спутники, духи, молния, крит, сбор, танк. Только характеристики (ID из docs/EFFECTS.md),
    /// отрицательные ID (стартовые заклинания) не используются — в бою не проверены. Эффекты применяет class_menu через SetGlobalEffects
    /// и они складываются с выбранным классом игры. Ключи c1..c8 задают порядок номеров в class_menu (сортировка по ключу).
    /// </summary>
    public class PackMod : IKitMod
    {
        private class Def
        {
            public string Key, Name, Subtitle; public string[] Lines; public (int Id, double Value)[] Effects;
        }

        private static readonly Def[] Classes =
        {
            new Def
            {
                Key = "c1_fire", Name = "Огненный мастер", Subtitle = "школа огня",
                Lines = new[] { "Урон огненных шаров +40%", "Урон испепеления +30%", "Урон метеоритов +30%", "Атака +5%" },
                Effects = new (int, double)[] { (181, 40), (461, 30), (321, 30), (4, 5) },
            },
            new Def
            {
                Key = "c2_frost", Name = "Ледяной жрец", Subtitle = "школа льда",
                Lines = new[] { "Урон ледяных игл +40%", "Урон метели +40%", "Время перезарядки ледяных игл -15%", "Время перезарядки метели -15%" },
                Effects = new (int, double)[] { (371, 40), (511, 40), (378, 15), (519, 15) },
            },
            new Def
            {
                Key = "c3_orbit", Name = "Страж орбит", Subtitle = "школа спутников",
                Lines = new[] { "Урон спутников +40%", "Число спутников +2", "Скорость вращения спутников +25%" },
                Effects = new (int, double)[] { (306, 40), (308, 2), (311, 25) },
            },
            new Def
            {
                Key = "c4_spirit", Name = "Повелитель духов", Subtitle = "школа духов",
                Lines = new[] { "Урон духов +40%", "Число духов +2", "Время перезарядки духов -15%" },
                Effects = new (int, double)[] { (271, 40), (273, 2), (281, 15) },
            },
            new Def
            {
                Key = "c5_storm", Name = "Грозовой посланник", Subtitle = "школа молнии",
                Lines = new[] { "Урон молний +40%", "Число молний +1", "Урон электрического удара +30%", "Урон области электротока +30%" },
                Effects = new (int, double)[] { (211, 40), (213, 1), (421, 30), (291, 30) },
            },
            new Def
            {
                Key = "c6_crit", Name = "Охотник за слабостями", Subtitle = "школа критов",
                Lines = new[] { "Шанс критического удара +10%", "Множитель критического урона +40%", "Атака +10%" },
                Effects = new (int, double)[] { (14, 10), (15, 40), (4, 10) },
            },
            new Def
            {
                Key = "c7_gather", Name = "Собиратель", Subtitle = "школа сбора",
                Lines = new[] { "Радиус сбора предметов +60%", "Получение маны +25%", "Получение крупиц маны +15%", "Исцеление от крупицы жизни +30%" },
                Effects = new (int, double)[] { (40, 60), (38, 25), (45, 15), (36, 30) },
            },
            new Def
            {
                Key = "c8_tank", Name = "Несокрушимый", Subtitle = "школа защиты",
                Lines = new[] { "Максимальное здоровье +30%", "Получаемый урон -12%", "Доля восстановления здоровья в сек. +0,3%", "Шанс уклонения +5%" },
                Effects = new (int, double)[] { (20, 30), (23, 12), (24, 30), (25, 5) },
            },
        };

        public ModDef Define() => new ModDef
        {
            Id = "class_pack", Name = "Набор классов", Version = "2.0",
            Description = "8 классов мода (огонь, лёд, спутники, духи, молния, крит, сбор, танк) для меню «Классы мода». Только характеристики. Собрано, на устройстве не проверено.",
            Depends = { ["class_menu"] = ">=2.1" },
            OnEnable = ctx =>
            {
                var reg = ctx.Registry<CustomClass>("classes");
                foreach (var d in Classes)
                {
                    var c = new CustomClass { Name = d.Name, Subtitle = d.Subtitle };
                    c.Lines.AddRange(d.Lines);
                    c.Effects.AddRange(d.Effects);
                    reg.Register(d.Key, c);
                    ctx.Log($"класс «{d.Name}» ({d.Key}): " + string.Join(", ", d.Effects.Select(e => $"{e.Id}={e.Value}")));
                }
                ctx.Log($"зарегистрировано классов мода: {Classes.Length}");
            },
        };
    }
}
