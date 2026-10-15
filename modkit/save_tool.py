"""Расшифровка/шифровка файлов сохранения Magic Survival (Z_AESCrypto: AES-128-CBC, IV = ключ, PKCS7, base64)."""
import base64, sys, glob, os
from Crypto.Cipher import AES
from Crypto.Util.Padding import pad, unpad

KEY = b"3ds1s334e4dcc7c4"


def dec(text: str):
    try:
        raw = base64.b64decode(text.strip(), validate=True)
        return unpad(AES.new(KEY, AES.MODE_CBC, KEY).decrypt(raw), 16).decode("utf-8")
    except Exception:
        return None


def enc(plain: str) -> str:
    return base64.b64encode(AES.new(KEY, AES.MODE_CBC, KEY).encrypt(pad(plain.encode("utf-8"), 16))).decode()


def load(dirpath):
    out = {}
    for p in sorted(glob.glob(os.path.join(dirpath, "*.json"))):
        d = dec(open(p, encoding="utf-8-sig").read())
        out[os.path.basename(p)[:-5]] = d
    return out


if __name__ == "__main__":
    a = load(sys.argv[1])
    for k, v in a.items():
        if v is None or len(v) < 200:
            print(k, v)
        else:
            print(k, v[:60], "...", v[-70:])
