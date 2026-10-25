using System.Collections.Generic;
using Il2Cpp;
using ModKit;

namespace ChestGuard
{
    /// <summary>
    /// Сундук артефактов (V_Artifact_Manager.Create_Artifact) берёт варианты из Z_Database.List_ArtifactDrop (без повторов и без уже
    /// собранных — List_ArtifactObtain) и 3–4 раза подряд забирает первый элемент без проверки длины: когда пул кончается, метод
    /// падает на середине, окно не достраивается, игра остаётся на паузе. Мод перед вызовом дополняет пул «заглушками» —
    /// собранными артефактами, временно убранными из List_ArtifactObtain; после вызова прячет слоты с заглушками, убирает их
    /// из пула и возвращает список собранных как был. Если не собрано ничего и пул пуст, дополнять нечем.
    /// </summary>
    public class ChestGuardMod : IKitMod
    {
        private const int Need = 8;     // 4 варианта + до 4 снятых игрой сверху (ветка обелиска) — с запасом

        public ModDef Define() => new ModDef
        {
            Id = "chest_guard",
            Name = "Защита сундука артефактов",
            Description = "Урезает выбор в сундуке артефактов, когда их не хватает (иначе игра ломается).",
            Version = "1.0",
            OnEnable = ctx =>
            {
                List<int> savedObtain = null;
                double savedAt = 0;           // список собранных до подмены
                var fillers = new HashSet<int>();       // ID-заглушки текущего сундука
                var dropCount = new Dictionary<int, int>(); // сколько раз заглушка была в пуле до подмены (у пула есть веса-повторы)
                ctx.Before("V_Artifact_Manager", "Create_Artifact", 0, (self, args) =>
                {
                    savedObtain = null; fillers.Clear(); dropCount.Clear();
                    var drop = Z_Database.List_ArtifactDrop;
                    var have = Z_Database.List_ArtifactObtain;
                    if (drop == null || have == null) return;
                    var owned = new HashSet<int>();
                    for (int i = 0; i < have.Count; i++) owned.Add(have[i]);
                    var free = new HashSet<int>();
                    for (int i = 0; i < drop.Count; i++) if (!owned.Contains(drop[i])) free.Add(drop[i]);
                    if (free.Count >= Need) return;
                    foreach (var id in owned)
                    {
                        if (free.Count + fillers.Count >= Need) break;
                        if (id > 0) fillers.Add(id);
                    }
                    if (fillers.Count == 0) { ctx.Log("в пуле сундука не осталось артефактов и нечем дополнить"); return; }
                    savedObtain = new List<int>();
                    for (int i = 0; i < have.Count; i++) savedObtain.Add(have[i]);
                    for (int i = have.Count - 1; i >= 0; i--) if (fillers.Contains(have[i])) have.RemoveAt(i);
                    foreach (var id in fillers) dropCount[id] = 0;
                    for (int i = 0; i < drop.Count; i++) if (dropCount.ContainsKey(drop[i])) dropCount[drop[i]]++;
                    foreach (var id in fillers) drop.Add(id);
                    savedAt = Scheduler.Now;
                    ctx.Log($"в пуле сундука {free.Count} артефактов, заглушек: {fillers.Count}");
                });
                // если Create_Artifact всё же упал и «после» не вызвалось — вернуть список собранных через ~1 с
                ctx.Every(500, () =>
                {
                    if (savedObtain == null || Scheduler.Now - savedAt < 1) return;
                    var have = Z_Database.List_ArtifactObtain;
                    have.Clear();
                    foreach (var id in savedObtain) have.Add(id);
                    ctx.Log("сундук не достроился — список собранных артефактов восстановлен");
                    savedObtain = null; fillers.Clear();
                });
                ctx.After("V_Artifact_Manager", "Create_Artifact", 0, (self, args) =>
                {
                    if (savedObtain == null) return;
                    var m = (V_Artifact_Manager)self;
                    var ids = m.List_ArtifactSelectID;
                    var sheets = m.List_ObjArtifactSelectSheet;
                    int hidden = 0;
                    for (int i = 1; ids != null && i <= m.ArtifactSelectNum && i < ids.Count; i++)
                        if (fillers.Contains(ids[i]) && sheets != null && i < sheets.Count && sheets[i] != null) { sheets[i].SetActive(false); hidden++; }
                    var drop = Z_Database.List_ArtifactDrop;
                    // убираем только добавленные копии: заглушек в пуле не больше, чем было до подмены
                    var now = new Dictionary<int, int>();
                    for (int i = drop.Count - 1; i >= 0; i--)
                    {
                        int id = drop[i];
                        if (!fillers.Contains(id)) continue;
                        now[id] = now.TryGetValue(id, out var c) ? c + 1 : 1;
                    }
                    for (int i = drop.Count - 1; i >= 0; i--)
                    {
                        int id = drop[i];
                        if (!fillers.Contains(id) || now[id] <= dropCount[id]) continue;
                        drop.RemoveAt(i); now[id]--;
                    }
                    var have = Z_Database.List_ArtifactObtain;
                    have.Clear();
                    foreach (var id in savedObtain) have.Add(id);
                    ctx.Log($"сундук: скрыто слотов-заглушек {hidden}");
                    savedObtain = null; fillers.Clear();
                });
            },
        };
    }
}
