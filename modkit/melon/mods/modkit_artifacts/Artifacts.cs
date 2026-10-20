using System;
using System.Collections.Generic;
using System.Linq;
using Il2Cpp;
using Il2CppInterop.Runtime;
using ModKit;
using UnityEngine;
using UnityEngine.UI;

namespace ModKitData
{
    /// <summary>
    /// Добавление новых артефактов (всё делает AddArtifact):
    ///  1. строка в таблице Ability (копия образца) + иконка в SpriteList_AbilityPortrait;
    ///  2. запись в пулы выпадения List_ArtifactPossibleGrade_* (вес = число копий; у обычных артефактов их 5);
    ///  3. запись в Z_Database.Dictionary_ApplySynergy (индекс = ID − 41) — без неё окно описания в бою падает с ArgumentOutOfRange;
    ///  4. плитка в энциклопедии (клонируем существующую) и своё окно описания.
    /// </summary>
    internal static class Artifacts
    {
        private class Entry
        {
            public int Id, Grade, CloneFrom;
            public string Name; public List<string> Lines; public string IconFile;
            public bool SynergyAssigned;
        }

        private const int FirstArtifactId = 41;      // ID существующих артефактов идут подряд 41..222
        private static readonly List<Entry> Registry = new List<Entry>();
        private static int _detailShownFor = -1;     // listIndex артефакта со своей иконкой, открытого сейчас в окне описания

        private static int GradeOf(int id)
        {
            for (int g = 0; g < Commands.Grades.Length; g++)
                if (Commands.Pool("List_ArtifactPossibleGrade_" + Commands.Grades[g]).IndexOf(id) >= 0) return g + 1;
            return 3;
        }

        public static void UpdateAdded(int id, string name, List<string> lines)
        {
            var e = Registry.FirstOrDefault(x => x.Id == id);
            if (e == null) return;
            if (name != null) e.Name = name;
            if (lines != null) e.Lines = lines;
        }

        /// <summary>Добавляет сразу несколько артефактов: массив иконок растёт один раз. Возвращает ID по порядку.</summary>
        public static List<int> AddMany(List<ArtifactDef> defs, Action<Action> undo, string owner)
        {
            if (defs.Count == 0) return new List<int>();
            var first = defs[0].Icon ?? defs[0].CloneFrom;
            Sprites.Ensure("SpriteList_AbilityPortrait", (int)new Table("Ability").MaxId() + defs.Count, first);
            return defs.Select(d => Add(d, undo, owner)).ToList();
        }

        public static int Add(ArtifactDef def, Action<Action> undo, string owner)
        {
            var t = new Table("Ability");
            var idCol = t.IdColumn;
            int src = t.IndexOf(def.CloneFrom, idCol);
            if (src < 0) throw new Exception($"Ability: нет артефакта-образца {def.CloneFrom}");
            int grade = def.Rarity != null ? Array.IndexOf(Commands.Grades, def.Rarity) + 1 : GradeOf(def.CloneFrom);
            var lines = def.Lines ?? Commands.DefaultLines(def.Effects);

            int row = t.CloneRow(src);
            int id = (int)t.MaxId() + 1;
            t.Set(row, idCol, id);
            int was = Sprites.Ensure("SpriteList_AbilityPortrait", id, def.Icon ?? def.CloneFrom);
            t.Set(row, "이름", def.Name);
            var cols = new HashSet<string>(t.Columns());
            foreach (var kv in Commands.TextChanges(t, def.Effects, lines, 9, 6, "효과 설명줄 개수")) t.Set(row, kv.Key, kv.Value);
            Log.Write($"[{owner}] артефакт «{def.Name}»: ID {id}, образец {def.CloneFrom}, редкость {Commands.Grades[grade - 1]}, иконок было {was}");

            // пулы выпадения
            var pools = def.Guaranteed ? Commands.Grades : new[] { Commands.Grades[grade - 1] };
            int copies = def.Guaranteed ? 400 : def.Weight;
            var added = new List<(Il2CppSystem.Collections.Generic.List<int> list, int v)>();
            foreach (var g in pools)
            {
                var l = Commands.Pool("List_ArtifactPossibleGrade_" + g);
                for (int i = 0; i < copies; i++) { l.Add(id); added.Add((l, id)); }
            }

            var entry = new Entry { Id = id, Grade = grade, CloneFrom = def.CloneFrom, Name = def.Name, Lines = lines, IconFile = def.IconFile };
            Registry.Add(entry);
            // общая подмена иконок (ModKit.ArtIcons): энциклопедия, выбор, торговец и бой; прежний оверлей книги остаётся запасным путём
            var png = ReadIcon(def.IconFile);
            if (png != null && ArtIcons.Set(owner, id, png)) entry.IconFile = null;
            undo(() => ArtIcons.Clear(id));
            try { EnsureSynergySlot(entry); } catch (Exception e) { Log.Write($"[{owner}] ApplySynergy: {e.Message}"); }

            undo(() =>
            {
                foreach (var a in added) a.list.Remove(a.v);
                Registry.Remove(entry);
                var tt = new Table("Ability");
                int ti = tt.IndexOf(id, idCol);
                if (ti == tt.Length - 1) tt.RemoveLast();
            });
            return id;
        }

        // ---------------------------------------------------------------- Dictionary_ApplySynergy

        private static Il2CppSystem.Collections.Generic.Dictionary<int, int> CloneDict(Il2CppSystem.Collections.Generic.Dictionary<int, int> src)
        {
            var copy = new Il2CppSystem.Collections.Generic.Dictionary<int, int>();
            foreach (var kv in src) copy[kv.Key] = kv.Value;
            return copy;
        }

        private static void EnsureSynergySlot(Entry e)
        {
            var list = Z_Database.Dictionary_ApplySynergy;
            if (list == null) return;
            int need = e.Id - FirstArtifactId;
            int count = list.Count;
            if (count > need && e.SynergyAssigned) return;
            int sampleIdx = e.CloneFrom - FirstArtifactId;
            // список наполняется игрой позже запуска мода; обращение за границу даёт исключение IL2CPP -> abort, после которого игра зависает
            if (count <= sampleIdx) return;
            var sample = list[sampleIdx];
            if (count <= need) e.SynergyAssigned = false;                    // игра пересобрала список — заполняем заново
            while (count <= need) { list.Add(CloneDict(sample)); count++; }
            list[need] = CloneDict(sample);
            e.SynergyAssigned = true;
            Log.Write($"Dictionary_ApplySynergy: запись для ID {e.Id} (в списке {count})");
        }

        // ---------------------------------------------------------------- энциклопедия

        private static V_ArtifactShow FindShow()
        {
            var arr = UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<V_ArtifactShow>());
            return arr.Length > 0 ? arr[0].TryCast<V_ArtifactShow>() : null;
        }

        /// <summary>Окно описания артефакта (V_ArtifactShow): заполняем поля вручную — штатный путь не знает про чужой ID.</summary>
        private static void ShowDetail(X_Select_ArtifactShow refTile, Entry e, int listIndex)
        {
            var vs = FindShow();
            if (vs == null) return;
            V_ArtifactShow.ArtifactShowIndex = listIndex;
            var portrait = vs.Img_ArtifactDetail_Portrait;
            UiKit.SetSprite(portrait, Sprites.Get("SpriteList_AbilityPortrait", e.Id));
            byte[] icon = ReadIcon(e.IconFile);
            UiKit.SetImageOverlay(portrait, icon);
            _detailShownFor = icon != null ? listIndex : -1;
            var g = vs.Img_ArtifactDetail_SheetGround;
            var rg = refTile.Img_ArtifactSheetGround;
            UiKit.SetSprite(g, UiKit.GetSprite(rg));
            g.color = rg.color;
            vs.Text_ArtifactDetail_Name.text = e.Name;
            vs.Text_ArtifactDetail_Grade.text = "Артефакт ModKit";
            vs.Text_ArtifactDetail_Effect.text = string.Join("\n", e.Lines.Select(l => $"<color=#6EFF3C>{l}</color>"));
            vs.Obj_FieldArtifactDetail.SetActive(true);
        }

        private static byte[] ReadIcon(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            try { return System.IO.File.ReadAllBytes(path); }
            catch (Exception ex) { Log.Write($"иконка {path}: не прочитан ({ex.Message})"); return null; }
        }

        /// <summary>Плитки в книге расставлены координатами, а не сеткой: клон ставим в следующую ячейку после последней.</summary>
        private static void AddBookTile(A_MainMenuInformation menu, Entry e)
        {
            var tf = menu.Obj_FieldBook_ArtifactContentB.transform;
            int n = tf.childCount;

            // окно описания берёт артефакт из списка V_ArtifactShow.Apply_AllyArtifactLearn по ArtifactIndex плитки
            var vs = FindShow();
            int listIndex = -1;
            var list = vs?.Apply_AllyArtifactLearn;
            if (list != null)
            {
                listIndex = list.IndexOf(e.Id);
                if (listIndex < 0) { list.Add(e.Id); listIndex = list.Count - 1; }
            }

            GameObject last = null;
            X_Select_ArtifactShow refTile = null;            // плитка той же редкости — берём цвета рамки и фона
            var tiles = new List<GameObject>();
            for (int i = 0; i < n; i++)
            {
                var go = tf.GetChild(i).gameObject;
                var comp = go.GetComponent<X_Select_ArtifactShow>();
                if (comp == null) continue;
                if (comp.Apply_ArtifactID == e.Id)
                {
                    if (listIndex >= 0) comp.ArtifactIndex = listIndex;      // плитка уже есть
                    return;
                }
                if (refTile == null && comp.Apply_ArtifactGrade == e.Grade) refTile = comp;
                last = go;
                tiles.Add(go);
            }
            if (last == null || refTile == null || tiles.Count < 2) { Log.Write("книга артефактов: нет плитки-образца"); return; }

            var clone = UnityEngine.Object.Instantiate(last.Cast<UnityEngine.Object>()).TryCast<GameObject>();
            clone.transform.SetParent(tf, false);
            var cc = clone.GetComponent<X_Select_ArtifactShow>();
            cc.Apply_ArtifactID = e.Id;
            cc.Apply_ArtifactGrade = e.Grade;
            if (listIndex >= 0) cc.ArtifactIndex = listIndex;
            // Start_ArtifactShow не зовём: он выбирает ID по номеру плитки среди существующих и для нового даёт 0. Вид задаём вручную.
            var tileImg = cc.Img_ArtifactPortrait;
            UiKit.SetSprite(tileImg, Sprites.Get("SpriteList_AbilityPortrait", e.Id));
            var icon = ReadIcon(e.IconFile);
            if (icon != null) UiKit.SetImageOverlay(tileImg, icon);
            foreach (var pair in new[] { (cc.Img_ArtifactSheetContour, refTile.Img_ArtifactSheetContour), (cc.Img_ArtifactSheetGround, refTile.Img_ArtifactSheetGround) })
            {
                UiKit.SetSprite(pair.Item1, UiKit.GetSprite(pair.Item2));
                pair.Item1.color = pair.Item2.color;
            }
            Vector2 Pos(GameObject go) => go.GetComponent<RectTransform>().anchoredPosition;
            var p0 = Pos(tiles[0]); var p1 = Pos(tiles[1]);
            float colStep = p1.x - p0.x;
            int cols = tiles.FindIndex(t => Math.Abs(Pos(t).y - p0.y) > 1);       // плиток в первой строке
            float rowStep = cols > 0 ? Pos(tiles[cols]).y - p0.y : 0;
            var pl = Pos(last);
            float nx = pl.x + colStep, ny = pl.y;
            if (cols > 0 && nx > p0.x + colStep * (cols - 0.5f)) { nx = p0.x; ny = pl.y + rowStep; }     // строка заполнена — следующая строка
            clone.GetComponent<RectTransform>().anchoredPosition = new Vector2(nx, ny);
            clone.SetActive(true);

            // штатная кнопка плитки для нового ID бросает исключение — заменяем своей (Selectable не терпит двух Button, поэтому DestroyImmediate)
            var oldBtn = clone.GetComponent<Button>();
            if (oldBtn != null) UnityEngine.Object.DestroyImmediate(oldBtn);
            var btn = clone.AddComponent<Button>();
            btn.targetGraphic = cc.Img_ArtifactSheetGround;
            var rt = refTile; int li = listIndex;
            btn.onClick.AddListener(UiKit.Act(new UiKit.Callback(() => ShowDetail(rt, e, li), "описание артефакта").Invoke));
            Log.Write($"книга артефактов: плитка для «{e.Name}» (ID {e.Id})");
        }

        private static string _lastFitHandle; private static float _lastFitY = float.NaN; private static float _baseTilesY = float.NaN;

        /// <summary>
        /// Высота списка книги: игра задаёт её при каждом открытии под свои артефакты, наши плитки идут сверх — добавляем высоту
        /// ровно на строки, занятые нашими плитками. Повторный вызов в том же открытии ничего не добавляет.
        /// </summary>
        private static void FitBookHeight(A_MainMenuInformation menu)
        {
            var tf = menu.Obj_FieldBook_ArtifactContentB.transform;
            var ours = new HashSet<int>(Registry.Select(r => r.Id));
            var ys = new List<float>();
            int total = 0, own = 0;
            for (int i = 0; i < tf.childCount; i++)
            {
                var go = tf.GetChild(i).gameObject;
                var comp = go.GetComponent<X_Select_ArtifactShow>();
                if (comp == null) continue;
                if (ys.Count < 2 || total < 200) ys.Add(go.GetComponent<RectTransform>().anchoredPosition.y);
                total++;
                if (ours.Contains(comp.Apply_ArtifactID)) own++;
            }
            if (own == 0 || ys.Count == 0) return;
            float y0 = ys[0];
            int cols = ys.FindIndex(y => Math.Abs(y - y0) > 1);
            if (cols <= 0) return;
            float rowStep = Math.Abs(ys[cols] - y0);
            int Rows(int k) => (k + cols - 1) / cols;
            int extra = Rows(total) - Rows(total - own);
            if (extra <= 0) return;
            // прокручивается не Field_ContentB (точка, в ней лежат плитки), а его родитель Field_ContentA
            var holder = tf.parent.GetComponent<RectTransform>();
            var size = holder.sizeDelta;
            float cur = size.y;
            var handle = holder.Pointer.ToString();
            if (_lastFitHandle == handle && Math.Abs(_lastFitY - cur) < 0.5f) return;     // уже добавили в этом открытии
            float next = cur + extra * rowStep;
            holder.sizeDelta = new Vector2(size.x, next);
            _lastFitHandle = handle; _lastFitY = next;
            // якоря по центру: рост высоты расширяет список и вверх, и вниз — сдвигаем плитки вверх на половину прироста
            var rt = tf.GetComponent<RectTransform>();
            var pos = rt.anchoredPosition;
            if (float.IsNaN(_baseTilesY)) _baseTilesY = pos.y;
            rt.anchoredPosition = new Vector2(pos.x, _baseTilesY + extra * rowStep / 2);
            Log.Write($"книга артефактов: плиток {total} (наших {own}), колонок {cols}, +{extra} стр.: высота {Math.Round(cur)} -> {Math.Round(next)}");
        }

        /// <summary>Своя иконка в окне описания — общий Image игры: как только открыт другой артефакт, убираем наш RawImage.</summary>
        private static void WatchDetail()
        {
            var vs = FindShow();
            if (vs == null) { _detailShownFor = -1; return; }
            bool open = vs.Obj_FieldArtifactDetail.activeSelf;
            if (open && V_ArtifactShow.ArtifactShowIndex == _detailShownFor) return;
            UiKit.SetImageOverlay(vs.Img_ArtifactDetail_Portrait, null);
            _detailShownFor = -1;
        }

        // ---------------------------------------------------------------- хуки и периодические задачи библиотеки

        private static int _tick;

        /// <summary>Вызывается из OnEnable библиотеки: хук книги артефактов и таймеры (снимаются сами при выключении библиотеки).</summary>
        public static void Install(ModContext lib)
        {
            lib.After("A_MainMenuInformation", "Button_BookArtifact", 0, (self, args) =>
            {
                if (Registry.Count == 0) return;
                try { var menu = (A_MainMenuInformation)self; foreach (var e in Registry) AddBookTile(menu, e); FitBookHeight(menu); }
                catch (Exception ex) { Log.Write("ошибка книги артефактов: " + ex); }
            });
            lib.Every(100, () =>
            {
                if (_detailShownFor >= 0) { try { WatchDetail(); } catch (Exception ex) { _detailShownFor = -1; Log.Write("иконка в окне описания: " + ex.Message); } }
                if (++_tick % 20 != 0) return;
                try { foreach (var e in Registry) EnsureSynergySlot(e); } catch { /* список ещё не создан */ }    // игра может пересобрать список при старте боя
                foreach (var id in ArtifactCommands.Banned.ToList()) Commands.TakeFromPools(id);
            });
            lib.OnCleanup(() => { Registry.Clear(); _detailShownFor = -1; });
        }
    }
}
