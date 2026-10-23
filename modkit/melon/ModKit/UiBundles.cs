using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;

namespace ModKit
{
    /// <summary>
    /// Спрайты мода из AssetBundle со сценой. Sprite.Create и AssetBundle.LoadAsset в движке игры вырезаны, но загрузка бандла
    /// и сцены осталась: бандл догружается сценой поверх текущей, спрайты берутся у её (выключенных) SpriteRenderer.
    /// Имя объекта в сцене = имя спрайта. Всё через указатели (обёртки Sprite/Texture роняют Mono).
    /// </summary>
    public static unsafe partial class UiKit
    {
        private static readonly Dictionary<string, Dictionary<string, IntPtr>> Bundles = new Dictionary<string, Dictionary<string, IntPtr>>();
        private static readonly Dictionary<string, List<Action<Dictionary<string, IntPtr>>>> BundleWaiters = new Dictionary<string, List<Action<Dictionary<string, IntPtr>>>>();
        private static IntPtr _abLoad, _abScenes, _abReqBundle, _opDone, _loadScene, _findObjects, _objName, _objHide, _srSprite, _srType;

        private static void InitBundles()
        {
            if (_abLoad != IntPtr.Zero) return;
            var ab = Raw.Class("UnityEngine.AssetBundleModule.dll", "UnityEngine", "AssetBundle");
            _abLoad = Raw.Method(ab, "LoadFromFileAsync", 3);
            _abScenes = Raw.Method(ab, "GetAllScenePaths", 0);
            _abReqBundle = Raw.Method(Raw.Class("UnityEngine.AssetBundleModule.dll", "UnityEngine", "AssetBundleCreateRequest"), "get_assetBundle", 0);
            _opDone = Raw.Method(Raw.Class("UnityEngine.CoreModule.dll", "UnityEngine", "AsyncOperation"), "get_isDone", 0);
            _loadScene = Raw.Method(Raw.Class("UnityEngine.CoreModule.dll", "UnityEngine.SceneManagement", "SceneManager"), "LoadSceneAsync", 2);
            var obj = Raw.Class("UnityEngine.CoreModule.dll", "UnityEngine", "Object");
            _findObjects = Raw.Method(obj, "FindObjectsOfType", 2);
            _objName = Raw.Method(obj, "get_name", 0);
            _objHide = Raw.Method(obj, "set_hideFlags", 1);
            var sr = Raw.Class("UnityEngine.CoreModule.dll", "UnityEngine", "SpriteRenderer");
            _srSprite = Raw.Method(sr, "get_sprite", 0);
            _srType = IL2CPP.il2cpp_type_get_object(IL2CPP.il2cpp_class_get_type(sr));
        }

        /// <summary>
        /// Загружает бандл спрайтов (файл .bundle из zip мода, собран pack_mod.py) и вызывает onDone со словарём «имя → спрайт» (указатели;
        /// вставлять через SetSprite, SpriteRenderer.set_sprite, массивы SpriteList_*). key — уникальное имя бандла (например id мода):
        /// бандл грузится один раз за запуск, повторные вызовы получают тот же словарь. При ошибке словарь пустой, причина — в журнале.
        /// </summary>
        public static void LoadSpriteBundle(string key, byte[] bundle, Action<Dictionary<string, IntPtr>> onDone)
        {
            if (Bundles.TryGetValue(key, out var ready)) { onDone(ready); return; }
            if (BundleWaiters.TryGetValue(key, out var waiting)) { waiting.Add(onDone); return; }
            BundleWaiters[key] = new List<Action<Dictionary<string, IntPtr>>> { onDone };
            var result = new Dictionary<string, IntPtr>();
            void Finish(string error)
            {
                if (error != null) Log.Write($"бандл спрайтов {key}: {error}");
                else Log.Write($"бандл спрайтов {key}: {result.Count} спрайтов");
                Bundles[key] = result;
                var list = BundleWaiters[key]; BundleWaiters.Remove(key);
                foreach (var fn in list) { try { fn(result); } catch (Exception e) { Log.Write("бандл спрайтов, обработчик: " + e); } }
            }

            IntPtr req;
            try
            {
                InitBundles();
                var dir = Path.Combine(Paths.Dir, "cache");
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, key + ".bundle");
                if (!File.Exists(path) || new FileInfo(path).Length != bundle.Length || !File.ReadAllBytes(path).AsSpan().SequenceEqual(bundle))
                    File.WriteAllBytes(path, bundle);
                uint crc = 0; ulong offset = 0;
                req = Raw.Invoke(_abLoad, IntPtr.Zero, IL2CPP.ManagedStringToIl2Cpp(path), new IntPtr(&crc), new IntPtr(&offset));
                Raw.Pin(req);
            }
            catch (Exception e) { Finish(e.Message); return; }

            IntPtr sceneOp = IntPtr.Zero;
            Action stop = null;
            stop = Scheduler.Every(50, () =>
            {
                try
                {
                    if (sceneOp == IntPtr.Zero)
                    {
                        if (!Raw.UnboxBool(Raw.Invoke(_opDone, req))) return;
                        var ab = Raw.Invoke(_abReqBundle, req);
                        if (ab == IntPtr.Zero) { stop(); Finish("AssetBundle не загрузился (версия Unity/платформа бандла?)"); return; }
                        Raw.Pin(ab);
                        var scenes = Raw.Invoke(_abScenes, ab);
                        if (scenes == IntPtr.Zero || ArrayLength(scenes) == 0) { stop(); Finish("в бандле нет сцены"); return; }
                        var scene = ArrayItem(scenes, 0);
                        int* prm = stackalloc int[2] { 1, 0 };          // LoadSceneParameters: Additive, без своей физики
                        sceneOp = Raw.Invoke(_loadScene, IntPtr.Zero, scene, new IntPtr(prm));
                        if (sceneOp == IntPtr.Zero) { stop(); Finish("сцена не загрузилась: " + IL2CPP.Il2CppStringToManaged(scene)); return; }
                        Raw.Pin(sceneOp);
                        return;
                    }
                    if (!Raw.UnboxBool(Raw.Invoke(_opDone, sceneOp))) return;
                    stop();
                    byte incl = 1;
                    var arr = Raw.Invoke(_findObjects, IntPtr.Zero, _srType, new IntPtr(&incl));
                    for (long i = 0, n = ArrayLength(arr); i < n; i++)
                    {
                        var r = ArrayItem(arr, i);
                        var name = IL2CPP.Il2CppStringToManaged(Raw.Invoke(_objName, r));
                        if (name == null || !name.StartsWith(BundlePrefix)) continue;
                        var sp = Raw.Invoke(_srSprite, r);
                        if (sp == IntPtr.Zero) continue;
                        Raw.InvokeInt(_objHide, sp, 32);             // DontUnloadUnusedAsset: сцена выгрузится вместе с игровой, спрайт останется
                        Raw.Pin(sp);
                        result[name.Substring(BundlePrefix.Length)] = sp;
                    }
                    Finish(null);
                }
                catch (Exception e) { stop(); Finish(e.Message); }
            });
        }

        /// <summary>Префикс имён объектов-носителей спрайтов в сцене бандла (его ставит pack_mod.py).</summary>
        public const string BundlePrefix = "mk_";

        private static long ArrayLength(IntPtr arr) => Marshal.ReadInt64(arr + 0x18);
        private static IntPtr ArrayItem(IntPtr arr, long i) => Marshal.ReadIntPtr(arr + 0x20 + (int)(i * IntPtr.Size));
    }
}
