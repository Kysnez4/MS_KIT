using System;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;

namespace ModKit
{
    /// <summary>
    /// Прямые вызовы IL2CPP без обёрток Il2CppInterop. Нужны там, где статический конструктор обёртки роняет Mono
    /// (UnityEngine.Texture / Texture2D): объекты создаются и настраиваются по указателям.
    /// </summary>
    public static unsafe class Raw
    {
        public static IntPtr Class(string assemblyDll, string ns, string name)
        {
            var k = IL2CPP.GetIl2CppClass(assemblyDll, ns, name);
            if (k == IntPtr.Zero) throw new Exception($"класс не найден: {ns}.{name}");
            return k;
        }

        public static IntPtr Method(IntPtr klass, string name, int argc)
        {
            var m = IL2CPP.il2cpp_class_get_method_from_name(klass, name, argc);
            if (m == IntPtr.Zero) throw new Exception($"метод не найден: {name}({argc})");
            return m;
        }

        /// <summary>Вызов метода. Аргументы: для значимых типов — указатель на значение, для ссылочных — сам указатель объекта.</summary>
        public static IntPtr Invoke(IntPtr method, IntPtr obj, params IntPtr[] args)
        {
            var p = stackalloc IntPtr[Math.Max(1, args.Length)];
            for (int i = 0; i < args.Length; i++) p[i] = args[i];
            IntPtr exc = IntPtr.Zero;
            var res = IL2CPP.il2cpp_runtime_invoke(method, obj, (void**)p, ref exc);
            if (exc != IntPtr.Zero) throw new Exception("исключение IL2CPP в " + Marshal.PtrToStringAnsi(IL2CPP.il2cpp_method_get_name(method)));
            return res;
        }

        /// <summary>Значение боксированного int/bool/float из результата Invoke.</summary>
        public static int UnboxInt(IntPtr boxed) => *(int*)IL2CPP.il2cpp_object_unbox(boxed);
        public static bool UnboxBool(IntPtr boxed) => *(byte*)IL2CPP.il2cpp_object_unbox(boxed) != 0;

        /// <summary>Вызов метода с одним int-аргументом.</summary>
        public static IntPtr InvokeInt(IntPtr method, IntPtr obj, int a)
        {
            return Invoke(method, obj, new IntPtr(&a));
        }

        /// <summary>Не даёт сборщику мусора IL2CPP удалить объект (возвращает дескриптор; освобождать не нужно).</summary>
        public static void Pin(IntPtr obj) => IL2CPP.il2cpp_gchandle_new(obj, false);

        /// <summary>UnityEngine.Object уничтожен (m_CachedPtr обнулён игрой).</summary>
        public static bool IsDestroyed(IntPtr unityObject) => unityObject == IntPtr.Zero || Marshal.ReadIntPtr(unityObject + 0x10) == IntPtr.Zero;
    }
}
