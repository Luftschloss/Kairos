import hashlib, json, re, struct, unittest, zipfile
from pathlib import Path
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
SOURCE=ROOT/'ArtSource/Kairos/EtchedCards'
ART=ROOT/'Assets/Kairos/Art/Resources/KairosArt'

def size(path):
 data=path.read_bytes(); assert data[:8]==b'\x89PNG\r\n\x1a\n';return struct.unpack('>II',data[16:24])

class ArtTests(unittest.TestCase):
 def test_complete_unique_runtime_illustrations(self):
  db=json.loads((ROOT/'docs/data/cards.json').read_text(encoding='utf-8'))
  keys=[x['key'] for x in db['cards']+db['keepers']];self.assertEqual(56,len(keys))
  self.assertEqual(set(keys),{x.stem for x in (ART/'Portraits').glob('*.png')})
  hashes=[]
  for key in keys:
   path=ART/'Portraits'/(key+'.png');self.assertEqual((768,1024),size(path));hashes.append(hashlib.sha256(path.read_bytes()).hexdigest())
  self.assertEqual(56,len(set(hashes)))
 def test_realistic_art_provenance_and_preserved_guids(self):
  realistic=ROOT/'ArtSource/Kairos/RealisticCards'
  manifest=json.loads((realistic/'manifest.json').read_text(encoding='utf-8'))
  integration=json.loads((realistic/'integration.json').read_text(encoding='utf-8'))
  records={r['key']:r for r in integration['records']}
  framing=json.loads((ART/'portrait-framing.json').read_text(encoding='utf-8'))['portraits']
  self.assertEqual(56,len(records));self.assertEqual(set(records),{p['key'] for p in framing})
  self.assertTrue(all(0 <= p['focusY'] <= 1 for p in framing))
  for e in manifest['entries']:
   key=e['key'];record=records[key];source=realistic/e['output'];runtime=ART/'Portraits'/(key+'.png')
   self.assertEqual((1024,1536),size(source))
   self.assertEqual(e['prompt_sha256'],hashlib.sha256((realistic/e['prompt']).read_bytes()).hexdigest())
   self.assertEqual(record['source_sha256'],hashlib.sha256(source.read_bytes()).hexdigest())
   self.assertEqual(record['runtime_sha256'],hashlib.sha256(runtime.read_bytes()).hexdigest())
   old=realistic/'previous-runtime'/(key+'.png')
   self.assertNotEqual(hashlib.sha256(old.read_bytes()).hexdigest(),record['runtime_sha256'])
   for meta in (runtime.with_suffix('.png.meta'),old.with_suffix('.png.meta')):
    self.assertEqual(record['guid'],re.search(r'^guid: (.+)$',meta.read_text(encoding='utf-8'),re.M).group(1))
 def test_all_source_layers_and_ora_stack(self):
  manifest=json.loads((SOURCE/'manifest.json').read_text(encoding='utf-8'))
  for entry in manifest['entries']:
   folder=SOURCE/entry['key'];svg=ET.parse(folder/'layered.svg');ns={'svg':'http://www.w3.org/2000/svg'}
   self.assertEqual(manifest['layers'],[g.attrib['id'] for g in svg.findall('svg:g',ns)])
   for name in manifest['layers']:self.assertEqual((600,800),size(folder/(name+'.png')))
   with zipfile.ZipFile(folder/'editable.ora') as z:
    self.assertEqual(b'image/openraster',z.read('mimetype'))
    stack=ET.fromstring(z.read('stack.xml'));self.assertEqual(('600','800'),(stack.attrib['w'],stack.attrib['h']))
    self.assertEqual(list(reversed(manifest['layers'])),[l.attrib['name'] for l in stack.find('stack')])
    for l in stack.find('stack'):self.assertEqual((folder/(l.attrib['name']+'.png')).read_bytes(),z.read(l.attrib['src']))
    self.assertEqual((folder/'merged.png').read_bytes(),z.read('mergedimage.png'))
 def test_school_frames_and_sources(self):
  self.assertEqual({'NEUTRAL','DAWN','NOON','DUSK','NIGHT','ETERNAL'},{p.stem for p in (ART/'Frames').glob('*.png')})
  for p in (ART/'Frames').glob('*.png'):self.assertEqual((384,512),size(p));ET.parse(SOURCE/(p.stem+'_frame.svg'))
 def test_no_text_or_external_images_in_svg_masters(self):
  for path in SOURCE.glob('*/layered.svg'):
   root=ET.parse(path).getroot()
   self.assertFalse(any(x.tag.endswith(('text','image','script')) for x in root.iter()))

if __name__=='__main__':unittest.main()
