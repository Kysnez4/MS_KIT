// Музыка на волне босса. Играет файл matador.mp3 через системный OpenSL ES (Java bridge на этом телефоне падает, Unity-аудио из файла не загрузить).
// Музыка лежит прямо в архиве мода (zip без сжатия): при первом запуске она распаковывается в Android/data/<пакет>/files/modkit/matador.mp3.
// Свой файл можно подложить вместо неё: Download/ModKit/matador.mp3 или тот же modkit/matador.mp3 (он главнее архива).
ModKit.define({
    onEnable(ctx) {
        const NAME = "matador.mp3";
        const libc = Process.getModuleByName("libc.so");

        // --- поиск файла ---
        let pkg = "com.vkslrzm.Zombie";
        for (const lib of ["libg.so", "libmain.so"]) {
            try {
                const m = /\/([A-Za-z][\w.]*)-[\w=-]{16,}\/lib\//.exec(Process.getModuleByName(lib).path);
                if (m) { pkg = m[1]; break; }
            } catch (_) { /* нет модуля */ }
        }
        const access = new NativeFunction(libc.getExportByName("access"), "int", ["pointer", "int"]);
        const candidates = [
            `/sdcard/Android/data/${pkg}/files/modkit/${NAME}`,
            `/sdcard/Android/data/${pkg}/files/modkit/mods/${NAME}`,
            `/sdcard/Download/ModKit/${NAME}`,
        ];

        // Достаёт matador.mp3 из zip мода (метод «store») и кладёт в папку modkit приложения; возвращает путь или null.
        const extractFromZip = () => {
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
                        let off = dv.getUint32(e + 16, true);
                        for (let i = dv.getUint16(e + 10, true); i > 0; i--) {
                            if (dv.getUint32(off, true) !== 0x02014b50) break;
                            const method = dv.getUint16(off + 10, true), size = dv.getUint32(off + 24, true);
                            const nlen = dv.getUint16(off + 28, true), elen = dv.getUint16(off + 30, true), clen = dv.getUint16(off + 32, true);
                            const lho = dv.getUint32(off + 42, true);
                            const name = String.fromCharCode(...new Uint8Array(buf, off + 46, nlen));
                            off += 46 + nlen + elen + clen;
                            if (name !== NAME) continue;
                            if (method !== 0) throw new Error("файл в архиве сжат, нужен «store»");
                            const start = lho + 30 + dv.getUint16(lho + 26, true) + dv.getUint16(lho + 28, true);
                            const out = `/sdcard/Android/data/${pkg}/files/modkit/${NAME}`;
                            File.writeAllBytes(out, buf.slice(start, start + size));
                            new NativeFunction(libc.getExportByName("chmod"), "int", ["pointer", "int"])(Memory.allocUtf8String(out), 0o644);
                            ctx.log(`[matador] ${NAME} распакован из ${z} (${size} байт)`);
                            return out;
                        }
                    } catch (e) { ctx.log(`[matador] ${z}: ${e}`); }
                }
            }
            return null;
        };
        const path = candidates.find(p => access(Memory.allocUtf8String(p), 4) === 0) ?? extractFromZip();
        if (!path) { ctx.log(`[matador] нет файла ${NAME}, искал: ${candidates.join(", ")}`); return; }

        // --- OpenSL ES ---
        let sl = Process.findModuleByName("libOpenSLES.so");
        if (!sl) {
            const dl = Process.findModuleByName("libdl.so") ?? libc;
            new NativeFunction(dl.getExportByName("dlopen"), "pointer", ["pointer", "int"])(Memory.allocUtf8String("libOpenSLES.so"), 2);
            sl = Process.getModuleByName("libOpenSLES.so");
        }
        const iid = n => sl.getExportByName("SL_IID_" + n).readPointer();
        // Интерфейс OpenSL — указатель на указатель на таблицу функций: (*itf)->fn(itf, ...)
        const call = (itf, idx, args, ...vals) =>
            new NativeFunction(itf.readPointer().add(idx * 8).readPointer(), "uint32", ["pointer", ...args])(itf, ...vals);
        const check = (what, rc) => { if (rc !== 0) throw new Error(`${what}: код ${rc}`); };
        const getItf = (obj, name) => {
            const out = Memory.alloc(8);
            check("GetInterface " + name, call(obj, 3, ["pointer", "pointer"], iid(name), out));
            return out.readPointer();
        };

        let engineObj = null, mixObj = null, playerObj = null, playItf = null;
        let active = false, paused = false, startedAt = 0, testLeft = 0;
        const keep = [];
        try {
            const pEngine = Memory.alloc(8);
            check("slCreateEngine", new NativeFunction(sl.getExportByName("slCreateEngine"), "uint32",
                ["pointer", "uint32", "pointer", "uint32", "pointer", "pointer"])(pEngine, 0, NULL, 0, NULL, NULL));
            engineObj = pEngine.readPointer();
            check("Realize engine", call(engineObj, 0, ["int"], 0));
            const engine = getItf(engineObj, "ENGINE");

            const pMix = Memory.alloc(8);
            check("CreateOutputMix", call(engine, 7, ["pointer", "uint32", "pointer", "pointer"], pMix, 0, NULL, NULL));
            mixObj = pMix.readPointer();
            check("Realize mix", call(mixObj, 0, ["int"], 0));

            const uri = Memory.allocUtf8String("file://" + path);
            const loc = Memory.alloc(16); loc.writeU32(1); loc.add(8).writePointer(uri);                 // SL_DATALOCATOR_URI
            const fmt = Memory.alloc(24); fmt.writeU32(1); fmt.add(8).writePointer(NULL); fmt.add(16).writeU32(1); // MIME, тип по содержимому
            const src = Memory.alloc(16); src.writePointer(loc); src.add(8).writePointer(fmt);
            const mixLoc = Memory.alloc(16); mixLoc.writeU32(4); mixLoc.add(8).writePointer(mixObj);     // SL_DATALOCATOR_OUTPUTMIX
            const snk = Memory.alloc(16); snk.writePointer(mixLoc); snk.add(8).writePointer(NULL);
            const ids = Memory.alloc(24);
            ["PLAY", "SEEK", "VOLUME"].forEach((n, i) => ids.add(i * 8).writePointer(iid(n)));
            const req = Memory.alloc(12);
            [0, 1, 2].forEach(i => req.add(i * 4).writeU32(1));
            keep.push(uri, loc, fmt, src, mixLoc, snk, ids, req);

            const pPlayer = Memory.alloc(8);
            check("CreateAudioPlayer", call(engine, 2, ["pointer", "pointer", "pointer", "uint32", "pointer", "pointer"], pPlayer, src, snk, 3, ids, req));
            playerObj = pPlayer.readPointer();
            check("Realize player", call(playerObj, 0, ["int"], 0));
            playItf = getItf(playerObj, "PLAY");
            const seek = getItf(playerObj, "SEEK");
            check("SetLoop", call(seek, 1, ["uint32", "uint32", "uint32"], 1, 0, 0xFFFFFFFF));
            const vol = getItf(playerObj, "VOLUME");
            const mb = Math.round(2000 * Math.log10(Math.max(1, ctx.settings.volume) / 100));
            check("SetVolumeLevel", call(vol, 0, ["int16"], mb));
            ctx.log(`[matador] готов: ${path}, громкость ${mb} мБ`);
        } catch (e) {
            ctx.log(`[matador] ошибка инициализации: ${e}`);
            playItf = null;
        }

        const state = s => { if (playItf) call(playItf, 0, ["uint32"], s); };   // 1 стоп, 2 пауза, 3 играет
        const start = why => {
            if (!playItf) return;
            try { state(1); state(3); active = true; paused = false; startedAt = Date.now(); ctx.log("[matador] играет: " + why); }
            catch (e) { ctx.log("[matador] play: " + e); }
        };
        const stop = why => {
            if (!playItf || !active) return;
            try { state(1); } catch (e) { ctx.log("[matador] stop: " + e); }
            active = false; paused = false; ctx.log("[matador] стоп: " + why);
        };
        const hook = (cls, method, fn) => {
            try { ctx.after(cls, method, 0, fn); } catch (e) { ctx.log(`[matador] хук ${method}: ${e}`); }
        };

        hook("C_Battle_Manager", "Play_BossWaveEffect_Start", () => start("волна босса"));
        hook("C_Battle_Manager", "Play_BossWaveEffect_End", () => stop("конец волны"));
        hook("C_Battle_Manager", "Start", () => stop("новый бой"));
        hook("C_Battle_Manager", "Button_GameGiveUp_ON", () => stop("сдался"));
        hook("C_Battle_Manager", "Button_GameExit_ON", () => stop("выход"));
        hook("C_Battle_Manager", "Button_StopGamePlay", () => {
            if (active && !paused) { try { state(2); paused = true; } catch (_) { /* ignore */ } }
        });
        const resume = () => { if (active && paused) { try { state(3); paused = false; } catch (_) { /* ignore */ } } };
        hook("C_Battle_Manager", "Button_StopExit", resume);
        hook("C_Battle_Manager", "Instant_StopExit", resume);

        // тестовый запуск без босса и страховка от бесконечной музыки
        if (playItf && ctx.settings.test > 0) { testLeft = ctx.settings.test; start("тест"); }
        ctx.every(1000, () => {
            if (testLeft > 0 && --testLeft === 0) stop("тест окончен");
            else if (active && !paused && Date.now() - startedAt > ctx.settings.maxsec * 1000) stop("лимит времени");
        });

        ctx.onCleanup(() => {
            try { stop("мод выключен"); } catch (_) { /* ignore */ }
            for (const o of [playerObj, mixObj, engineObj]) {
                try { if (o) call(o, 6, []); } catch (_) { /* ignore */ }   // Destroy
            }
            playItf = null;
        });
    },
});
