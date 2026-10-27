"""Local art preparation, review sheets and Unity image integration. No API calls."""
from __future__ import annotations
import argparse
import hashlib
import json
import math
from pathlib import Path
import re
import shutil
import uuid
from datetime import datetime, timezone
from PIL import Image, ImageDraw, ImageFont, ImageOps

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / 'ArtSource/Kairos/RealisticCards'
RUNTIME = ROOT / 'Assets/Kairos/Art/Resources/KairosArt/Portraits'
PALETTES = {
    'NEUTRAL': 'patinated brass, charcoal, worn leather, cool blue-grey environmental shadow; daily life and honest craftsmanship',
    'DAWN': 'burnt orange and copper sunrise against cool blue-grey mist; dynamic movement, working couriers, wind',
    'NOON': 'ivory cloth, aged gold, steel and stone; firm high-window sunlight with cool shadows; protective humane dignity',
    'DUSK': 'ember copper, muted burgundy, violet-grey twilight; tactile worn objects, memory, emotional cost',
    'NIGHT': 'indigo wool, tarnished silver and brass, cold moonlight, restrained violet reflections; scientific instruments and uncertainty',
    'ETERNAL': 'moss green, warm honey amber, soft reflected light; aged wood, living roots, quiet rain and patient growth',
}
COMMON = '''Use case: illustration-story
Asset type: premium portrait collectible-card illustration for KAIROS; full-bleed artwork only.
Style/medium: highly realistic dark-fantasy narrative painting, comparable to the cinematic realism and rich materials of Gwent-like card illustrations. Believable anatomy and individual expressive faces, refined restrained brushwork, tactile worn metal, cloth, leather, wood and stone, realistic light, compelling original story. Rich midtones, readable darks, one clear focal subject, layered foreground/midground/background. Not a photo shoot or a plastic 3D render.
World: the original KAIROS clock-city is a vertical city of weathered brass, glass, stone bridges and immense clockworks. Its AI Sichen allocated people's time through wrist-clocks until the central clock shattered into five schools: efficiency, order, cost, prediction and continuity. Life-candles represent life; time-sand is wasted time crystallized. Ordinary people's dignity matters; the AI and its echoes are not automatically evil.
'''
ENDING = '''Composition: portrait image designed for a small card; essential face, object and narrative gesture in the central 70 percent; faces around one third from the top; allow slight top/bottom crop to 3:4, no essential content at extreme edges. Use asymmetry and believable perspective where appropriate. A complete scene, not a symbol on a decorative backdrop.
Constraints: original KAIROS characters and setting, no text, letters, numerals, logos, watermark, card border, UI, split panels or collage. Avoid geometric silhouettes, flat vector shapes, woodcut, engraving hatching, line-art, anime, cartoon, low-poly, plastic skin, generic glamour posing, excessive luminous particle effects and black-crushed backgrounds. Card rules and flavor text below are narrative context only: never render their words in the image.'''


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def cards():
    data = json.loads((ROOT / 'docs/data/cards.json').read_text(encoding='utf-8-sig'))
    return data['cards'] + data['keepers']


def valid_image(path):
    if not path.exists():
        return False
    try:
        with Image.open(path) as im:
            im.verify()
        return True
    except (OSError, SyntaxError):
        return False


def prepare():
    scenes = json.loads((SOURCE / 'scenes.json').read_text(encoding='utf-8'))
    entries = cards()
    assert set(scenes) == {c['key'] for c in entries}, 'Scene/card keys do not match'
    assert {p.stem for p in RUNTIME.glob('*.png')} == set(scenes), 'Runtime/card keys do not match'
    (SOURCE / 'prompts').mkdir(exist_ok=True)
    (SOURCE / 'originals').mkdir(exist_ok=True)
    world_path = ROOT / 'docs/KAIROS_世界观与叙事.md'
    world = world_path.read_text(encoding='utf-8')
    entries_out, jobs = [], []
    # Respect the locally chosen model without reading or recording its key.
    model = 'gpt-image-2.5-sunburst'
    local = ROOT / '.env.imagegen.local'
    for line in local.read_text(encoding='utf-8-sig').splitlines():
        if line.strip().startswith('OPENAI_IMAGE_MODEL='):
            model = line.split('=', 1)[1].strip().strip('\"\'')
    for card in entries:
        key = card['key']
        prompt_path = SOURCE / 'prompts' / f'{key}.txt'
        extra = ''
        if key.startswith('K_'):
            name = card['name'].split('·')[-1].strip()
            match = re.search(r'^### [^\n]*' + re.escape(name) + r'\n(.*?)(?=\n### |\n---)', world, re.M | re.S)
            if match:
                extra = '\nKeeper source biography (context, do not render text):\n' + match.group(1).strip()
        prompt = (COMMON + '\nSchool palette and visual direction: ' + PALETTES[card['school']]
                  + '\nSubject and scene: ' + scenes[key] + '\n' + ENDING
                  + '\nCard source context: ' + json.dumps({k: card.get(k) for k in ('key', 'name', 'school', 'type', 'text', 'flavor')}, ensure_ascii=False)
                  + extra + '\n')
        # Keep the manually directed pilot prompt as the exact source of its image.
        if not prompt_path.exists():
            prompt_path.write_text(prompt, encoding='utf-8')
        prompt = prompt_path.read_text(encoding='utf-8')
        entry = dict(key=key, name=card['name'], school=card['school'], kind='keeper' if key.startswith('K_') else 'card', scene=scenes[key], prompt=f'prompts/{key}.txt', prompt_sha256=digest(prompt_path), model=model, size='1024x1536', quality='high', output=f'originals/{key}.png')
        entries_out.append(entry)
        if not valid_image(SOURCE / entry['output']):
            jobs.append(dict(prompt=prompt, model=model, size='1024x1536', quality='high', output_format='png', out=f'{key}.png', n=1))
    manifest = dict(version=1, style='Realistic cinematic dark-fantasy narrative painting', sources={str(p.relative_to(ROOT)).replace('\\', '/'):digest(p) for p in (world_path, ROOT/'docs/data/cards.json')}, runtime_size=[768,1024], entries=entries_out)
    (SOURCE / 'manifest.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
    (SOURCE / 'jobs.remaining.jsonl').write_text(''.join(json.dumps(j, ensure_ascii=False)+'\n' for j in jobs), encoding='utf-8')
    print(f'Prepared {len(entries)} original card prompts; {len(jobs)} missing images. Model: {model}')


def cropped(path, size=(768, 1024)):
    with Image.open(path) as im:
        return ImageOps.fit(im.convert('RGB'), size, method=Image.Resampling.LANCZOS, centering=(.5, .47))


def focus_y(card):
    human_action = {'DAWN_FIRST_LIGHT_STRIKE', 'DAWN_GALE_CHORUS', 'DAWN_LAW_OF_INITIATIVE',
                    'NOON_RADIANT_BARRIER', 'NOON_MOMENT_OF_ORDER', 'NIGHT_PROPHECY',
                    'NIGHT_VOID_GAZE', 'NIGHT_MIRROR_MOMENT', 'DUSK_TWILIGHT_PACT'}
    return .30 if card['key'].startswith('K_') or card.get('type') == 'UNIT' or card['key'] in human_action else .45


def ui_proof():
    group=[c for c in cards() if valid_image(SOURCE/'originals'/f"{c['key']}.png")]
    cols=6; w,h=300,320
    sheet=Image.new('RGB',(cols*w,math.ceil(len(group)/cols)*h+50),'#192027')
    draw=ImageDraw.Draw(sheet)
    draw.text((12,10),'CardFace · 280×270 插画区域裁切校验（布局模拟）',font=font(22),fill='#e7d6b5')
    for i,c in enumerate(group):
        im=cropped(SOURCE/'originals'/f"{c['key']}.png")
        height=im.width/(280/270)
        top=max(0,min(im.height-height,focus_y(c)*im.height-height/2))
        im=im.crop((0,top,im.width,top+height)).resize((280,270),Image.Resampling.LANCZOS)
        x,y=i%cols*w,i//cols*h+50
        sheet.paste(im,(x+10,y))
        draw.text((x+10,y+276),c['name'],font=font(17),fill='#efe2c2')
    sheet.save(SOURCE/'ui-crop-proof.jpg',quality=92)
    print(f'CardFace crop proof updated for {len(group)} images.')


def font(size):
    for path in (ROOT/'Assets/Kairos/Art/Resources/Fonts/MiSans-Regular.ttf', Path('C:/Windows/Fonts/msyh.ttc')):
        if path.exists():
            return ImageFont.truetype(str(path), size)
    return ImageFont.load_default()


def contact():
    entries = cards()
    for school in ['ALL', *PALETTES]:
        group = [c for c in entries if school == 'ALL' or c['school'] == school]
        cols = 8 if school == 'ALL' else min(5,len(group))
        tile_w, tile_h = (196, 302) if school == 'ALL' else (256, 390)
        sheet = Image.new('RGB', (cols*tile_w, math.ceil(len(group)/cols)*tile_h+55), '#10161c')
        draw = ImageDraw.Draw(sheet)
        draw.text((14,12), f'KAIROS · 写实卡牌 / {school}', font=font(23), fill='#e7d6b5')
        for i,c in enumerate(group):
            x,y = (i%cols)*tile_w, (i//cols)*tile_h+55
            path = SOURCE/'originals'/f"{c['key']}.png"
            size=(tile_w-16, int((tile_w-16)*4/3))
            if valid_image(path):
                sheet.paste(cropped(path, size),(x+8,y+4))
            else:
                draw.rectangle((x+8,y+4,x+8+size[0],y+4+size[1]),fill='#26313a')
                draw.text((x+18,y+40),'PENDING',font=font(18),fill='#b6a083')
            draw.text((x+8,y+size[1]+10),c['name'],font=font(15 if school=='ALL' else 18),fill='#f4e3c5')
            draw.text((x+8,y+size[1]+34),c['key'],font=font(10),fill='#8b9ba8')
        dest=SOURCE/f'contact-{school.lower()}.jpg'
        sheet.save(dest,quality=92)
    done = sum(valid_image(SOURCE/'originals'/f"{c['key']}.png") for c in entries)
    print(f'Review sheets updated: {done}/{len(entries)} images available.')


def integrate():
    entries=cards()
    missing=[c['key'] for c in entries if not valid_image(SOURCE/'originals'/f"{c['key']}.png")]
    if missing:
        raise SystemExit('Incomplete set; runtime not touched. Missing: '+', '.join(missing))
    backup=SOURCE/'previous-runtime'
    backup.mkdir(exist_ok=True)
    records=[]
    for c in entries:
        key=c['key']; path=RUNTIME/f'{key}.png'; meta=path.with_suffix('.png.meta'); original=SOURCE/'originals'/f'{key}.png'
        meta_text=meta.read_text(encoding='utf-8')
        guid=re.search(r'^guid: (.+)$',meta_text,re.M).group(1)
        for old in (path,meta):
            if not (backup/old.name).exists():
                shutil.copy2(old,backup/old.name)
        runtime=cropped(original)
        runtime.save(path,optimize=True)
        # Raise only the default import limit, preserving GUID and all other settings.
        new_meta=re.sub(r'(buildTarget: DefaultTexturePlatform\s+maxTextureSize:) 512',r'\g<1> 1024',meta_text)
        if new_meta != meta_text:
            meta.write_text(new_meta,encoding='utf-8')
        assert re.search(r'^guid: (.+)$',meta.read_text(encoding='utf-8'),re.M).group(1)==guid
        records.append(dict(key=key,source_sha256=digest(original),runtime_sha256=digest(path),guid=guid,size=list(runtime.size)))
    framing=dict(portraits=[dict(key=c['key'],focusY=focus_y(c)) for c in entries])
    framing_meta=RUNTIME.parent/'portrait-framing.json.meta'
    if not framing_meta.exists():
        framing_meta.write_text('fileFormatVersion: 2\nguid: '+uuid.uuid4().hex+'\nTextScriptImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n',encoding='utf-8')
    (RUNTIME.parent/'portrait-framing.json').write_text(json.dumps(framing,indent=2)+'\n',encoding='utf-8')
    (SOURCE/'integration.json').write_text(json.dumps(dict(integrated_at=datetime.now(timezone.utc).isoformat(),count=len(records),records=records),ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(f'Integrated {len(records)} portraits at 768x1024; original GUIDs preserved; previous runtime files backed up.')


def status():
    entries=cards()
    existing=[c for c in entries if valid_image(SOURCE/'originals'/f"{c['key']}.png")]
    print(json.dumps(dict(complete=len(existing),total=len(entries),schools={s:sum(c['school']==s for c in existing) for s in PALETTES}),ensure_ascii=False))


def gallery():
    items=[]
    (SOURCE/'thumbnails').mkdir(exist_ok=True)
    for c in cards():
        key=c['key']; path=SOURCE/'originals'/f'{key}.png'
        if valid_image(path):
            cropped(path,(384,512)).save(SOURCE/'thumbnails'/f'{key}.jpg',quality=90)
        items.append(dict(key=key,name=c['name'],school=c['school'],kind='钟主' if key.startswith('K_') else '卡牌',rules=c.get('text',''),flavor=c.get('flavor',''),ready=valid_image(path)))
    template='''<!doctype html><html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>KAIROS · 钟碎之日 / 写实卡牌</title>
<style>*{box-sizing:border-box}body{margin:0;background:#11171c;color:#ded8c9;font:15px/1.6 system-ui,"Microsoft YaHei",sans-serif}header{padding:44px 5vw 24px;border-bottom:1px solid #3e3d34;background:radial-gradient(ellipse at top left,#343226,#11171c 65%)}small{color:#bf9f68;letter-spacing:.16em}h1{font-family:Georgia,"Microsoft YaHei",serif;font-size:clamp(28px,4vw,48px);margin:8px 0}p{max-width:850px;color:#abb3b5}nav{display:flex;gap:10px;flex-wrap:wrap;padding:20px 5vw;position:sticky;top:0;background:#11171cef;backdrop-filter:blur(12px);z-index:2}input,select,button{background:#212a30;color:#e8deca;border:1px solid #46504e;border-radius:5px;padding:9px 12px;font:inherit}button{cursor:pointer}button:hover{border-color:#c8a96c}input{flex:1;min-width:180px}main{padding:0 5vw 40px;display:grid;grid-template-columns:repeat(auto-fill,minmax(210px,1fr));gap:23px}article{cursor:pointer}article img{display:block;width:100%;aspect-ratio:3/4;object-fit:cover;border:1px solid #43463b;background:#27323a;border-radius:5px}article h3{margin:8px 0 0;font-size:16px}article span{font-size:12px;color:#96a19f}article:hover img{outline:1px solid #c8a96c}dialog{width:min(1120px,96vw);max-height:96vh;background:#131b20;color:#e0d9cb;border:1px solid #8a7957;padding:0}dialog::backdrop{background:#000c}.detail{display:grid;grid-template-columns:1.1fr 1fr}.detail>img{width:100%;max-height:90vh;object-fit:contain;background:#090e12}.copy{padding:28px;overflow:auto;max-height:90vh}.copy h2{font-size:26px}.copy p{color:#c2c7c5}.copy a{color:#d4b881}#close{float:right}#count{padding:0 5vw 16px;color:#9aa6a4}blockquote{margin:25px 0;border-left:2px solid #b58d55;padding-left:15px;color:#d5c09b}a{color:#c5ab7e}@media(max-width:680px){.detail{display:block}.copy{max-height:none}.detail>img{max-height:66vh}nav{position:static}main{grid-template-columns:repeat(2,minmax(0,1fr));gap:14px}article h3{font-size:14px}}</style>
<header><small>KAIROS / THE SHATTERED CLOCK</small><h1>钟碎之日 · 写实卡牌</h1><p>46 张卡牌与 10 名钟主。根据刻律之城、五时学派与角色背景制作，统一采用写实暗色奇幻绘画。点击卡片查看完整原图、背景文案和提示词，可切换对照原几何版。此页为美术审阅图库，游戏卡面沿用 Unity Prefab。</p><a href="ART_DIRECTION.md">美术规范</a> · <a href="contact-all.jpg">全套总览</a> · <a href="manifest.json">制作清单</a></header>
<nav><select id="school"><option value="ALL">全部学派</option><option value="NEUTRAL">中立</option><option value="DAWN">晨 · 破晓</option><option value="NOON">昼 · 烈阳</option><option value="DUSK">暮 · 余烬</option><option value="NIGHT">夜 · 星轨</option><option value="ETERNAL">永 · 琥珀</option></select><select id="kind"><option value="ALL">卡牌与钟主</option><option>卡牌</option><option>钟主</option></select><input id="search" placeholder="搜索卡名、Key、背景或效果" aria-label="搜索卡牌"><button id="compare">查看原几何版</button></nav><div id="count"></div><main id="grid"></main>
<dialog id="dialog"><div class="detail"><img id="large" alt=""><div class="copy"><button id="close">关闭</button><small id="key"></small><h2 id="title"></h2><div id="tag"></div><blockquote id="flavor"></blockquote><p id="rules"></p><p><a id="original" target="_blank">打开完整原图</a> · <a id="prompt" target="_blank">查看完整提示词</a></p><button id="swap">切换新旧版本</button></div></div></dialog>
<script>const items=__ITEMS__;let before=false,selected=null,detailBefore=false;const $=s=>document.querySelector(s),names={NEUTRAL:'中立',DAWN:'晨 · 破晓',NOON:'昼 · 烈阳',DUSK:'暮 · 余烬',NIGHT:'夜 · 星轨',ETERNAL:'永 · 琥珀'};function old(c){return '../EtchedCards/'+c.key+'/merged.png'}function show(c){selected=c;detailBefore=before;$('#large').src=detailBefore?old(c):'originals/'+c.key+'.png';$('#large').alt=c.name;$('#key').textContent=c.key;$('#title').textContent=c.name;$('#tag').textContent=names[c.school]+' / '+c.kind;$('#flavor').textContent=c.flavor||'此牌画面根据世界观与机制进行叙事设计。';$('#rules').textContent=c.rules;$('#original').href='originals/'+c.key+'.png';$('#prompt').href='prompts/'+c.key+'.txt';$('#dialog').showModal()}function render(){const q=$('#search').value.toLowerCase(),s=$('#school').value,k=$('#kind').value;const list=items.filter(c=>(s==='ALL'||c.school===s)&&(k==='ALL'||c.kind===k)&&JSON.stringify(c).toLowerCase().includes(q));$('#count').textContent='显示 '+list.length+' / '+items.length+' · 写实图已完成 '+items.filter(c=>c.ready).length;$('#grid').replaceChildren(...list.map(c=>{const a=document.createElement('article'),im=document.createElement('img'),h=document.createElement('h3'),sub=document.createElement('span');im.loading='lazy';im.alt=c.name;im.src=before?old(c):'thumbnails/'+c.key+'.jpg';h.textContent=c.name;sub.textContent=names[c.school]+' · '+c.kind;a.append(im,h,sub);a.tabIndex=0;a.onclick=()=>show(c);a.onkeydown=e=>{if(e.key==='Enter')show(c)};return a}));$('#compare').textContent=before?'查看写实新版':'查看原几何版'}for(const id of ['school','kind','search'])$('#'+id).addEventListener('input',render);$('#compare').onclick=()=>{before=!before;render()};$('#close').onclick=()=>$('#dialog').close();$('#swap').onclick=()=>{detailBefore=!detailBefore;$('#large').src=detailBefore?old(selected):'originals/'+selected.key+'.png'};render();</script></html>'''
    content=template.replace('__ITEMS__',json.dumps(items,ensure_ascii=False).replace('<','\\u003c'))
    (SOURCE/'gallery.html').write_text(content,encoding='utf-8')
    print('Gallery and review thumbnails updated.')


if __name__=='__main__':
    parser=argparse.ArgumentParser()
    parser.add_argument('command',choices=['prepare','contact','integrate','status','gallery','ui_proof'])
    globals()[parser.parse_args().command]()
