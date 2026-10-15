/** Чтение .zip на телефоне: «store» разбирается в JS, «deflate» распаковывается системным libz.so. */

export interface ZipEntry { name: string; method: number; csize: number; size: number; offset: number }

function libz(): Module {
    return Process.getModuleByName("libz.so");
}

function u16(b: Uint8Array, o: number): number { return b[o] | (b[o + 1] << 8); }
function u32(b: Uint8Array, o: number): number { return (b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24)) >>> 0; }

export function utf8(b: Uint8Array): string {
    let s = "";
    for (let i = 0; i < b.length;) {
        const c = b[i++];
        if (c < 0x80) s += String.fromCharCode(c);
        else if (c < 0xe0) s += String.fromCharCode(((c & 31) << 6) | (b[i++] & 63));
        else if (c < 0xf0) { s += String.fromCharCode(((c & 15) << 12) | ((b[i] & 63) << 6) | (b[i + 1] & 63)); i += 2; }
        else {
            const cp = ((c & 7) << 18) | ((b[i] & 63) << 12) | ((b[i + 1] & 63) << 6) | (b[i + 2] & 63); i += 3;
            s += String.fromCodePoint(cp);
        }
    }
    return s;
}

/** Сырой deflate (без заголовка zlib) через inflateInit2_/inflate из libz.so. */
function inflateRaw(data: Uint8Array, outSize: number): Uint8Array {
    const z = libz();
    const initFn = new NativeFunction(z.getExportByName("inflateInit2_"), "int", ["pointer", "int", "pointer", "int"]);
    const inflateFn = new NativeFunction(z.getExportByName("inflate"), "int", ["pointer", "int"]);
    const endFn = new NativeFunction(z.getExportByName("inflateEnd"), "int", ["pointer"]);
    const STRM = 112;                                    // sizeof(z_stream) на arm64
    const strm = Memory.alloc(STRM);
    strm.writeByteArray(new Uint8Array(STRM).buffer);
    const input = Memory.alloc(Math.max(1, data.length));
    input.writeByteArray(data.buffer.slice(data.byteOffset, data.byteOffset + data.length) as ArrayBuffer);
    const output = Memory.alloc(Math.max(1, outSize));
    if (initFn(strm, -15, Memory.allocUtf8String("1.2.11"), STRM) !== 0) throw new Error("inflateInit2 не удался");
    try {
        strm.writePointer(input);                        // next_in
        strm.add(8).writeU32(data.length);               // avail_in
        strm.add(24).writePointer(output);               // next_out
        strm.add(32).writeU32(outSize);                  // avail_out
        const rc = inflateFn(strm, 4);                   // Z_FINISH
        if (rc !== 1) throw new Error("inflate код " + rc);
    } finally { endFn(strm); }
    return new Uint8Array(output.readByteArray(outSize) as ArrayBuffer);
}

export class ZipReader {
    readonly entries = new Map<string, ZipEntry>();
    constructor(private readonly buf: Uint8Array) {
        let eocd = -1;
        for (let i = buf.length - 22; i >= Math.max(0, buf.length - 22 - 65535); i--) {
            if (u32(buf, i) === 0x06054b50) { eocd = i; break; }
        }
        if (eocd < 0) throw new Error("не zip-архив");
        const count = u16(buf, eocd + 10);
        let p = u32(buf, eocd + 16);
        for (let n = 0; n < count; n++) {
            if (u32(buf, p) !== 0x02014b50) throw new Error("повреждён каталог zip");
            const nameLen = u16(buf, p + 28), extraLen = u16(buf, p + 30), commentLen = u16(buf, p + 32);
            const name = utf8(buf.subarray(p + 46, p + 46 + nameLen)).replace(/\\/g, "/");
            this.entries.set(name, { name, method: u16(buf, p + 10), csize: u32(buf, p + 20), size: u32(buf, p + 24), offset: u32(buf, p + 42) });
            p += 46 + nameLen + extraLen + commentLen;
        }
    }
    has(name: string): boolean { return this.entries.has(name); }
    read(name: string): Uint8Array {
        const e = this.entries.get(name);
        if (!e) throw new Error("в архиве нет " + name);
        const b = this.buf;
        const start = e.offset + 30 + u16(b, e.offset + 26) + u16(b, e.offset + 28);
        const raw = b.subarray(start, start + e.csize);
        if (e.method === 0) return raw;
        if (e.method === 8) return inflateRaw(raw, e.size);
        throw new Error(`метод сжатия ${e.method} не поддерживается`);
    }
    text(name: string): string { return utf8(this.read(name)).replace(/^﻿/, ""); }
}
