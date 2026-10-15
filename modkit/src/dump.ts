import "frida-il2cpp-bridge";
import { DIR } from "./log";

const OUT = DIR + "/dump.cs";

Il2Cpp.perform(() => {
    const f = new File(OUT, "w");
    let classes = 0;
    for (const asm of Il2Cpp.domain.assemblies) {
        f.write(`// ==== ${asm.name}\n`);
        for (const k of asm.image.classes) {
            classes++;
            f.write(`class ${k.namespace ? k.namespace + "." : ""}${k.name}${k.parent ? " : " + k.parent.name : ""}\n{\n`);
            for (const fl of k.fields) {
                f.write(`  ${fl.isStatic ? "static " : ""}${fl.type.name} ${fl.name}; // 0x${fl.offset.toString(16)}\n`);
            }
            for (const m of k.methods) {
                const ps = m.parameters.map(p => `${p.type.name} ${p.name}`).join(", ");
                f.write(`  ${m.isStatic ? "static " : ""}${m.returnType.name} ${m.name}(${ps}); // RVA 0x${m.relativeVirtualAddress.toString(16)}\n`);
            }
            f.write("}\n");
        }
        f.flush();
    }
    f.close();
    const chmod = new NativeFunction(Process.getModuleByName("libc.so").getExportByName("chmod"), "int", ["pointer", "int"]);
    chmod(Memory.allocUtf8String(OUT), 0o644);
    console.log("dump done, classes: " + classes);
});
