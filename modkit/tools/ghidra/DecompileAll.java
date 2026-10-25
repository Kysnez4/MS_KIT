// Пакетная декомпиляция функций игры (Assembly-CSharp) в modkit/decomp/<Класс>/<Метод>_<RVA>.c.
// Список функций: functions.tsv (RVA<TAB>класс<TAB>метод), его строит decompile_all.sh из dump.cs.
// Аргументы: <functions.tsv> <выходная папка> <номер части> <всего частей> <таймаут, с>.
// Уже существующие файлы пропускаются; функции, на которых Ghidra не справилась, пишутся в failed_<часть>.tsv.
// Адрес в Ghidra = RVA + 0x100000.
//@category ModKit
import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Function;

import java.io.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.util.*;

public class DecompileAll extends GhidraScript {
    private static String safe(String s) {
        return s.replaceAll("[<>:\"/\\\\|?*`,\\[\\] ]", "_");
    }

    @Override
    protected void run() throws Exception {
        String[] a = getScriptArgs();
        Path list = Paths.get(a[0]), out = Paths.get(a[1]);
        int part = Integer.parseInt(a[2]), parts = Integer.parseInt(a[3]), timeout = Integer.parseInt(a[4]);

        DecompInterface dec = new DecompInterface();
        dec.openProgram(currentProgram);
        PrintWriter failed = new PrintWriter(new OutputStreamWriter(
            new FileOutputStream(out.resolve("failed_" + part + ".tsv").toFile(), true), StandardCharsets.UTF_8), true);

        List<String> lines = Files.readAllLines(list, StandardCharsets.UTF_8);
        int done = 0, skipped = 0, bad = 0, i = -1;
        long t0 = System.currentTimeMillis();
        for (String line : lines) {
            if (line.isBlank() || ++i % parts != part) continue;
            if (monitor.isCancelled()) break;
            String[] f = line.split("\t");
            long rva = Long.decode(f[0]);
            Path dir = out.resolve(safe(f[1]));
            Path file = dir.resolve(safe(f[2]) + "_" + Long.toHexString(rva) + ".c");
            if (Files.exists(file)) { skipped++; continue; }
            Address addr = toAddr(rva + 0x100000);
            Function fn = getFunctionAt(addr);
            if (fn == null) fn = createFunction(addr, null);
            if (fn == null) { failed.println(line + "\tнет функции"); bad++; continue; }
            DecompileResults r = dec.decompileFunction(fn, timeout, monitor);
            if (r == null || !r.decompileCompleted() || r.getDecompiledFunction() == null) {
                failed.println(line + "\t" + (r == null ? "null" : r.getErrorMessage().replace('\n', ' ')));
                bad++;
                continue;
            }
            Files.createDirectories(dir);
            String head = "// " + f[1] + "$$" + f[2] + "  RVA 0x" + Long.toHexString(rva) + "\n";
            Files.write(file, (head + r.getDecompiledFunction().getC()).getBytes(StandardCharsets.UTF_8));
            done++;
            if (done % 50 == 0)
                println(String.format("часть %d: готово %d, пропущено %d, ошибок %d, %d с", part, done, skipped, bad,
                    (System.currentTimeMillis() - t0) / 1000));
        }
        println(String.format("ИТОГ часть %d: готово %d, пропущено %d, ошибок %d, %d с", part, done, skipped, bad,
            (System.currentTimeMillis() - t0) / 1000));
        failed.close();
        dec.dispose();
    }
}
