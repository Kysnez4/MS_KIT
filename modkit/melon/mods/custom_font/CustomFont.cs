using System;
using System.Collections.Generic;
using System.IO;
using Il2CppInterop.Runtime;
using Il2Cpp;
using ModKit;
using UnityEngine;

namespace CustomFont
{
    /// <summary>
    /// Замена всех шрифтов игры на font.ttf из архива. pack_mod.py превращает его в font.bundle (загрузка TTF в игре вырезана),
    /// LoadFont грузит бандл и его сцену (как UiKit.LoadSpriteBundle) и берёт Font у объекта mk_font. Подменяются Z_Database.Font_Apply_* / PFont_*
    /// (по ним игра выбирает шрифт языка) и шрифт у всех UI.Text — новые тексты подхватываются периодическим проходом.
    /// </summary>
    public class CustomFontMod : IKitMod
    {
        public ModDef Define() => new ModDef
        {
            Id = "custom_font",
            Name = "Шрифт Tagesschrift",
            Description = "Заменяет все шрифты игры на Tagesschrift Cyrillic.",
            Version = "1.0",
            OnEnable = Enable,
        };

        private static IntPtr _font;                       // бандл грузится один раз за запуск: при повторном включении берём готовый шрифт
        private static IntPtr _getFont, _setFont, _textType, _findObjects;

        private static unsafe void Enable(ModContext ctx)
        {
            var bundle = ctx.ReadAsset("font.bundle");
            if (bundle == null) { ctx.Warn("в архиве нет font.bundle"); return; }
            bool alive = true;
            ctx.OnCleanup(() => alive = false);
            LoadFont(ctx, bundle, font =>
            {
                if (!alive) return;
                if (font == IntPtr.Zero) { ctx.Warn("шрифт не загрузился, см. журнал"); return; }
                var mine = new Font(font);

                // поля, из которых игра берёт шрифт для новых текстов
                var a = (Z_Database.Font_Apply_Regular, Z_Database.Font_Apply_Medium, Z_Database.Font_Apply_Bold);
                Z_Database.Font_Apply_Regular = Z_Database.Font_Apply_Medium = Z_Database.Font_Apply_Bold = mine;
                ctx.OnCleanup(() => (Z_Database.Font_Apply_Regular, Z_Database.Font_Apply_Medium, Z_Database.Font_Apply_Bold) = a);
                SwapDatabase(ctx, mine);

                // уже созданные тексты: запоминаем исходный шрифт, при выключении возвращаем
                var orig = new Dictionary<IntPtr, IntPtr>();
                int total = 0;
                void Pass()
                {
                    byte incl = 1;
                    var arr = Raw.Invoke(_findObjects, IntPtr.Zero, _textType, new IntPtr(&incl));
                    long n = *(long*)(arr + 0x18);
                    for (long i = 0; i < n; i++)
                    {
                        var t = *(IntPtr*)(arr + 0x20 + (int)(i * IntPtr.Size));
                        var f = Raw.Invoke(_getFont, t);
                        if (f == font) continue;
                        if (!orig.ContainsKey(t)) { orig[t] = f; IL2CPP.il2cpp_gchandle_new(t, true); }
                        Raw.Invoke(_setFont, t, font);
                        total++;
                    }
                }
                Pass();
                ctx.Log($"шрифт подменён, текстов: {total}");
                ctx.Every(500, Pass);
                ctx.OnCleanup(() =>
                {
                    int back = 0;
                    foreach (var kv in orig)
                        if (!Raw.IsDestroyed(kv.Key)) { Raw.Invoke(_setFont, kv.Key, kv.Value); back++; }
                    ctx.Log($"шрифт возвращён, текстов: {back}");
                });
            });
        }

        /// <summary>
        /// Бандл шрифта → Font. AssetBundle.LoadAsset в игре вырезан, поэтому бандл догружает свою сцену (Additive),
        /// а шрифт берётся у выключенного UI.Text с именем mk_font. Всё через указатели Raw.
        /// </summary>
        private static unsafe void LoadFont(ModContext ctx, byte[] bundle, Action<IntPtr> onDone)
        {
            if (_font != IntPtr.Zero) { onDone(_font); return; }
            var ab = Raw.Class("UnityEngine.AssetBundleModule.dll", "UnityEngine", "AssetBundle");
            var opDone = Raw.Method(Raw.Class("UnityEngine.CoreModule.dll", "UnityEngine", "AsyncOperation"), "get_isDone", 0);
            var reqBundle = Raw.Method(Raw.Class("UnityEngine.AssetBundleModule.dll", "UnityEngine", "AssetBundleCreateRequest"), "get_assetBundle", 0);
            var loadScene = Raw.Method(Raw.Class("UnityEngine.CoreModule.dll", "UnityEngine.SceneManagement", "SceneManager"), "LoadSceneAsync", 2);
            var obj = Raw.Class("UnityEngine.CoreModule.dll", "UnityEngine", "Object");
            var objName = Raw.Method(obj, "get_name", 0);
            var objHide = Raw.Method(obj, "set_hideFlags", 1);
            var text = Raw.Class("UnityEngine.UI.dll", "UnityEngine.UI", "Text");
            _getFont = Raw.Method(text, "get_font", 0);
            _setFont = Raw.Method(text, "set_font", 1);
            _textType = IL2CPP.il2cpp_type_get_object(IL2CPP.il2cpp_class_get_type(text));
            _findObjects = Raw.Method(obj, "FindObjectsOfType", 2);

            var path = Path.Combine(Application.persistentDataPath, "custom_font.bundle");
            if (!File.Exists(path) || !File.ReadAllBytes(path).AsSpan().SequenceEqual(bundle)) File.WriteAllBytes(path, bundle);
            uint crc = 0; ulong offset = 0;
            var req = Raw.Invoke(Raw.Method(ab, "LoadFromFileAsync", 3), IntPtr.Zero, IL2CPP.ManagedStringToIl2Cpp(path), new IntPtr(&crc), new IntPtr(&offset));
            Raw.Pin(req);
            IntPtr sceneOp = IntPtr.Zero;
            bool done = false;
            void Fail(string m) { done = true; ctx.Log("бандл шрифта: " + m); onDone(IntPtr.Zero); }
            ctx.Every(50, () =>
            {
                if (done) return;
                try
                {
                    if (sceneOp == IntPtr.Zero)
                    {
                        if (!Raw.UnboxBool(Raw.Invoke(opDone, req))) return;
                        var b = Raw.Invoke(reqBundle, req);
                        if (b == IntPtr.Zero) { Fail("AssetBundle не загрузился (бандл уже загружен или версия Unity?)"); return; }
                        Raw.Pin(b);
                        var scenes = Raw.Invoke(Raw.Method(ab, "GetAllScenePaths", 0), b);
                        if (scenes == IntPtr.Zero || *(long*)(scenes + 0x18) == 0) { Fail("в бандле нет сцены"); return; }
                        int* prm = stackalloc int[2] { 1, 0 };          // LoadSceneParameters: Additive, без своей физики
                        sceneOp = Raw.Invoke(loadScene, IntPtr.Zero, *(IntPtr*)(scenes + 0x20), new IntPtr(prm));
                        if (sceneOp == IntPtr.Zero) { Fail("сцена не загрузилась"); return; }
                        Raw.Pin(sceneOp);
                        return;
                    }
                    if (!Raw.UnboxBool(Raw.Invoke(opDone, sceneOp))) return;
                    byte incl = 1;
                    var arr = Raw.Invoke(_findObjects, IntPtr.Zero, _textType, new IntPtr(&incl));
                    for (long i = 0, n = *(long*)(arr + 0x18); i < n; i++)
                    {
                        var t = *(IntPtr*)(arr + 0x20 + (int)(i * IntPtr.Size));
                        if (IL2CPP.Il2CppStringToManaged(Raw.Invoke(objName, t)) != "mk_font") continue;
                        var f = Raw.Invoke(_getFont, t);
                        if (f == IntPtr.Zero) continue;
                        Raw.InvokeInt(objHide, f, 32);               // DontUnloadUnusedAsset: шрифт переживёт выгрузку сцены
                        Raw.Pin(f);
                        done = true; _font = f; onDone(f); return;
                    }
                    Fail("в сцене нет mk_font со шрифтом");
                }
                catch (Exception e) { Fail(e.Message); }
            });
        }

        /// <summary>Исходные шрифты языков на объекте Z_Database (из них игра заново заполняет Font_Apply_* при смене языка).</summary>
        private static void SwapDatabase(ModContext ctx, Font mine)
        {
            var db = UnityEngine.Object.FindObjectOfType<Z_Database>();
            if (db == null) { ctx.Log("Z_Database не найден, PFont_* не подменены"); return; }
            var old = new[] { db.PFont_JejuHallasan, db.PFont_NotoSansEng_Bold, db.PFont_NotoSansEng_Medium, db.PFont_NotoSansJP_Medium,
                db.PFont_NotoSansJP_Regular, db.PFont_NotoSansSC_Medium, db.PFont_NotoSansSC_Regular };
            db.PFont_JejuHallasan = db.PFont_NotoSansEng_Bold = db.PFont_NotoSansEng_Medium = db.PFont_NotoSansJP_Medium =
                db.PFont_NotoSansJP_Regular = db.PFont_NotoSansSC_Medium = db.PFont_NotoSansSC_Regular = mine;
            ctx.OnCleanup(() =>
            {
                if (db.WasCollected || Raw.IsDestroyed(db.Pointer)) return;
                db.PFont_JejuHallasan = old[0]; db.PFont_NotoSansEng_Bold = old[1]; db.PFont_NotoSansEng_Medium = old[2];
                db.PFont_NotoSansJP_Medium = old[3]; db.PFont_NotoSansJP_Regular = old[4]; db.PFont_NotoSansSC_Medium = old[5]; db.PFont_NotoSansSC_Regular = old[6];
            });
        }
    }
}
