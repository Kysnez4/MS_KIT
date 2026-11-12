// Правит Il2CppInterop.Runtime.dll внутри melon_data.zip (LemonLoader): InjectorHelpers..cctor ищет модуль libil2cpp.so через
// Enumerable.Single, но на Android библиотека отображается несколькими кусками, и .NET считает каждый отдельным модулем ->
// «Sequence contains more than one matching element». Заменяем Single на First. Заодно правит MonoMod.Utils (см. ниже).
//   dotnet run -- <melon_data.zip> <melon_data_patched.zip>
using System.IO.Compression;
using Mono.Cecil;
using Mono.Cecil.Cil;

if (args.Length < 2) { Console.WriteLine("использование: IlPatch <вход.zip> <выход.zip>"); return 1; }
File.Copy(args[0], args[1], true);
using var zip = ZipFile.Open(args[1], ZipArchiveMode.Update);
int patched = 0;
foreach (var entry in zip.Entries.Where(e => e.Name == "Il2CppInterop.Runtime.dll").ToList())
{
    byte[] data;
    using (var s = entry.Open()) { var ms = new MemoryStream(); s.CopyTo(ms); data = ms.ToArray(); }
    var asm = AssemblyDefinition.ReadAssembly(new MemoryStream(data));
    var cctor = asm.MainModule.GetType("Il2CppInterop.Runtime.Injection.InjectorHelpers").Methods.First(m => m.IsConstructor && m.IsStatic);
    bool changed = false;
    foreach (var ins in cctor.Body.Instructions)
    {
        if (ins.OpCode != OpCodes.Call || ins.Operand is not GenericInstanceMethod gm || gm.ElementMethod.Name != "Single" || gm.ElementMethod.Parameters.Count != 2) continue;
        var single = gm.ElementMethod;
        var first = new MethodReference("First", single.ReturnType, single.DeclaringType) { HasThis = false };
        var gp = new GenericParameter("TSource", first);
        first.GenericParameters.Add(gp);
        first.ReturnType = gp;
        var enumerable = new GenericInstanceType(((GenericInstanceType)single.Parameters[0].ParameterType).ElementType);
        enumerable.GenericArguments.Add(gp);
        var func = new GenericInstanceType(((GenericInstanceType)single.Parameters[1].ParameterType).ElementType);
        func.GenericArguments.Add(gp);
        func.GenericArguments.Add(asm.MainModule.TypeSystem.Boolean);
        first.Parameters.Add(new ParameterDefinition(enumerable));
        first.Parameters.Add(new ParameterDefinition(func));
        var inst = new GenericInstanceMethod(first);
        foreach (var a in gm.GenericArguments) inst.GenericArguments.Add(a);
        ins.Operand = inst;
        changed = true;
    }
    if (!changed) { Console.WriteLine($"{entry.FullName}: Single не найден"); continue; }
    var outMs = new MemoryStream();
    asm.Write(outMs);
    var name = entry.FullName;
    entry.Delete();
    var ne = zip.CreateEntry(name, CompressionLevel.Optimal);
    using (var w = ne.Open()) w.Write(outMs.ToArray());
    patched++;
    Console.WriteLine($"{name}: исправлено");
}

// MonoMod.Utils: на x86_64-эмуляторе с трансляцией ARM uname/AT_PLATFORM дают x86_64, и MonoMod пишет x86-детуры в ARM64-код
// (Error 255). Подменяем литералы в PlatformDetection.DetectInfoPosix: X86_64 -> ветка Arm64. На телефоне (aarch64) ничего не меняется.
var map = new Dictionary<string, string> { ["X86_64"] = "", ["AMD64"] = "", ["X86"] = "", ["ARM64"] = "X86_64" };
int monomod = 0;
foreach (var entry in zip.Entries.Where(e => e.Name == "MonoMod.Utils.dll" && (e.FullName.Contains("/net8/") || e.FullName.Contains("/SupportModules/"))).ToList())
{
    byte[] data;
    using (var s = entry.Open()) { var ms = new MemoryStream(); s.CopyTo(ms); data = ms.ToArray(); }
    var asm = AssemblyDefinition.ReadAssembly(new MemoryStream(data));
    var m = asm.MainModule.GetType("MonoMod.Utils.PlatformDetection").Methods.First(x => x.Name == "DetectInfoPosix");
    int n = 0;
    foreach (var i in m.Body.Instructions)
        if (i.OpCode == OpCodes.Ldstr && map.TryGetValue((string)i.Operand, out var v)) { i.Operand = v; n++; }
    if (n != 4) throw new Exception($"{entry.FullName}: подменено {n} литералов вместо 4");
    var outMs = new MemoryStream();
    asm.Write(outMs);
    var name = entry.FullName;
    entry.Delete();
    var ne = zip.CreateEntry(name, CompressionLevel.Optimal);
    using (var w = ne.Open()) w.Write(outMs.ToArray());
    monomod++;
    Console.WriteLine($"{name}: исправлено");
}
Console.WriteLine($"исправлено файлов: {patched}, MonoMod: {monomod}");
return patched > 0 && monomod > 0 ? 0 : 2;
