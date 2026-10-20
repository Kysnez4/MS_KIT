using System;
using System.Collections.Generic;
using System.IO;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.UI;

namespace ModKit
{
    /// <summary>
    /// Текстуры без обёрток Texture/Texture2D (их статический конструктор роняет Mono): всё через <see cref="Raw"/>.
    /// Картинки мода (PNG/JPG) и скруглённые кнопки.
    /// </summary>
    public static partial class UiKit
    {
        private class Tex { public IntPtr Ptr; public int W, H; }

        private static readonly Dictionary<byte[], Tex> ByBytes = new Dictionary<byte[], Tex>();
        private static readonly Dictionary<string, Tex> ByKey = new Dictionary<string, Tex>();
        private static IntPtr _tex2D, _ctor, _load, _setHide, _getW, _getH, _setTexture, _imageConversion;

        private static void InitTextures()
        {
            if (_tex2D != IntPtr.Zero) return;
            _tex2D = Raw.Class("UnityEngine.CoreModule.dll", "UnityEngine", "Texture2D");
            _ctor = Raw.Method(_tex2D, ".ctor", 2);
            _setHide = Raw.Method(_tex2D, "set_hideFlags", 1);
            _getW = Raw.Method(_tex2D, "get_width", 0);
            _getH = Raw.Method(_tex2D, "get_height", 0);
            _imageConversion = Raw.Class("UnityEngine.ImageConversionModule.dll", "UnityEngine", "ImageConversion");
            _load = Raw.Method(_imageConversion, "LoadImage", 2);
            _setTexture = Raw.Method(Raw.Class("UnityEngine.UI.dll", "UnityEngine.UI", "RawImage"), "set_texture", 1);
        }

        private static unsafe Tex CreateTexture(byte[] image)
        {
            InitTextures();
            var tex = Il2CppInterop.Runtime.IL2CPP.il2cpp_object_new(_tex2D);
            int two = 2;
            Raw.Invoke(_ctor, tex, new IntPtr(&two), new IntPtr(&two));
            var arr = new Il2CppStructArray<byte>(image);
            var ok = Raw.Invoke(_load, IntPtr.Zero, tex, arr.Pointer);
            if (!Raw.UnboxBool(ok)) return null;
            Raw.InvokeInt(_setHide, tex, 61);                    // HideAndDontSave: не выгружается при смене сцены
            Raw.Pin(tex);                                        // и не собирается сборщиком мусора
            return new Tex { Ptr = tex, W = Raw.UnboxInt(Raw.Invoke(_getW, tex)), H = Raw.UnboxInt(Raw.Invoke(_getH, tex)) };
        }

        /// <summary>Текстура из PNG/JPG (кэшируется, пересоздаётся, если игра её выгрузила). null — не удалось.</summary>
        private static Tex TextureOf(byte[] bytes)
        {
            if (ByBytes.TryGetValue(bytes, out var old))
            {
                if (!Raw.IsDestroyed(old.Ptr)) return old;
                ByBytes.Remove(bytes);
            }
            try
            {
                var t = CreateTexture(bytes);
                if (t != null) ByBytes[bytes] = t;
                return t;
            }
            catch (Exception e) { Log.Write("картинка мода: " + e.Message); return null; }
        }

        private static Tex RoundedTexture(int w, int h, int r)
        {
            var key = $"{w}x{h}x{r}";
            if (ByKey.TryGetValue(key, out var old))
            {
                if (!Raw.IsDestroyed(old.Ptr)) return old;
                ByKey.Remove(key);
            }
            try
            {
                var t = CreateTexture(RoundedPng(w, h, r));
                if (t != null) ByKey[key] = t;
                return t;
            }
            catch (Exception e) { Log.Write("скруглённая кнопка: " + e.Message); return null; }
        }

        private static void SetTexture(RawImage raw, Tex t) => Raw.Invoke(_setTexture, raw.Pointer, t.Ptr);

        /// <summary>RawImage с картинкой в рамке w×h (вписывается с сохранением пропорций).</summary>
        public static void Picture(Transform parent, byte[] bytes, double x, double y, double w, double h)
        {
            var tex = TextureOf(bytes);
            if (tex == null || tex.W <= 0 || tex.H <= 0) return;
            double sc = Math.Min(w / tex.W, h / tex.H);
            double pw = tex.W * sc, ph = tex.H * sc;
            var n = Node("pic", parent);
            Place(n.rt, x + (w - pw) / 2, y + (h - ph) / 2, pw, ph);
            var raw = n.go.AddComponent<RawImage>();
            SetTexture(raw, tex);
            raw.raycastTarget = false;
        }

        private static readonly Dictionary<IntPtr, Color> OrigColors = new Dictionary<IntPtr, Color>();

        /// <summary>
        /// Кладёт поверх Image дочерний RawImage с картинкой (png = null — убирает) и прячет сам спрайт (alpha 0; Image остаётся — на него опираются скрипты игры).
        /// Нужен, когда спрайт игры нельзя заменить (он из общего сжатого атласа).
        /// </summary>
        public static void SetImageOverlay(Image img, byte[] png)
        {
            const string nodeName = "ModKitIcon";
            var tf = img.transform;
            RawImage raw = null;
            for (int i = 0; i < tf.childCount; i++)
            {
                var c = tf.GetChild(i);
                if (c.gameObject.name == nodeName) { raw = c.gameObject.GetComponent<RawImage>(); break; }
            }
            if (!OrigColors.ContainsKey(img.Pointer)) OrigColors[img.Pointer] = img.color;
            if (png == null)
            {
                if (raw != null) raw.gameObject.SetActive(false);
                if (OrigColors.TryGetValue(img.Pointer, out var orig)) img.color = orig;
                return;
            }
            var tex = TextureOf(png);
            if (tex == null) return;
            if (raw == null)
            {
                var n = Node(nodeName, tf);
                n.rt.anchorMin = V2(0, 0); n.rt.anchorMax = V2(1, 1); n.rt.pivot = V2(0.5, 0.5);
                n.rt.anchoredPosition = V2(0, 0); n.rt.sizeDelta = V2(0, 0);
                raw = n.go.AddComponent<RawImage>();
                raw.raycastTarget = false;
            }
            SetTexture(raw, tex);
            raw.gameObject.SetActive(true);
            img.color = new Color(0, 0, 0, 0);
        }

        /// <summary>Спрайт Image как указатель (без обёртки Sprite, она роняет Mono).</summary>
        public static IntPtr GetSprite(Image img)
        {
            InitImage();
            return Raw.Invoke(_getSprite, img.Pointer);
        }

        internal static IntPtr SpriteOfImage(IntPtr image)
        {
            InitImage();
            return Raw.Invoke(_getSprite, image);
        }

        public static void SetSprite(Image img, IntPtr sprite)
        {
            InitImage();
            Raw.Invoke(_setSprite, img.Pointer, sprite);
        }

        private static IntPtr _getSprite, _setSprite;
        private static void InitImage()
        {
            if (_getSprite != IntPtr.Zero) return;
            var k = Raw.Class("UnityEngine.UI.dll", "UnityEngine.UI", "Image");
            _getSprite = Raw.Method(k, "get_sprite", 0);
            _setSprite = Raw.Method(k, "set_sprite", 1);
        }

        // ---------------------------------------------------------------- PNG без сжатия (скруглённый белый прямоугольник)

        private static readonly uint[] CrcTable = BuildCrc();
        private static uint[] BuildCrc()
        {
            var t = new uint[256];
            for (uint n = 0; n < 256; n++) { uint c = n; for (int i = 0; i < 8; i++) c = (c & 1) != 0 ? 0xedb88320 ^ (c >> 1) : c >> 1; t[n] = c; }
            return t;
        }

        private static uint Crc32(byte[] b, int from, int to)
        {
            uint c = 0xffffffff;
            for (int i = from; i < to; i++) c = CrcTable[(c ^ b[i]) & 255] ^ (c >> 8);
            return c ^ 0xffffffff;
        }

        private static void BE(byte[] b, int at, uint v) { b[at] = (byte)(v >> 24); b[at + 1] = (byte)(v >> 16); b[at + 2] = (byte)(v >> 8); b[at + 3] = (byte)v; }

        /// <summary>PNG (RGBA) со скруглённым белым прямоугольником w×h и сглаживанием по краю; deflate «store».</summary>
        private static byte[] RoundedPng(int w, int h, int r)
        {
            int stride = w * 4 + 1;
            var raw = new byte[stride * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    double dx = Math.Max(Math.Max(r - x - 0.5, x + 0.5 - (w - r)), 0), dy = Math.Max(Math.Max(r - y - 0.5, y + 0.5 - (h - r)), 0);
                    double cover = Math.Max(0, Math.Min(1, r + 0.5 - Math.Sqrt(dx * dx + dy * dy)));
                    int o = y * stride + 1 + x * 4;
                    raw[o] = raw[o + 1] = raw[o + 2] = 255;
                    raw[o + 3] = (byte)Math.Round(cover * 255);
                }
            }
            uint a = 1, bsum = 0;
            foreach (var v in raw) { a = (a + v) % 65521; bsum = (bsum + a) % 65521; }
            int blocks = (raw.Length + 65534) / 65535;
            var z = new byte[2 + raw.Length + blocks * 5 + 4];
            int p = 0;
            z[p++] = 0x78; z[p++] = 0x01;
            for (int i = 0; i < blocks; i++)
            {
                int len = Math.Min(65535, raw.Length - i * 65535);
                z[p++] = (byte)(i == blocks - 1 ? 1 : 0);
                z[p++] = (byte)(len & 255); z[p++] = (byte)(len >> 8); z[p++] = (byte)(~len & 255); z[p++] = (byte)((~len >> 8) & 255);
                Buffer.BlockCopy(raw, i * 65535, z, p, len); p += len;
            }
            BE(z, p, (bsum << 16) | a); p += 4;

            using var ms = new MemoryStream();
            ms.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, 0, 8);
            void Chunk(string type, byte[] data, int len)
            {
                var buf = new byte[12 + len];
                BE(buf, 0, (uint)len);
                for (int i = 0; i < 4; i++) buf[4 + i] = (byte)type[i];
                Buffer.BlockCopy(data, 0, buf, 8, len);
                BE(buf, 8 + len, Crc32(buf, 4, 8 + len));
                ms.Write(buf, 0, buf.Length);
            }
            var ihdr = new byte[13];
            BE(ihdr, 0, (uint)w); BE(ihdr, 4, (uint)h); ihdr[8] = 8; ihdr[9] = 6;
            Chunk("IHDR", ihdr, 13); Chunk("IDAT", z, p); Chunk("IEND", new byte[0], 0);
            return ms.ToArray();
        }
    }
}
