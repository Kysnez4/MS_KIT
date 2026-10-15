// Упаковывает папку мода в .zip (mod.json + скрипт + картинки):
//   npm run pack-mod -- examples\wrath_x8 [выход.zip]
import { existsSync, readFileSync, readdirSync, statSync, writeFileSync } from "node:fs";
import { basename, join, resolve } from "node:path";

const crcTable = Array.from({ length: 256 }, (_, n) => { let c = n; for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1; return c >>> 0; });
const crc32 = b => { let c = 0xffffffff; for (const x of b) c = crcTable[(c ^ x) & 255] ^ (c >>> 8); return (c ^ 0xffffffff) >>> 0; };

function walk(dir, prefix = "") {
    return readdirSync(dir).sort().flatMap(f => statSync(join(dir, f)).isDirectory() ? walk(join(dir, f), prefix + f + "/") : [prefix + f]);
}

/** Собирает zip без сжатия («store»): моды маленькие, картинки уже сжаты, а телефону не нужен распаковщик. */
export function packDir(dir) {
    const meta = join(dir, "mod.json");
    if (!existsSync(meta)) throw new Error("в папке нет mod.json");
    const m = JSON.parse(readFileSync(meta, "utf8"));
    if (!m.id || !m.name) throw new Error("в mod.json нужны id и name");
    const need = [m.main ?? "main.js", ...(m.icon ? [m.icon] : []), ...(m.screenshots ?? [])];
    for (const f of need) if (!existsSync(join(dir, f))) throw new Error(`mod.json ссылается на отсутствующий файл ${f}`);

    const parts = [], central = [];
    let off = 0;
    for (const name of walk(dir)) {
        const data = readFileSync(join(dir, name)), nb = Buffer.from(name, "utf8"), crc = crc32(data);
        const h = Buffer.alloc(30);
        h.writeUInt32LE(0x04034b50, 0); h.writeUInt16LE(20, 4); h.writeUInt16LE(0x0800, 6);
        h.writeUInt32LE(crc, 14); h.writeUInt32LE(data.length, 18); h.writeUInt32LE(data.length, 22); h.writeUInt16LE(nb.length, 26);
        const c = Buffer.alloc(46);
        c.writeUInt32LE(0x02014b50, 0); c.writeUInt16LE(20, 4); c.writeUInt16LE(20, 6); c.writeUInt16LE(0x0800, 8);
        c.writeUInt32LE(crc, 16); c.writeUInt32LE(data.length, 20); c.writeUInt32LE(data.length, 24); c.writeUInt16LE(nb.length, 28); c.writeUInt32LE(off, 42);
        parts.push(h, nb, data); central.push(c, nb);
        off += 30 + nb.length + data.length;
    }
    const cd = Buffer.concat(central);
    const end = Buffer.alloc(22);
    end.writeUInt32LE(0x06054b50, 0); end.writeUInt16LE(central.length / 2, 8); end.writeUInt16LE(central.length / 2, 10);
    end.writeUInt32LE(cd.length, 12); end.writeUInt32LE(off, 16);
    return { zip: Buffer.concat([...parts, cd, end]), id: m.id };
}

if (process.argv[1] && resolve(process.argv[1]) === resolve(import.meta.filename)) {
    const dir = process.argv[2];
    if (!dir || !existsSync(dir)) { console.error("Использование: npm run pack-mod -- папка_мода [выход.zip]"); process.exit(1); }
    const { zip, id } = packDir(dir);
    const out = process.argv[3] ?? join(resolve(dir, ".."), (basename(resolve(dir)) || id) + ".zip");
    writeFileSync(out, zip);
    console.log("OK:", out, `(${zip.length} байт)`);
}
