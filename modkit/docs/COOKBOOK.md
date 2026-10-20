# Рецепты

Каждый рецепт — метод в `modkit/melon/examples/cookbook/Cookbook.cs`: проект собирается вместе с ядром, поэтому код ниже компилируется. В игре мод `cookbook` включает нужный рецепт настройкой `recipe` (1..12). Библиотеки для рецептов указаны в его `mod.json`. Пометки ✅/◻ — проверено в бою или нет (по [LIBRARIES.md](LIBRARIES.md)).

Общая рамка:
```csharp
public class CookbookMod : IKitMod
{
    public ModDef Define() => new ModDef { Id = "cookbook", Name = "Книга рецептов", Version = "1.0",
        OnEnable = ctx => Hooks(ctx) };
}
```

## 1. Выполнить код до или после метода игры ✅
```csharp
ctx.After("C_Battle_Manager", "Start", 0, (self, args) => ctx.Log("бой начался"));
ctx.Before("C_Battle_AllyUnit", "Unit_Damage", 3, (self, args) => ctx.Log($"герой получит урон {Convert.ToDouble(args[0]):0}"));
```
Число — количество параметров метода. Имена классов и методов — из `dump.cs` или декомпиляции ([DECOMP.md](DECOMP.md)).

## 2. События ✅
```csharp
ctx.Events.On("enemyDeath", o => { var d = (EventData)o; ctx.Log("враг умер, тип " + d.Get<int>("unitId")); });
ctx.Events.On<BattleResult>(r => ctx.Log($"итог боя: {r.Kills} убийств за {r.Time:0} с"));   // нужна modkit_game
ctx.Events.On("cookbook:ping", o => ctx.Log("получен ping"));
ctx.Events.Emit("cookbook:ping", new EventData { ["n"] = 1 });
```
Список событий — [API.md](API.md) §3.

## 3. Найти врагов и нанести урон ✅
```csharp
var enemies = ctx.Use<IEnemies>("modkit_combat");
var combat = ctx.Use<ICombat>("modkit_combat");
ctx.Timer.Every(2, () =>
{
    var hp = enemies.HeroPos;
    if (!hp.HasValue) return;                                  // боя нет
    foreach (var e in enemies.Near(hp.Value.x, hp.Value.y, 6).Take(3))
        combat.Damage(e, 20);
});
```
`ctx.Timer` идёт только в бою и стоит на паузе. Не сохраняйте `IEnemy` между боями.

## 4. Изменить характеристики героя ✅
```csharp
ctx.Use<IEffects>("modkit_effects").OnHeroPass(view =>
{
    view.MulPercent("UnitApply_MagicBulletDamage", 25);       // +25% урона Магической пули
    var n = view.Field("UnitApply_MagicBulletNum");           // Value читает базу, запись задаёт результат
    n.Value = Math.Min(n.Value, 5);
});
```
Проход вызывается после каждого пересчёта характеристик и раз в ~0,7 с. Имена полей — `UnitApply_*` у `C_Battle_AllyUnit`.

## 5. Свой эффект артефакта ✅
```csharp
var fx = ctx.Use<IEffects>("modkit_effects");
fx.Define(new EffectDef
{
    Id = 10100, Name = "Урон пули в %",
    Apply = (view, value, c) => view.MulPercent("UnitApply_MagicBulletDamage", value),
});
```
ID свои — от 10000 (меньшие заняты игрой). Эффект срабатывает у любого артефакта или комбинации, в чьей колонке «효과ID» записан этот ID (рецепт 6 так и делает).

## 6. Новый артефакт ✅
```csharp
ctx.Use<IData>("modkit_data").WhenReady(() => ctx.Guard("артефакт", () =>
{
    int id = ctx.Use<IArtifacts>("modkit_artifacts").Add(new ArtifactDef
    {
        Name = "Рецепт", Rarity = "B", CloneFrom = 42, Weight = 5,
        Effects = { new Effect(4, 10) },                        // 4 = атака, +10
        Lines = new List<string> { "Атака +10%" },
    });
    ctx.Log("артефакт добавлен, ID " + id);
}));
```
Нужны `using ModKitData;` и в `mod.json`: `"restartRequired": true`. ID эффектов игры — [EFFECTS.md](EFFECTS.md). Чтобы артефакт выпадал почти всегда при тесте, поставьте `Guaranteed = true`.

## 7. Активное заклинание: кнопка на экране боя ✅
```csharp
ctx.Use<ISpells>("modkit_spells").AddActive(new ActiveSpellDef
{ SpellId = 2, Label = "Огонь", Cooldown = 8, Burst = 3, OnCast = n => ctx.Log("залпов " + n) });
```
Авто-каст выбранного заклинания отключается; вернётся при выключении мода. ID заклинаний — [LIBRARIES.md](LIBRARIES.md) §7.

## 8. Союзник ✅
```csharp
var spells = ctx.Use<ISpells>("modkit_spells");
var companions = ctx.Use<ICompanions>("modkit_companions");
bool spawned = false;
ctx.Timer.Every(3, () =>
{
    if (!spells.InBattle || spawned) return;
    spawned = companions.Spawn(1.5, 1, spellId: 2, mode: CompanionMode.Hunt) != null;
});
ctx.Events.On("battleStart", _ => spawned = false);
```

## 9. Хранить данные, показать подпись ✅
```csharp
int runs = ctx.Store.Get("runs", 0) + 1;
ctx.Store.Set("runs", runs);
ctx.Timer.After(5, () => ctx.Toast($"Запусков мода: {runs}", 3));
```

## 10. Своё окно ✅
```csharp
var win = UiKit.OpenPage("cookbook", "Книга рецептов", "Пример окна");
UiKit.Label(win.rt, "Привет из мода", 30, win.top, 600, 40, 28);
UiKit.AddButton(win.rt, "Нажми", 30, win.top + 60, 300, 70, 28, new[] { 0.2, 0.5, 0.9, 1 }, () => ctx.Toast("нажато", 2));
```
Чтобы окно открывалось кнопкой «Настройки» карточки мода, вызовите этот код из `ModDef.OpenSettings`. Готовые окна: `UiKit.OpenChecklist`, `UiKit.OpenNumberForm`.

## 11. Ресурс мода и триггеры ✅
```csharp
var hope = ctx.Use<IResources>("modkit_resources").Create("hope", 100, 2);
hope.ShowBar("Надежда", 20, 300, 300, 28, null);
ctx.Use<ITriggers>("modkit_triggers").OnKills(50, n => hope.Add(10));
ctx.Use<ITriggers>("modkit_triggers").OnLowHp(0.3, 20, () => { if (hope.TrySpend(30)) ctx.Use<IHero>("modkit_hero").Heal(100); });
```

## 12. Общие данные между модами ✅
```csharp
var reg = ctx.Registry<string>("cookbook.notes");
reg.Register("hello", "привет");
ctx.Log("записей: " + reg.All.Count);
```
Записи убираются при выключении мода. Для сложных типов объявите тип в библиотеке (как `CustomClass` в `class_menu`).

## Чего в рецептах нет (смотрите готовые моды)
| Задача | Мод-образец |
|---|---|
| Лимит числа снарядов, срез после пересчёта | `projectile_cap` |
| Бонус за серию убийств через глобальные эффекты | `kill_streak` |
| Свои иконки артефактов | `art_swap` |
| Окно настроек на `OpenNumberForm` | `auto_artifact`, `spell_tuner` |
| Карта на `PanView` | `achievements` |
| Свой класс | `class_menu` + пример `class_pack` |
| Заменить метод игры | `ctx.Replace` ([API.md](API.md) §2) |
| Библиотека для других модов | `modkit/melon/examples/demo_lib` и `demo_user` |
