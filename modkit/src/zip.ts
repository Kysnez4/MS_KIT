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

/**
 * Читает из .zip на диске ОДИН файл, не загружая архив целиком (open/pread из libc): конец архива, каталог, нужная запись.
 * Для просмотра mod.json в больших архивах (музыка, картинки) это в сотни раз быстрее, чем читать файл полностью.
 */
let io: {
    open: NativeFunction<number, [NativePointer, number]>;
    close: NativeFunction<number, [number]>;
    lseek: NativeFunction<Int64, [number, number, number]>;
    pread: NativeFunction<Int64, [number, NativePointer, number, number]>;
} | null = null;

/** Размер файла в байтах (open/lseek); -1, если не открылся. */
export function fileSize(path: string): number {
    ioInit();
    const fd = io!.open(Memory.allocUtf8String(path), 0);
    if (fd < 0) return -1;
    try { return Number(io!.lseek(fd, 0, 2)); } finally { io!.close(fd); }
}

function ioInit(): void {
    if (io) return;
    const libc = Process.getModuleByName("libc.so");
    io = {
        open: new NativeFunction(libc.getExportByName("open"), "int", ["pointer", "int"]),
        close: new NativeFunction(libc.getExportByName("close"), "int", ["int"]),
        lseek: new NativeFunction(libc.getExportByName("lseek"), "int64", ["int", "int64", "int"]) as any,
        pread: new NativeFunction(libc.getExportByName("pread"), "int64", ["int", "pointer", "uint64", "int64"]) as any,
    };
}

export function readZipEntry(path: string, name: string): Uint8Array {
    ioInit();   // NativeFunction создаётся один раз: каждый new NativeFunction генерирует машинный код и стоит миллисекунды
    const { open, close, lseek, pread } = io!;
    const fd = open(Memory.allocUtf8String(path), 0);
    if (fd < 0) throw new Error("файл не открылся");
    try {
        const size = Number(lseek(fd, 0, 2));
        const at = (offset: number, len: number): Uint8Array => {
            const buf = Memory.alloc(Math.max(1, len));
            const n = Number(pread(fd, buf, len, offset));
            if (n !== len) throw new Error("чтение zip оборвалось");
            return new Uint8Array(buf.readByteArray(len) as ArrayBuffer);
        };
        const tailLen = Math.min(size, 22 + 65535);
        const tail = at(size - tailLen, tailLen);
        let eocd = -1;
        for (let i = tail.length - 22; i >= 0; i--) if (u32(tail, i) === 0x06054b50) { eocd = i; break; }
        if (eocd < 0) throw new Error("не zip-архив");
        const count = u16(tail, eocd + 10), cdSize = u32(tail, eocd + 12), cdOff = u32(tail, eocd + 16);
        const cd = at(cdOff, cdSize);
        for (let p = 0, n = 0; n < count && p + 46 <= cd.length; n++) {
            const nameLen = u16(cd, p + 28), extraLen = u16(cd, p + 30), commentLen = u16(cd, p + 32);
            // имена не декодируем: в больших архивах (тысячи записей) это секунды; сравниваем байты
            let hit = nameLen === name.length;
            for (let i = 0; hit && i < nameLen; i++) if (cd[p + 46 + i] !== name.charCodeAt(i)) hit = false;
            if (hit) {
                const method = u16(cd, p + 10), csize = u32(cd, p + 20), usize = u32(cd, p + 24), off = u32(cd, p + 42);
                const lh = at(off, 30);
                const raw = at(off + 30 + u16(lh, 26) + u16(lh, 28), csize);
                if (method === 0) return raw;
                if (method === 8) return inflateRaw(raw, usize);
                throw new Error(`метод сжатия ${method} не поддерживается`);
            }
            p += 46 + nameLen + extraLen + commentLen;
        }
        throw new Error("в архиве нет " + name);
    } finally { close(fd); }
}
