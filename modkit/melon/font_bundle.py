"""Бандл шрифта мода из TTF/OTF без Unity Editor: берёт шаблон font_template.bundle (сцена с объектом mk_font, у него UI.Text
с динамическим шрифтом; собран скриптом modkit/tools/unity_font_template/BuildFontTemplate.cs в Unity 2022.3.62f3 под Android)
и подставляет байты шрифта, имя и метрики. В игре бандл грузит UiKit.LoadFontBundle.
Запуск отдельно: python font_bundle.py <font.ttf> <выход.bundle>
Вычистить заглушку из свежесобранного шаблона: python font_bundle.py --strip <modkit_font> <font_template.bundle>"""
import os, struct, sys
import UnityPy

HERE = os.path.dirname(os.path.abspath(__file__))
TEMPLATE = os.path.join(HERE, 'font_template.bundle')


def metrics(data, size=16.0):
    """Ascent/descent/межстрочный интервал как у импортёра Unity (по hhea, кегль импорта 16)."""
    if data[:4] == b'ttcf': raise SystemExit('коллекции шрифтов (.ttc) не поддерживаются')
    tables = {}
    for i in range(struct.unpack_from('>H', data, 4)[0]):
        tag, _, off, _ = struct.unpack_from('>4sIII', data, 12 + 16 * i)
        tables[tag] = off
    upm = struct.unpack_from('>H', data, tables[b'head'] + 18)[0]
    asc, desc, gap = struct.unpack_from('>hhh', data, tables[b'hhea'] + 4)
    k = size / upm
    return asc * k, desc * k, (asc - desc + gap) * k


def build(font_path, out, template=TEMPLATE, name=None):
    data = open(font_path, 'rb').read()
    asc, desc, line = metrics(data)
    env = UnityPy.load(template)
    font = next(o for o in env.objects if o.type.name == 'Font')
    f = font.read_typetree()
    f['m_FontData'] = list(data)
    f['m_Name'] = name or os.path.splitext(os.path.basename(font_path))[0]
    f['m_FontNames'] = [f['m_Name']]
    f['m_Ascent'], f['m_Descent'], f['m_LineSpacing'] = asc, desc, line
    font.save_typetree(f)
    with open(out, 'wb') as fo:
        fo.write(env.file.save(packer='lz4'))
    return out


def strip(src, out):
    """Убирает TTF-заглушку из шаблона (в репозитории не должно быть чужого шрифта)."""
    env = UnityPy.load(src)
    font = next(o for o in env.objects if o.type.name == 'Font')
    f = font.read_typetree()
    f['m_FontData'] = []
    f['m_Name'] = 'slot'; f['m_FontNames'] = ['ModKitFont']
    font.save_typetree(f)
    with open(out, 'wb') as fo:
        fo.write(env.file.save(packer='lz4'))


if __name__ == '__main__':
    if sys.argv[1] == '--strip': strip(sys.argv[2], sys.argv[3])
    else: build(sys.argv[1], sys.argv[2])
