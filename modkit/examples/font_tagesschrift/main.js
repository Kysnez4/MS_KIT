// Замена шрифта кириллицы. Создать Font из файла в этой сборке нельзя (обёртки вырезаны), поэтому байты шрифтов
// NotoSansDisplay-Medium/Bold подменяются на font.ttf из архива прямо при загрузке ассетов. Нужен перезапуск игры.
// Перехват ставится на верхнем уровне скрипта (при загрузке zip), а не в onEnable: шрифты грузятся почти сразу.
const status = { text: "не запускался" };
const keep = (globalThis.__fontTagesschriftKeep = globalThis.__fontTagesschriftKeep || []); // буферы и CModule должны жить, пока игра работает (иначе GC их освободит)
const say = (m) => { status.text = m; console.log("[font] " + m); };
(function install() {
        const NAME = "font.ttf";
        const libc = Process.getModuleByName("libc.so");
        let pkg = "com.vkslrzm.Zombie";
        for (const lib of ["libg.so", "libmain.so"]) {
            try {
                const m = /\/([A-Za-z][\w.]*)-[\w=-]{16,}\/lib\//.exec(Process.getModuleByName(lib).path);
                if (m) { pkg = m[1]; break; }
            } catch (_) { /* нет модуля */ }
        }
        const enabled = () => {
            try { return JSON.parse(File.readAllText(`/sdcard/Android/data/${pkg}/files/modkit/config.json`)).enabled?.ext_font_tagesschrift === true; }
            catch (_) { return false; }
        };
        const out = `/sdcard/Android/data/${pkg}/files/modkit/tagesschrift.ttf`;

        // --- достаём font.ttf из zip мода (метод «store») ---
        const extract = () => {
            const opendir = new NativeFunction(libc.getExportByName("opendir"), "pointer", ["pointer"]);
            const readdir = new NativeFunction(libc.getExportByName("readdir"), "pointer", ["pointer"]);
            const closedir = new NativeFunction(libc.getExportByName("closedir"), "int", ["pointer"]);
            for (const dirPath of [`/sdcard/Android/data/${pkg}/files/modkit/mods`, "/sdcard/Download/ModKit"]) {
                const d = opendir(Memory.allocUtf8String(dirPath));
                if (d.isNull()) continue;
                const zips = [];
                try {
                    for (;;) {
                        const ent = readdir(d);
                        if (ent.isNull()) break;
                        const n = ent.add(19).readUtf8String();
                        if (n && n.toLowerCase().endsWith(".zip") && !n.startsWith("_")) zips.push(n);
                    }
                } finally { closedir(d); }
                for (const z of zips) {
                    try {
                        const buf = File.readAllBytes(`${dirPath}/${z}`);
                        const dv = new DataView(buf);
                        let e = buf.byteLength - 22;
                        while (e >= 0 && dv.getUint32(e, true) !== 0x06054b50) e--;
                        if (e < 0) continue;
                        let off = dv.getUint32(e + 16, true), hit = null;
                        for (let i = dv.getUint16(e + 10, true); i > 0; i--) {
                            if (dv.getUint32(off, true) !== 0x02014b50) break;
                            const method = dv.getUint16(off + 10, true), size = dv.getUint32(off + 24, true);
                            const nlen = dv.getUint16(off + 28, true), elen = dv.getUint16(off + 30, true), clen = dv.getUint16(off + 32, true);
                            const lho = dv.getUint32(off + 42, true);
                            const name = String.fromCharCode(...new Uint8Array(buf, off + 46, nlen));
                            off += 46 + nlen + elen + clen;
                            if (name === NAME && method === 0) hit = { lho, size };
                        }
                        if (!hit) continue;
                        const start = hit.lho + 30 + dv.getUint16(hit.lho + 26, true) + dv.getUint16(hit.lho + 28, true);
                        File.writeAllBytes(out, buf.slice(start, start + hit.size));
                        new NativeFunction(libc.getExportByName("chmod"), "int", ["pointer", "int"])(Memory.allocUtf8String(out), 0o644);
                        say(`${NAME} распакован из ${z} (${hit.size} байт)`);
                        return true;
                    } catch (err) { say(`${z}: ${err}`); }
                }
            }
            return false;
        };
        if (!enabled()) { say("мод выключен в config.json"); return; }
        if (!extract()) { say("font.ttf не найден в архивах"); return; }

        // --- подмена данных шрифтов NotoSansDisplay (ими игра пишет кириллицу) при загрузке из data.unity3d ---
        // Unity копирует байты шрифта из распакованных LZ4-блоков кусками через memcpy; начало узнаём по заголовку TTF,
        // и все куски берём из нашего TTF, дополненного нулями (FreeType хвост не читает).
        const ttf = File.readAllBytes(out);
        const SIZES = [360696, 360576];
        if (ttf.byteLength > SIZES[1]) { say(`font.ttf больше ${SIZES[1]} байт — не влезет`); return; }
        // наш TTF, дополненный нулями до размера оригинала (отдельно для каждого из двух шрифтов)
        const pads = SIZES.map(n => { const m = Memory.alloc(n); m.writeByteArray(new Uint8Array(n)); m.writeByteArray(ttf); return m; });
        const hex = (h) => { const m = Memory.alloc(h.length / 2); keep.push(m); m.writeByteArray(h.match(/../g).map(x => parseInt(x, 16))); return m; };
        const state = Memory.alloc(64);
        state.add(8).writeU64(SIZES[0]); state.add(32).writeU64(SIZES[1]);
        // Без onLeave: каждый кусок копирования шрифта сразу берётся из нашего буфера (подмена аргумента src).
        const cm = new CModule(`
#include <gum/guminterceptor.h>
extern unsigned char headA[32], headB[32];
extern unsigned char *padA, *padB;
typedef struct { unsigned char *dst; unsigned long total, written; } Slot;
extern Slot slots[2];
extern int hits;
static int eq32(const unsigned char *a, const unsigned char *b) { for (int i = 0; i < 32; i++) if (a[i] != b[i]) return 0; return 1; }
void onEnter(GumInvocationContext *ic) {
    unsigned char *dst = gum_invocation_context_get_nth_argument(ic, 0);
    unsigned char *src = gum_invocation_context_get_nth_argument(ic, 1);
    unsigned long n = (unsigned long) gum_invocation_context_get_nth_argument(ic, 2);
    for (int k = 0; k < 2; k++) {
        Slot *s = &slots[k];
        if (n >= 32 && eq32(src, k ? headB : headA)) { s->dst = dst; s->written = 0; hits++; }
        if (s->dst && dst == s->dst + s->written && s->written + n <= s->total) {
            gum_invocation_context_replace_nth_argument(ic, 1, (k ? padB : padA) + s->written);
            s->written += n;
            if (s->written >= s->total) s->dst = 0;
            return;
        }
    }
}
`, {
            headA: hex("00010000000e00800003006047444546fbaff5c4000001ec0000053447504f53"),
            headB: hex("00010000000e00800003006047444546fbcff622000001ec0000053447504f53"),
            padA: (() => { const m = Memory.alloc(8); keep.push(m); m.writePointer(pads[0]); return m; })(),
            padB: (() => { const m = Memory.alloc(8); keep.push(m); m.writePointer(pads[1]); return m; })(),
            slots: state,
            hits: state.add(48),
        });
        const l = Interceptor.attach(libc.getExportByName("memcpy"), cm);
        keep.push(cm, pads, state, l);
        say("перехват memcpy установлен, ждём загрузку шрифтов");
        const timer = setInterval(() => {
            const h = state.add(48).readS32();
            if (h >= 2 && state.isNull() === false && state.readPointer().isNull() && state.add(24).readPointer().isNull()) {
                l.detach(); clearInterval(timer); say(`подменено шрифтов: ${h}`);
            }
        }, 500);
        setTimeout(() => { if (state.add(48).readS32() < 2) { l.detach(); clearInterval(timer); say(`за 60 с подменено шрифтов: ${state.add(48).readS32()} из 2`); } }, 60000);
})();

ModKit.define({
    onEnable(ctx) { say("" + status.text); },
});
