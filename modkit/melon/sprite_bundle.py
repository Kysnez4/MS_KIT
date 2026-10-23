"""Бандл спрайтов мода из PNG без Unity Editor: берёт шаблон sprite_template.bundle (256 слотов, собран один раз
скриптом modkit/tools/unity_sprite_template/BuildTemplate.cs в Unity 2022.3.62f3 под Android) и подставляет в слоты
картинки, размеры и имена. В игре бандл грузит UiKit.LoadSpriteBundle: объект mk_<имя> → спрайт <имя>.
Запуск отдельно: python sprite_bundle.py <папка с PNG> <выход.bundle> [ppu]"""
import os, struct, sys
import UnityPy
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
TEMPLATE = os.path.join(HERE, 'sprite_template.bundle')
SLOTS = 256


def build(pngs, out, ppu=100.0):
    """pngs: {имя: путь к PNG}. Спрайт FullRect, опорная точка в центре."""
    if len(pngs) > SLOTS: raise SystemExit(f'спрайтов {len(pngs)}, в шаблоне {SLOTS} слотов')
    env = UnityPy.load(TEMPLATE)
    objs = {o.path_id: o for o in env.objects}
    gos = sorted((o for o in env.objects if o.type.name == 'GameObject'), key=lambda o: o.read_typetree()['m_Name'])
    names = sorted(pngs)
    for i, go in enumerate(gos):
        g = go.read_typetree()
        if i >= len(names):
            g['m_Name'] = 'free_%03d' % i; go.save_typetree(g); continue
        name = names[i]
        img = Image.open(pngs[name]).convert('RGBA')
        w, h = img.size
        # GameObject → SpriteRenderer → Sprite → Texture2D (все в sharedAssets/сцене по path_id)
        sr = next(objs[c['component']['m_PathID']] for c in g['m_Component'] if objs[c['component']['m_PathID']].type.name == 'SpriteRenderer')
        sp_ptr = sr.read_typetree()['m_Sprite']
        sp_obj = next(o for o in env.objects if o.type.name == 'Sprite' and o.path_id == sp_ptr['m_PathID'])
        s = sp_obj.read_typetree()
        tex_obj = next(o for o in env.objects if o.type.name == 'Texture2D' and o.path_id == s['m_RD']['texture']['m_PathID'])

        tex = tex_obj.read()
        tex.m_Width, tex.m_Height = w, h
        tex.set_image(img)
        tex.save()

        s['m_Name'] = name
        s['m_Rect'] = {'x': 0.0, 'y': 0.0, 'width': float(w), 'height': float(h)}
        s['m_PixelsToUnits'] = float(ppu)
        rd = s['m_RD']
        rd['textureRect'] = {'x': 0.0, 'y': 0.0, 'width': float(w), 'height': float(h)}
        rd['uvTransform'] = {'x': float(ppu), 'y': w / 2.0, 'z': float(ppu), 'w': h / 2.0}
        # вершины шаблона (поток 0, float3) — квадрат 4×4 px при ppu 100 вокруг центра; UV движок считает сам из textureRect
        vd = bytearray(rd['m_VertexData']['m_DataSize'])
        tw, tppu = 4.0, 100.0
        for v in range(rd['m_VertexData']['m_VertexCount']):
            x, y, z = struct.unpack_from('<3f', vd, v * 12)
            struct.pack_into('<3f', vd, v * 12, x * (w / tw) * (tppu / ppu), y * (h / tw) * (tppu / ppu), z)
        rd['m_VertexData']['m_DataSize'] = bytes(vd)
        for sm in rd['m_SubMeshes']:
            sm['localAABB'] = {'m_Center': {'x': 0.0, 'y': 0.0, 'z': 0.0}, 'm_Extent': {'x': w / 2.0 / ppu, 'y': h / 2.0 / ppu, 'z': 0.0}}
        sp_obj.save_typetree(s)
        g['m_Name'] = 'mk_' + name
        go.save_typetree(g)
    with open(out, 'wb') as f:
        f.write(env.file.save(packer='lz4'))
    return out


def from_dir(d, out, ppu=100.0):
    pngs = {os.path.splitext(f)[0]: os.path.join(d, f) for f in os.listdir(d) if f.lower().endswith('.png')}
    return build(pngs, out, ppu)


if __name__ == '__main__':
    print(from_dir(sys.argv[1], sys.argv[2], float(sys.argv[3]) if len(sys.argv) > 3 else 100.0))
