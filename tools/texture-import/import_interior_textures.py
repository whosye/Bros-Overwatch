"""Import curated Poly Haven CC0 textures as URP/Lit materials (requires Pillow).

Run from any directory. Downloads are cached in Unity's ignored Temp directory.
Only the new Assets/Materials/Interier_CC0 directory is written.
"""
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
import hashlib
import io
import json
import re
import time
import urllib.request
import uuid

from PIL import Image, ImageDraw, ImageFont, ImageOps

ROOT = Path(__file__).resolve().parents[2]
PROJECT = ROOT / 'OverwatchBros'
DEST = PROJECT / 'Assets/Materials/Interier_CC0'
CACHE = PROJECT / 'Temp/interior-texture-downloads'
PREVIEW = PROJECT / 'Temp/Interier_CC0_prehled.jpg'
CATALOG = [
    ('Predsin', 'Predsin_Teraco', 'terrazzo_tiles', 'Teraco / predsín'),
    ('Predsin', 'Predsin_Dlazba_Hneda', 'brown_floor_tiles', 'Hneda dlazba / predsín'),
    ('Kuchyn', 'Kuchyn_Dlazba_Bezova', 'interior_tiles', 'Bezova dlazba / kuchyn'),
    ('Kuchyn', 'Kuchyn_Dlazba_Svetla', 'floor_tiles_02', 'Svetla dlazba / kuchyn'),
    ('Kuchyn', 'Kuchyn_Lino_Retro', 'old_linoleum_flooring_01', 'Retro lino / kuchyn'),
    ('Kuchyn', 'Kuchyn_Obklad_Bily', 'long_white_tiles', 'Bily obklad / kuchyn'),
    ('Chodba', 'Chodba_Lino_Hnede', 'linoleum_brown', 'Hnede lino / chodba'),
    ('Chodba', 'Chodba_Parkety', 'herringbone_parquet', 'Parkety / chodba'),
    ('Chodba', 'Chodba_Dlazba_Seda', 'grey_tiles', 'Seda dlazba / chodba'),
    ('Sklep', 'Sklep_Podlaha_Beton', 'concrete_floor', 'Beton / sklepni podlaha'),
    ('Sklep', 'Sklep_Podlaha_Beton_Osoupany', 'worn_concrete_floor', 'Osoupany beton / podlaha'),
    ('Sklep', 'Sklep_Zed_Cihly', 'brick_wall_001', 'Cihly / sklepni zed'),
    ('Sklep', 'Sklep_Zed_Omitka_Cihly', 'rough_plaster_brick', 'Opryskana omitka a cihly'),
    ('Sklep', 'Sklep_Zed_Omitka_Seda', 'grey_plaster', 'Seda omitka / sklepni zed'),
]


def fetch(url, attempts=3):
    for attempt in range(attempts):
        try:
            request = urllib.request.Request(url, headers={
                'User-Agent': 'BrosOverwatch-interior-material-import/1.0',
            })
            with urllib.request.urlopen(request, timeout=90) as response:
                return response.read()
        except Exception:
            if attempt + 1 == attempts:
                raise
            time.sleep(attempt + 1)


def metadata(endpoint, asset):
    path = CACHE / f'{asset}_{endpoint}.json'
    if not path.exists():
        path.write_bytes(fetch(f'https://api.polyhaven.com/{endpoint}/{asset}'))
    return json.loads(path.read_text(encoding='utf-8'))


def stable_guid(relative):
    return uuid.uuid5(uuid.NAMESPACE_URL, 'bros-overwatch/interior-cc0/' + relative).hex


def write(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    if isinstance(data, str):
        data = data.encode('utf-8')
    if not path.exists() or path.read_bytes() != data:
        path.write_bytes(data)


def asset_meta(path, kind=''):
    relative = path.relative_to(DEST).as_posix()
    guid = stable_guid(relative)
    if kind == 'folder':
        text = f'fileFormatVersion: 2\nguid: {guid}\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {{}}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n'
    elif kind in ('color', 'normal', 'mask'):
        template = PROJECT / 'Assets/Materials/Wood' / ('WoodPlanks_Normal.png.meta' if kind == 'normal' else 'WoodPlanks.png.meta')
        text = template.read_text(encoding='utf-8')
        text = re.sub(r'^guid: .*$', f'guid: {guid}', text, flags=re.M)
        text = re.sub(r'^(\s*)sRGBTexture: \d+', r'\g<1>sRGBTexture: ' + ('1' if kind == 'color' else '0'), text, flags=re.M)
        text = re.sub(r'^(\s*)aniso: \d+', r'\g<1>aniso: 4', text, flags=re.M)
        text = re.sub(r'^(\s*)alphaUsage: \d+', r'\g<1>alphaUsage: ' + ('1' if kind == 'mask' else '0'), text, flags=re.M)
    else:
        text = f'fileFormatVersion: 2\nguid: {guid}\n'
    meta = Path(str(path) + '.meta')
    # Never replace an existing Unity-generated GUID on a subsequent run.
    if meta.exists():
        existing = re.search(r'^guid: (\w+)', meta.read_text(), re.M)
        if existing:
            return existing.group(1)
    write(meta, text)
    return guid


def folder(path):
    path.mkdir(parents=True, exist_ok=True)
    if path != DEST.parent:
        asset_meta(path, 'folder')


def download(asset, files, choices, ext):
    channel = next((name for name in choices if name in files), None)
    if channel is None:
        raise ValueError(f'{asset}: missing {choices}')
    formats = files[channel]['2k']
    selected_ext = ext if ext in formats else 'jpg'
    entry = formats[selected_ext]
    cache_file = CACHE / f'{asset}_{channel}_2k.{selected_ext}'
    def valid(data):
        return len(data) == entry['size'] and hashlib.md5(data).hexdigest() == entry['md5']
    data = cache_file.read_bytes() if cache_file.exists() else b''
    if not valid(data):
        data = fetch(entry['url'])
        if not valid(data):
            raise ValueError(f'{asset}: size/checksum mismatch for {channel}')
        write(cache_file, data)
    with Image.open(io.BytesIO(data)) as im:
        im.verify()
    return data, selected_ext, {'channel': channel, **entry}


def make_material(path, name, guids, width, height):
    text = (PROJECT / 'Assets/Materials/Wood/Wood_Planks.mat').read_text(encoding='utf-8')
    text = text.replace('m_Name: Wood_Planks', 'm_Name: ' + name)
    text = text.replace('m_ValidKeywords:\n  - _NORMALMAP',
                        'm_ValidKeywords:\n  - _METALLICSPECGLOSSMAP\n  - _NORMALMAP\n  - _OCCLUSIONMAP')
    for prop, guid in [('_BaseMap', guids['color']), ('_MainTex', guids['color']),
                       ('_BumpMap', guids['normal']), ('_MetallicGlossMap', guids['mask']),
                       ('_OcclusionMap', guids['mask'])]:
        pattern = r'(- ' + prop + r':\s*\n\s*m_Texture: )\{[^\n]*\}'
        text, count = re.subn(pattern, lambda m: m[1] + '{fileID: 2800000, guid: ' + guid + ', type: 3}', text)
        assert count == 1, prop
    # Physical map width/height from Poly Haven, in meters. World Space UV=1
    # therefore gives realistic tile/brick size without hand-tuning every mesh.
    text = re.sub(r'm_Scale: \{x: 1, y: 1\}', f'm_Scale: {{x: {1/width:.8g}, y: {1/height:.8g}}}', text)
    text = re.sub(r'(- _Smoothness:) [^\n]+', r'\1 1', text)
    text = re.sub(r'(- _Metallic:) [^\n]+', r'\1 1', text)
    text = re.sub(r'(- _OcclusionStrength:) [^\n]+', r'\1 0.7', text)
    write(path, text)
    asset_meta(path)


def import_one(item):
    group, name, asset, label = item
    info, files = metadata('info', asset), metadata('files', asset)
    location = DEST / group
    maps = location / 'Textures' / asset
    for path in (location, location / 'Textures', maps):
        folder(path)
    diffuse, dext, dsource = download(asset, files, ['Diffuse', 'diff'], 'jpg')
    normal, nextension, nsource = download(asset, files, ['nor_gl'], 'png')
    arm, _, asource = download(asset, files, ['arm'], 'jpg')
    color_path, normal_path, mask_path = (maps / f'Albedo.{dext}', maps / f'NormalGL.{nextension}', maps / 'UnityMask.png')
    write(color_path, diffuse)
    # Source PNGs can be 16-bit RGBA (20 MB each). RGB8 is sufficient for
    # Unity's compressed game normal maps and avoids storing unused alpha.
    with Image.open(io.BytesIO(normal)) as im:
        buffer = io.BytesIO()
        im.convert('RGB').save(buffer, format='PNG', optimize=True)
        write(normal_path, buffer.getvalue())
    with Image.open(io.BytesIO(arm)) as im:
        ao, roughness, _ = im.convert('RGB').split()
        # URP: R=metallic, G=occlusion, A=smoothness. Poly Haven: R=AO,G=rough,B=metal.
        zero = Image.new('L', im.size, 0)
        # All curated materials are dielectrics. Force metallic=0 rather than
        # reproducing JPEG cross-channel artifacts in the source ARM image.
        mask = Image.merge('RGBA', (zero, ao, zero, ImageOps.invert(roughness)))
        buffer = io.BytesIO()
        mask.save(buffer, format='PNG', optimize=True)
        write(mask_path, buffer.getvalue())
    guids = {role: asset_meta(path, role) for role, path in [('color', color_path), ('normal', normal_path), ('mask', mask_path)]}
    dimensions = info.get('dimensions')
    if not dimensions or len(dimensions) < 2 or min(dimensions[:2]) <= 0:
        raise ValueError(f'{asset}: missing physical dimensions')
    width, height = dimensions[0] / 1000, dimensions[1] / 1000
    material_path = location / (name + '.mat')
    make_material(material_path, name, guids, width, height)
    print(f'OK {name} ({width:g} x {height:g} m)', flush=True)
    return {'material': str(material_path.relative_to(DEST)).replace('\\', '/'),
            'label': label, 'asset': asset, 'source': f'https://polyhaven.com/a/{asset}',
            'license': 'CC0-1.0', 'authors': list(info.get('authors', {})),
            'physical_size_m': [width, height], 'resolution': '2k',
            'downloads': [dsource, nsource, asource],
            'generated_normal': 'OpenGL RGB8 PNG, unused alpha removed',
            'generated_mask': 'R=0 (dielectric), G=AO, B=0, A=1-roughness',
            'albedo': str(color_path.relative_to(DEST)).replace('\\', '/')}


def preview(records):
    # Preview uses the downloaded CC0 albedo maps, not website example renders.
    cell_w, cell_h, columns = 300, 344, 4
    sheet = Image.new('RGB', (columns * cell_w, ((len(records) + columns - 1) // columns) * cell_h), '#20252b')
    draw = ImageDraw.Draw(sheet)
    try:
        font = ImageFont.truetype('C:/Windows/Fonts/arial.ttf', 17)
        small = ImageFont.truetype('C:/Windows/Fonts/arial.ttf', 13)
    except OSError:
        font = small = ImageFont.load_default()
    for i, record in enumerate(records):
        x, y = (i % columns) * cell_w, (i // columns) * cell_h
        with Image.open(DEST / record['albedo']) as im:
            tile = ImageOps.fit(im.convert('RGB'), (280, 280))
        sheet.paste(tile, (x + 10, y + 10))
        draw.text((x + 10, y + 298), record['label'], font=font, fill='white')
        draw.text((x + 10, y + 322), record['asset'], font=small, fill='#b6c7d9')
    sheet.save(PREVIEW, quality=90)


def main():
    CACHE.mkdir(parents=True, exist_ok=True)
    folder(DEST)
    # Avoid concurrent writers creating parent metadata.
    for group in sorted({item[0] for item in CATALOG}):
        folder(DEST / group)
        folder(DEST / group / 'Textures')
    with ThreadPoolExecutor(max_workers=3) as pool:
        records = list(pool.map(import_one, CATALOG))
    manifest = DEST / 'Sources.json'
    write(manifest, json.dumps({'provider': 'Poly Haven', 'license_url': 'https://polyhaven.com/license', 'materials': records}, indent=2, ensure_ascii=False) + '\n')
    asset_meta(manifest)
    lines = [
        'INTERIER CC0 - hotove materialy pro Unity URP', '',
        'Pouziti: pretahni soubor .mat na plochu. V ProBuilderu pro souvisle',
        'podlahy pouzij Auto UV, World Space, Anchor None, Tiling 1/1.',
        'Materialy jiz obsahuji prevod na fyzickou velikost textury v metrech.',
        'Na beznem meshi s UV 0..1 velikost uprav podle potreby.', '',
        'Vsechny zdrojove textury: Poly Haven, CC0-1.0.',
        'Povoleno pouziti a upravy ve hre vcetne komercniho sireni.',
        'Licence: https://polyhaven.com/license',
        'CC0: https://creativecommons.org/publicdomain/zero/1.0/', '',
        'Normal: OpenGL, import jako Normal Map. Albedo: sRGB.',
        'UnityMask: linear; R=0 (nekovovy povrch), G=AO, B=0, A=smoothness.',
        'Mapa UnityMask je zapojena do Metallic i Occlusion slotu.',
        'Neni nutny displacement ani vlastni shader.', '', 'MATERIALY A ZDROJE:', '',
    ]
    for r in records:
        lines += [r['material'], '  ' + r['source'], '  Autori: ' + ', '.join(r['authors'])]
    readme = DEST / 'CTI_ME.txt'
    write(readme, '\n'.join(lines) + '\n')
    asset_meta(readme)
    preview(records)
    print(f'Imported {len(records)} materials. Preview: {PREVIEW}', flush=True)


if __name__ == '__main__':
    main()
