using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace ModKit
{
    /// <summary>Свои иконки артефактов: одна подмена работает везде — в энциклопедии, окнах выбора, у торговца, в описании в бою.</summary>
    public interface IArt
    {
        /// <summary>Подменяет иконку артефакта (PNG, лучше 248x288 с прозрачностью). false — иконку не удалось выделить (см. журнал). Снимается при выключении мода.</summary>
        bool SetIcon(int artifactId, byte[] png);
        /// <summary>Возвращает родную иконку.</summary>
        void ClearIcon(int artifactId);
    }

    /// <summary>
    /// Иконка артефакта — спрайт Z_Database.SpriteList_AbilityPortrait[ID] из сжатого атласа: пиксели не заменить. Поэтому поверх каждого
    /// Image с таким спрайтом кладётся RawImage (UiKit.SetImageOverlay). Находим такие Image двумя способами:
    ///  1) хук V_Artifact_Manager.Show_ArtifactDetail — там известен ID и целевой Image (работает и для общего спрайта у клонов);
    ///  2) обход активных Image по таймеру: спрайт Image == спрайт переопределённого артефакта (плитки, листы выбора, торговец, список).
    /// У клонов (новые артефакты) спрайт общий с образцом — для обхода делаем им свой экземпляр спрайта (Object.Instantiate).
    /// </summary>
    public static unsafe class ArtIcons
    {
        private const string Field = "SpriteList_AbilityPortrait";

        private class Over { public int Id; public byte[] Png; public string Owner; public IntPtr Own, Orig; }

        private static readonly Dictionary<int, Over> ById = new Dictionary<int, Over>();
        private static readonly Dictionary<IntPtr, Over> BySprite = new Dictionary<IntPtr, Over>();
        private static readonly Dictionary<IntPtr, IntPtr> Tracked = new Dictionary<IntPtr, IntPtr>();       // Image с нашим оверлеем -> спрайт, с которым наложили
        private static IntPtr _zdb;
        private static bool _hooked;
        private static double _nextScan;

        private static IntPtr Zdb => _zdb != IntPtr.Zero ? _zdb : _zdb = Raw.Class("Assembly-CSharp.dll", "", "Z_Database");

        private static IntPtr ArrayPtr()
        {
            IntPtr arr = IntPtr.Zero;
            IL2CPP.il2cpp_field_static_get_value(IL2CPP.GetIl2CppField(Zdb, Field), &arr);
            return arr;
        }

        private static IntPtr SpriteAt(int id)
        {
            var arr = ArrayPtr();
            if (arr == IntPtr.Zero || id < 0 || id >= Marshal.ReadInt32(arr + 0x18)) return IntPtr.Zero;
            return Marshal.ReadIntPtr(arr + 0x20 + id * IntPtr.Size);
        }

        private static void PutSprite(int id, IntPtr sprite)
        {
            var arr = ArrayPtr();
            if (arr != IntPtr.Zero && id >= 0 && id < Marshal.ReadInt32(arr + 0x18)) Marshal.WriteIntPtr(arr + 0x20 + id * IntPtr.Size, sprite);
        }

        private static bool Shared(int id, IntPtr sp)
        {
            var arr = ArrayPtr();
            int n = Marshal.ReadInt32(arr + 0x18);
            for (int i = 0; i < n; i++) if (i != id && Marshal.ReadIntPtr(arr + 0x20 + i * IntPtr.Size) == sp) return true;
            return false;
        }

        /// <summary>Свой экземпляр спрайта (копия объекта Sprite на ту же область атласа), чтобы отличать клон от образца по указателю.</summary>
        private static IntPtr Clone(IntPtr sp)
        {
            var k = Raw.Class("UnityEngine.CoreModule.dll", "UnityEngine", "Object");
            var copy = Raw.Invoke(Raw.Method(k, "Instantiate", 1), IntPtr.Zero, sp);
            if (copy == IntPtr.Zero || copy == sp) throw new Exception("Instantiate вернул тот же спрайт");
            Raw.Pin(copy);
            return copy;
        }

        // ---------------------------------------------------------------- подмена

        public static bool Set(string owner, int id, byte[] png)
        {
            if (png == null || png.Length == 0) { Clear(id); return false; }
            var sp = SpriteAt(id);
            if (sp == IntPtr.Zero) { Log.Write($"[{owner}] иконка артефакта {id}: нет спрайта в {Field}"); return false; }
            if (ById.TryGetValue(id, out var old)) Remove(old, false);
            var o = new Over { Id = id, Png = png, Owner = owner, Orig = sp, Own = sp };
            if (Shared(id, sp))
            {
                try { o.Own = Clone(sp); PutSprite(id, o.Own); }
                catch (Exception e) { Log.Write($"[{owner}] иконка артефакта {id}: спрайт общий с другим артефактом, свой экземпляр не создан ({e.Message}); работает только окно описания в бою"); o.Own = IntPtr.Zero; }
            }
            ById[id] = o;
            if (o.Own != IntPtr.Zero) BySprite[o.Own] = o;
            Install();
            _nextScan = 0;
            return true;
        }

        public static void Clear(int id)
        {
            if (ById.TryGetValue(id, out var o)) Remove(o, true);
        }

        private static void Remove(Over o, bool refresh)
        {
            ById.Remove(o.Id);
            if (o.Own != IntPtr.Zero) BySprite.Remove(o.Own);
            if (o.Own != o.Orig && o.Own != IntPtr.Zero) PutSprite(o.Id, o.Orig);
            if (refresh) { _nextScan = 0; Scan(); }
        }

        public static void ClearOwner(string owner)
        {
            foreach (var o in ById.Values.Where(x => x.Owner == owner).ToList()) Remove(o, false);
            Scan();
        }

        // ---------------------------------------------------------------- хук и обход

        private static void Install()
        {
            if (_hooked) return;
            _hooked = true;
            try
            {
                Hooks.HookAfter("modkit.art", Hooks.FindMethod("V_Artifact_Manager", "Show_ArtifactDetail", 7), (self, a) =>
                {
                    int id = Convert.ToInt32(a[1]);
                    if (a[5] is Image img && ById.TryGetValue(id, out var o)) { Apply(img, o.Png); }
                });
            }
            catch (Exception e) { Log.Write("иконки артефактов: хук описания в бою не поставлен: " + e.Message); }
            Scheduler.Every(150, () => { try { Scan(); } catch (Exception e) { Log.Write("иконки артефактов: " + e.Message); } });
        }

        private static void Apply(Image img, byte[] png)
        {
            UiKit.SetImageOverlay(img, png);
            Tracked[img.Pointer] = UiKit.SpriteOfImage(img.Pointer);
        }

        private static double Now() => System.Diagnostics.Stopwatch.GetTimestamp() * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        private static double _interval = 0;

        private static void Scan()
        {
            double t0 = Now();
            if (t0 < _nextScan) return;
            if (BySprite.Count == 0 && Tracked.Count == 0) return;

            // убрать оверлей там, где Image уже показывает другой спрайт (игра переиспользует Image под другой артефакт)
            foreach (var kv in Tracked.ToList())
            {
                var p = kv.Key;
                if (Raw.IsDestroyed(p)) { Tracked.Remove(p); continue; }
                if (UiKit.SpriteOfImage(p) != kv.Value) { UiKit.SetImageOverlay(new Image(p), null); Tracked.Remove(p); }
            }
            if (BySprite.Count > 0)
            {
                var all = UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<Image>());
                foreach (var obj in all)
                {
                    var p = obj.Pointer;
                    if (!BySprite.TryGetValue(UiKit.SpriteOfImage(p), out var o)) continue;
                    var img = new Image(p);
                    if (Tracked.ContainsKey(p) && img.color.a < 0.01f) continue;      // уже наложено
                    Apply(img, o.Png);
                }
            }
            // слишком дорогой обход (много Image) — реже
            double ms = Now() - t0;
            _interval = ms > 8 ? 600 : ms > 3 ? 300 : 0;
            _nextScan = Now() + _interval;
        }
    }

    internal sealed class ArtApi : IArt
    {
        private readonly string _owner;
        private readonly HashSet<int> _mine = new HashSet<int>();
        public ArtApi(string owner, List<Action> cleanups) { _owner = owner; cleanups.Add(() => { _mine.Clear(); ArtIcons.ClearOwner(owner); }); }
        public bool SetIcon(int artifactId, byte[] png) { bool ok = ArtIcons.Set(_owner, artifactId, png); if (ok) _mine.Add(artifactId); return ok; }
        public void ClearIcon(int artifactId) { _mine.Remove(artifactId); ArtIcons.Clear(artifactId); }
    }
}
