using System;
using System.Collections.Generic;
using System.Linq;
using Il2Cpp;
using ModKit;
using ModKitData;

namespace AutoArtifact
{
    /// <summary>
    /// «Артефакт на старте боя»: при входе в бой сразу кладёт в список артефактов героя артефакты с заданными ID
    /// (ID — в docs/CATALOG.md) и выставляет счётчики синергий. Выбор сундука и обычное получение не затрагиваются.
    /// </summary>
    public class AutoArtifactMod : IKitMod
    {
        private const int Slots = 8;
        // по умолчанию — синергия 33 «Гильгамеш»: Богатство, Ковчег Завета, Корона, Золотая рулетка, Монета
        private static readonly int[] Gilgamesh = { 75, 176, 177, 198, 218 };
        private static string IdKey(int i) => i == 0 ? "id" : $"id{i + 1}";
        private static string CntKey(int i) => $"cnt{i + 1}";

        public ModDef Define()
        {
            var def = new ModDef
            {
                Id = "auto_artifact",
                Name = "Артефакт на старте боя",
                Description = "Сразу выдаёт выбранные артефакты (ID и количество — в «Настройки») в начале боя.",
                Version = "2.2",
                OnEnable = OnEnable,
                OpenSettings = ctx => UiKit.OpenNumberForm(new UiKit.NumberFormOptions
                {
                    Name = "ModKitAutoArtifactSettings",
                    Title = "Артефакт на старте боя",
                    Subtitle = "ID артефакта (0 — нет, см. docs/CATALOG.md) и сколько копий выдать. Применится в следующем бою.",
                    Columns = new List<string> { "ID", "Кол-во" },
                    Rows = Enumerable.Range(0, Slots).Select(i => new UiKit.NumberFormRow
                    {
                        Label = $"Слот {i + 1}",
                        Fields =
                        {
                            new UiKit.NumberFormField { Key = IdKey(i), Min = 0, Max = 9999 },
                            new UiKit.NumberFormField { Key = CntKey(i), Min = 0, Max = 99 },
                        },
                    }).ToList(),
                    Get = key => ctx.Setting(key),
                    Set = (key, v) => ctx.SetSetting(key, v),
                }),
            };
            for (int i = 0; i < Slots; i++)
                def.Options.Add(new ModOption { Key = IdKey(i), Label = $"ID артефакта {i + 1}", Default = i < Gilgamesh.Length ? Gilgamesh[i] : 0, Min = 0, Max = 9999, Step = 1, Hidden = true });
            for (int i = 0; i < Slots; i++)
                def.Options.Add(new ModOption { Key = CntKey(i), Label = $"Количество {i + 1}", Default = 1, Min = 0, Max = 99, Step = 1, Hidden = true });
            return def;
        }

        private static int CountOf(Il2CppSystem.Collections.Generic.List<int> list, int id)
        {
            int n = 0;
            for (int i = 0; i < list.Count; i++) if (list[i] == id) n++;
            return n;
        }

        private static Dictionary<int, int> WantOf(List<int> ids)
        {
            var m = new Dictionary<int, int>();
            foreach (var id in ids) m[id] = (m.TryGetValue(id, out var v) ? v : 0) + 1;
            return m;
        }

        /// <summary>Игра ведёт счётчик артефактов по синергиям (Array_SynergyObtain[ID синергии]) только при штатном получении — выставляем сами.</summary>
        private static void SyncSynergies(ModContext ctx, Il2CppSystem.Collections.Generic.List<int> list)
        {
            var have = new HashSet<int>();
            for (int i = 0; i < list.Count; i++) have.Add(list[i]);
            var arr = Z_Database.Array_SynergyObtain;
            var on = Z_Database.List_ApplySynergyON;
            var t = new Table("Synergy");
            var done = new List<string>();
            for (int i = 0; i < t.Length; i++)
            {
                if (!(t.Get(i, "ID") is double idd)) continue;
                int id = (int)idd;
                if (id < 0 || id >= arr.Length) continue;
                int n = 0;
                for (int k = 1; k <= 5; k++) if (have.Contains((int)t.Num(i, $"ID_0{k}"))) n++;
                if (n > arr[id]) { arr[id] = n; done.Add($"{id}={n}/{t.Num(i, "개수")}"); }
                // синергия собрана -> флаг включения (его читает пересчёт характеристик)
                int need = (int)t.Num(i, "개수");
                if (need > 0 && n >= need && on != null && id < on.Count && on[id] == 0)
                {
                    on[id] = 1;
                    done.Add($"ON[{id}]=1");
                }
            }
            ctx.Log($"счётчики синергий: {(done.Count > 0 ? string.Join(", ", done) : "без изменений")}");
        }

        private void OnEnable(ModContext ctx)
        {
            bool pending = false;
            int warm = 0, tick = 0, poolTick = 0, stage = 0, logged = 0;
            double trimUntil = 0;
            var obtained = Z_Database.List_ArtifactObtain;

            List<int> WantedIds()
            {
                var ids = new List<int>();
                for (int i = 0; i < Slots; i++)
                {
                    int id = (int)Math.Round(ctx.Setting(IdKey(i))), n = (int)Math.Round(ctx.Setting(CntKey(i)));
                    if (id > 0) for (int k = 0; k < n; k++) ids.Add(id);
                }
                return ids;
            }

            // Копий одного ID в настройках — столько и нужно. Лишнее убираем только вскоре после выдачи.
            void TrimExtra(List<int> ids)
            {
                var l = Z_Database.List_ArtifactObtain;
                foreach (var kv in WantOf(ids))
                {
                    int extra = CountOf(l, kv.Key) - kv.Value;
                    while (extra-- > 0)
                    {
                        for (int i = l.Count - 1; i >= 0; i--)
                            if (l[i] == kv.Key) { l.RemoveAt(i); break; }
                        ctx.Log($"убрана лишняя копия артефакта {kv.Key}");
                    }
                }
            }

            // убирает выданные ID из пулов сундуков и торговца (List_ArtifactPossibleGrade_*, List_ArtifactDrop)
            void RemoveFromChests(List<int> ids)
            {
                foreach (var id in ids.Distinct())
                {
                    var was = Commands.TakeFromPools(id);
                    if (was.Count > 0) ctx.Log($"артефакт {id} убран из сундуков ({string.Join(", ", was.Select(w => $"{w.pool}×{w.n}"))})");
                }
            }

            // игра зовёт AbilityStatApply при старте героя: если артефактов ещё нет, просим выдать наш
            ctx.After("V_Ability_Manager", "AbilityStatApply", 1, (self, args) =>
            {
                if (stage == 0) pending = true;
                if (logged++ < 5) ctx.Log($"AbilityStatApply (stage={stage})");
            });

            // каждый кадр боя: новый бой — список опять пуст, можно выдавать снова
            ctx.Before("C_Battle_Manager", "Update", 0, (self, args) =>
            {
                try
                {
                    var ids = WantedIds();
                    var list = Z_Database.List_ArtifactObtain;
                    if (stage == 1 && list.Count < ids.Count) { stage = 0; warm = 0; }     // новый бой: список снова короткий
                    // AbilityStatApply мог быть вызван до включения мода — тогда ждём ~2 с боя и выдаём сами
                    if (stage == 0 && !pending && ++warm == 120) { pending = true; ctx.Log("AbilityStatApply не было — выдаю по таймеру"); }
                    // при возврате в идущий бой игра сама восстанавливает список (с нашими артефактами) — убираем лишние копии
                    if (stage == 1 && Scheduler.Now < trimUntil && ++tick % 30 == 0) TrimExtra(ids);
                    // выданные артефакты не должны снова выпадать из сундуков: игра может пересобрать пулы, поэтому чистим раз в ~0,5 с
                    if (stage == 1 && ++poolTick % 30 == 0) RemoveFromChests(ids);
                    if (!pending) return;
                    pending = false;
                    if (ids.Count == 0) return;                                // все ID = 0: ничего не выдаём
                    var given = new List<int>();
                    foreach (var kv in WantOf(ids))
                    {
                        int have = CountOf(list, kv.Key);                      // уже есть (восстановлен из сохранения боя) — докладываем недостающее
                        for (int k = have; k < kv.Value; k++) { list.Add(kv.Key); given.Add(kv.Key); }
                    }
                    stage = 1;
                    trimUntil = Scheduler.Now + 15;
                    if (given.Count == 0) { ctx.Log("артефакты уже в списке (возврат в бой) — повторно не выдаю"); return; }
                    SyncSynergies(ctx, list);
                    RemoveFromChests(ids);
                    ctx.Log($"выданы артефакты {string.Join(", ", given)}");
                }
                catch (Exception e) { ctx.Log("ошибка: " + e); }
            });
        }
    }
}
