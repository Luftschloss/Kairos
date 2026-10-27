import argparse, hashlib, json, math, random, zipfile, xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
W,H=600,800
PALETTES={
 'NEUTRAL':('#101e23','#b59a68','#d4c9a0','#56777a'),
 'DAWN':('#18202a','#c78152','#eed8a3','#7497a0'),
 'NOON':('#15232b','#b7a063','#f0deb0','#577b82'),
 'DUSK':('#211b25','#b25845','#e4b17b','#70566c'),
 'NIGHT':('#151b32','#8278ab','#d0c7e2','#4c7597'),
 'ETERNAL':('#1a2525','#9b995a','#e0d3a0','#5c8272')}
# Bespoke visual direction per key. Geometry is original, not sourced from other games.
MOTIFS={
'N_GEAR_APPRENTICE':'artisan','N_BELL_GUARD':'bellguard','N_WANDERING_TIMEKEEPER':'wanderer','N_BRASS_GOLEM':'golem','N_HOURGLASS':'hourglass','N_PENDULUM_BLADE':'blade',
'DAWN_SCOUT':'runner','DAWN_COURIER':'courier','DAWN_FIRST_LIGHT_STRIKE':'strike','DAWN_INTERCEPTOR':'interceptor','DAWN_GALE_CHORUS':'wind','DAWN_RIDER':'rider','DAWN_LAW_OF_INITIATIVE':'law','DAWN_SUNRISE_TORRENT':'sunburst',
'NOON_MORNING_PRAYER':'monk','NOON_ZENITH_WARDEN':'shieldguard','NOON_RADIANT_BARRIER':'shield','NOON_MOMENT_OF_ORDER':'dial','NOON_SUNDIAL_JUDGE':'judge','NOON_NOON_JUDGMENT':'scales','NOON_COVENANT_OF_RADIANCE':'covenant','NOON_SOLAR_ANGEL':'angel',
'DUSK_ASH_ACOLYTE':'acolyte','DUSK_RITE_OF_OFFERING':'offering','DUSK_WHISPERER_OF_PAST':'whisperer','DUSK_ECHO_OF_THE_DEAD':'echo','DUSK_WITHERING_TOUCH':'hand','DUSK_TWILIGHT_PACT':'pact','DUSK_REAPER':'reaper','DUSK_EMBER_COLOSSUS':'colossus',
'NIGHT_STARGAZER_APPRENTICE':'astronomer','NIGHT_STARFALL':'meteor','NIGHT_FATE_WEAVER':'weaver','NIGHT_PROPHECY':'eye','NIGHT_VOID_GAZE':'void','NIGHT_TEMPORAL_REWIND':'spiral','NIGHT_MIRROR_MOMENT':'mirror','NIGHT_STARS_REALIGN':'constellation',
'ETERNAL_SPROUT':'sprout','ETERNAL_MOSS_WATCHER':'moss','ETERNAL_AMBER_SEAL':'amber','ETERNAL_SLOW_CURRENT':'river','ETERNAL_RING_TREE':'tree','ETERNAL_STASIS_FIELD':'field','ETERNAL_MILLENNIUM_INSTANT':'rings','ETERNAL_EVERLASTING_GUARDIAN':'guardian',
'K_DAWN_IRIN':'irin','K_DAWN_AXUN':'axun','K_NOON_ORIS':'oris','K_NOON_KELAI':'kelai','K_DUSK_MORGAN':'morgan','K_DUSK_SHENMO':'shenmo','K_NIGHT_SELENE':'selene','K_NIGHT_ZERO':'zero','K_ETERNAL_GOULD':'gould','K_ETERNAL_XIAOMAN':'xiaoman'}

class Layer:
 def __init__(self,name):self.name=name;self.shapes=[]
 def shape(self,kind,points,fill=None,stroke=None,width=1,alpha=255):self.shapes.append(dict(kind=kind,points=points,fill=fill or '',stroke=stroke or '',width=width,alpha=alpha))
 def poly(self,p,f=None,s=None,w=1,a=255):self.shape('poly',[v for xy in p for v in xy],f,s,w,a)
 def rect(self,x,y,w,h,f=None,s=None,lw=1,a=255):self.shape('rect',[x,y,w,h],f,s,lw,a)
 def ellipse(self,x,y,w,h,f=None,s=None,lw=1,a=255):self.shape('ellipse',[x,y,w,h],f,s,lw,a)
 def line(self,x1,y1,x2,y2,s,w=1,a=255):self.shape('line',[x1,y1,x2,y2],None,s,w,a)
 def svg(self):
  out=[]
  for p in self.shapes:
   q=p['points'];attrs=f'fill="{p["fill"] or "none"}" stroke="{p["stroke"] or "none"}" stroke-width="{p["width"]}" opacity="{p["alpha"]/255:.4f}"'
   if p['kind']=='poly':out.append(f'<polygon points="'+ ' '.join(f'{q[i]},{q[i+1]}' for i in range(0,len(q),2))+f'" {attrs}/>')
   elif p['kind']=='line':out.append(f'<line x1="{q[0]}" y1="{q[1]}" x2="{q[2]}" y2="{q[3]}" {attrs}/>')
   elif p['kind']=='rect':out.append(f'<rect x="{q[0]}" y="{q[1]}" width="{q[2]}" height="{q[3]}" {attrs}/>')
   else:out.append(f'<ellipse cx="{q[0]+q[2]/2}" cy="{q[1]+q[3]/2}" rx="{q[2]/2}" ry="{q[3]/2}" {attrs}/>')
  return '\n'.join(out)

def gear(l,x,y,r,color,teeth=16,inner=.55):
 points=[]
 for i in range(teeth*4):
  a=i*math.tau/(teeth*4);rr=r if i%4 in (1,2) else r*.84;points.append((x+math.cos(a)*rr,y+math.sin(a)*rr))
 l.poly(points,color);l.ellipse(x-r*inner,y-r*inner,r*inner*2,r*inner*2,PALETTES['NEUTRAL'][0],color,2)
 for i in range(8):
  a=i*math.tau/8;l.line(x+math.cos(a)*r*.25,y+math.sin(a)*r*.25,x+math.cos(a)*r*.72,y+math.sin(a)*r*.72,color,2)

def dial(l,x,y,r,metal,light,dark,hands=True):
 for dr,w in ((0,3),(10,1),(21,2)):l.ellipse(x-r+dr,y-r+dr,(r-dr)*2,(r-dr)*2,None,metal,w)
 for i in range(60):
  a=i*math.tau/60;size=15 if i%5==0 else 5
  l.line(x+math.sin(a)*(r-25),y+math.cos(a)*(r-25),x+math.sin(a)*(r-25-size),y+math.cos(a)*(r-25-size),light if i%5==0 else metal,2 if i%5==0 else 1)
 if hands:l.line(x,y,x-r*.3,y-r*.6,light,4);l.line(x,y,x+r*.55,y-r*.2,light,2);l.ellipse(x-6,y-6,12,12,metal)

def person(l,x,y,scale,role,metal,light,dark,accent):
 def p(pts,f=dark,s=metal,w=2):l.poly([(x+a*scale,y+b*scale) for a,b in pts],f,s,w)
 def ln(a,b,c,d,col=metal,w=1):l.line(x+a*scale,y+b*scale,x+c*scale,y+d*scale,col,w)
 # Hood, angular face, long engraved cloak; silhouette landmarks differ by profession.
 p([(-76,-178),(-45,-240),(6,-264),(52,-228),(70,-170),(27,-189),(-19,-181)])
 p([(-24,-224),(26,-225),(36,-196),(12,-177),(-17,-185)],metal,light,1)
 ln(2,-216,25,-215,dark,3);ln(18,-215,15,-195,dark,2);ln(-11,-203,-7,-191,light,2)
 p([(-21,-179),(-105,-139),(-139,70),(-81,94),(-52,200),(100,200),(125,55),(74,-129),(22,-171)])
 p([(-50,-151),(-95,72),(-38,181),(18,-21),(25,-165)],accent,metal,1)
 for i in range(9):ln(-48+i*7,-115,-91+i*20,184,light if i%3==0 else metal,1)
 p([(52,-130),(117,-86),(164,-18),(137,6),(68,-42),(27,-75)],dark,metal)
 p([(136,-17),(176,-10),(178,9),(154,21),(133,6)],metal,light,1)
 p([(-80,-115),(-147,-63),(-128,9),(-103,1),(-103,-43),(-47,-79)],dark,metal)
 if role in ('runner','courier','irin','axun','rider'):
  p([(-67,-145),(-186,-152),(-251,-77),(-141,-109),(-40,-90)],accent,light)
  p([(-39,188),(-130,268),(-83,280),(29,190)],dark,metal);p([(47,187),(136,263),(158,250),(93,168)],dark,metal)
  p([(48,-27),(101,-9),(94,54),(42,34)],metal,light)
  ln(51,-10,89,3,dark,2);ln(81,1,78,32,dark,2)
 elif role in ('bellguard','shieldguard','interceptor','oris','judge','guardian'):
  p([(-162,-37),(-58,-56),(-27,74),(-94,157),(-173,63)],accent,light,4)
  ln(-108,-21,-95,127,light,3);ln(-158,39,-37,28,light,3)
  l.ellipse(x-124*scale,y+14*scale,34*scale,34*scale,None,metal,2)
  ln(163,-130,161,203,metal,7);p([(162,-207),(146,-150),(178,-147)],light,metal)
 elif role in ('reaper','whisperer','gould','wanderer','astronomer','selene'):
  ln(169,-235,160,220,metal,7);l.ellipse(x+149*scale,y-260*scale,38*scale,38*scale,None,light,3)
  if role=='reaper':p([(165,-220),(57,-272),(-74,-233),(20,-241),(144,-192)],metal,light)
 elif role in ('monk','acolyte','kelai','morgan','shenmo','xiaoman','artisan','weaver'):
  p([(-16,-27),(53,-38),(82,31),(7,57),(-42,29)],accent,light)
  l.ellipse(x+3*scale,y-21*scale,30*scale,42*scale,light,metal,1)
  ln(18,-47,18,-68,light,2)
 elif role=='zero':
  p([(-22,-225),(26,-225),(32,-190),(-22,-183)],light,metal);ln(-10,-209,16,-209,dark,4);ln(-8,-196,13,-196,dark,2)


def subject(l,m,rng,metal,light,dark,accent):
 human={'artisan','bellguard','wanderer','runner','courier','interceptor','rider','monk','shieldguard','judge','acolyte','whisperer','echo','reaper','astronomer','weaver','irin','axun','oris','kelai','morgan','shenmo','selene','zero','gould','xiaoman','guardian'}
 if m in human:
  if m=='echo':person(l,228,477,.8,'shenmo',metal,light,dark,accent);person(l,367,471,.66,'shenmo',accent,metal,dark,metal)
  else:person(l,282+rng.randint(-16,16),451,1.02 if m.startswith(('g','o')) else .94,m,metal,light,dark,accent)
  # Distinct silhouettes and role props within the shared etched language.
  if m in ('irin','courier'):
   l.poly([(224,277),(251,256),(335,264),(351,285)],light,metal,2)
   l.poly([(318,483),(387,468),(402,560),(333,578)],metal,light,2)
   for j in range(3):l.line(345,496+j*19,385,490+j*19,dark,3)
  if m=='axun':
   l.poly([(246,242),(265,199),(282,230),(314,206),(325,264)],dark,light,2)
   for x in (262,302):l.ellipse(x,249,27,15,None,light,3)
   l.line(252,283,248,354,light,4);l.line(330,279,338,349,light,4)
  if m in ('judge','oris'):
   l.poly([(238,242),(235,187),(287,165),(342,187),(339,242)],accent,light,3)
   for j in range(5):l.line(247+j*18,199,247+j*18,228,metal,2)
   l.poly([(392,488),(458,451),(492,493),(426,532)],metal,light,2);l.line(429,501,462,625,metal,7)
  if m in ('kelai','morgan'):
   l.poly([(242,240),(251,210),(326,209),(340,241)],light,metal,2)
   l.rect(279,214,10,25,accent);l.rect(271,222,27,8,accent)
   l.poly([(368,486),(416,486),(417,563),(370,558)],light,metal,3)
   l.line(393,497,393,541,accent,4);l.line(377,518,407,518,accent,4)
  if m=='shenmo':
   l.poly([(236,261),(250,223),(294,218),(327,240),(334,291),(316,275),(306,237),(257,242),(248,285)],metal,light,2)
   l.ellipse(349,399,99,121,None,light,4);person(l,399,476,.22,'zero',metal,light,dark,accent)
  if m=='gould':
   l.poly([(250,236),(233,286),(244,326),(264,296),(279,239)],light,metal,2)
   l.poly([(319,234),(333,269),(304,332),(271,302),(287,271)],metal,light,2)
  if m=='xiaoman':
   l.poly([(206,245),(251,213),(317,212),(365,242)],metal,light,3);l.ellipse(180,235,210,20,metal,light,2)
   l.poly([(366,514),(405,459),(443,500),(435,561),(382,567)],accent,light,2)
  if m=='selene':
   l.poly([(235,262),(238,218),(283,190),(331,224),(341,279),(316,257),(302,217),(254,234),(248,282)],light,metal,2)
   l.poly([(347,230),(365,243),(344,258),(333,246)],light,metal,1)
  if m=='zero':
   for yy in (341,369,397):l.rect(238,yy,110,7,light,None,1,170)
   l.poly([(387,435),(460,472),(417,582),(365,535)],accent,light,3)
  if m=='artisan':l.ellipse(189,430,62,68,None,light,3);l.line(221,456,366,387,metal,7)
  if m=='rider':l.poly([(225,248),(254,213),(310,204),(356,234),(329,274)],metal,light,3)
  if m in ('astronomer','selene','weaver'):dial(l,398,368,81,metal,light,dark);l.line(145,572,455,351,light,2)
  if m in ('artisan','bellguard'):gear(l,142,604,76,metal);gear(l,467,700,99,accent)
  if m=='rider':l.poly([(144,666),(132,571),(75,495),(34,491),(66,555),(121,694),(221,712),(277,669)],dark,metal,3)
  if m=='gould':l.poly([(48,620),(135,575),(161,388),(194,292),(201,428),(173,642)],accent,metal,2)
 elif m in ('golem','colossus'):
  for x,y,w,h in [(176,239,226,134),(140,380,300,220),(78,380,67,213),(444,368,66,235),(169,618,84,127),(333,609,99,140)]:l.poly([(x,y),(x+w,y-12),(x+w+8,y+h),(x+12,y+h+6)],dark,metal,5)
  dial(l,290,471,77,metal,light,dark);gear(l,302,285,82,metal)
  for x in (217,345):l.ellipse(x,287,25,13,light)
  for i in range(10):l.line(153,402+i*18,425,392+i*18,metal,1)
  if m=='colossus':
   for i in range(12):x=170+i*23;l.poly([(x,610),(x+9,410+rng.randrange(100)),(x+20,618)],accent,None,1,155)
 elif m in ('hourglass','amber','mirror'):
  l.poly([(162,250),(442,250),(400,354),(349,457),(409,635),(438,704),(160,704),(189,629),(250,452),(193,356)],dark,metal,6)
  for x in (146,441):l.rect(x,243,15,472,metal);l.ellipse(x-14,224,42,27,light)
  l.rect(135,231,330,18,metal);l.rect(129,709,342,20,metal)
  if m=='hourglass':l.poly([(205,326),(403,326),(309,444)],metal,light,2);l.poly([(189,686),(309,561),(412,686)],metal,light,2);l.line(309,440,309,566,light,2)
  elif m=='amber':l.poly([(216,307),(380,285),(413,580),(308,687),(189,599)],accent,light,5);l.poly([(282,345),(245,476),(301,595),(336,401)],dark,metal,3)
  else:l.ellipse(179,274,251,400,accent,light,3);person(l,303,493,.56,'zero',metal,light,dark,accent);l.line(391,294,217,636,light,2)
 elif m in ('shield','covenant','pact','law','scales','offering'):
  l.poly([(146,289),(457,289),(424,562),(302,719),(178,563)],dark,metal,5)
  if m in ('shield','covenant'):dial(l,301,436,106,metal,light,dark);l.line(302,324,302,655,light,3)
  elif m in ('pact','law'):l.poly([(171,312),(295,335),(427,310),(414,605),(297,628),(180,602)],accent,metal,2);l.line(297,335,297,628,light,2)
  elif m=='scales':
   l.line(300,305,300,683,light,6);l.line(135,371,453,371,metal,6)
   for x in (181,409):l.line(x,374,x-58,497,light,2);l.line(x,374,x+58,497,light,2);l.poly([(x-64,497),(x+64,497),(x+35,539),(x-36,539)],accent,light,2)
  else:l.ellipse(178,424,248,104,accent,light,4);l.poly([(230,447),(293,307),(328,457)],light,metal,2)
  if m=='law':
   for yy in range(351,563,30):l.line(204,yy,278,yy+8,light,2);l.line(324,yy+8,395,yy,light,2)
   l.poly([(283,576),(307,551),(330,576),(307,602)],metal,light,2)
  if m=='pact':
   l.line(199,343,407,578,light,3);l.line(417,338,189,565,light,3);l.ellipse(252,400,100,122,dark,metal,4)
   l.poly([(275,434),(328,434),(304,489)],light,metal,2)
  if m=='covenant':
   for x in (146,452):l.rect(x,589,18,85,light,metal,2);l.poly([(x-2,582),(x+9,548),(x+21,580)],light,None)
  if m=='shield':
   for rr in (139,165,190):l.ellipse(300-rr,436-rr,rr*2,rr*2,None,light,2,150)
 elif m in ('tree','moss','sprout','rings'):
  l.poly([(271,704),(256,457),(223,361),(179,291),(231,331),(279,387),(320,266),(368,224),(337,330),(311,452),(333,694)],dark,metal,4)
  for i in range(16):
   x=rng.randint(95,480);y=rng.randint(236,478);l.poly([(x,y),(x-18,y-34),(x+12,y-57),(x+36,y-17)],accent,metal,1)
  for rr in (40,66,90):l.ellipse(292-rr,571-rr,rr*2,rr*2,None,light,2)
  if m=='sprout':l.ellipse(137,655,336,92,dark,metal,3);l.poly([(298,548),(187,469),(169,387),(265,431)],accent,light,3);l.poly([(296,546),(394,462),(429,370),(337,422)],accent,light,3)
  if m=='moss':person(l,302,514,.65,'shieldguard',metal,light,dark,accent)
  if m=='tree':
   for i in range(12):l.line(273+i*4,404,258+i*6,719,metal,2)
  if m=='rings':
   for r in range(32,260,22):l.ellipse(300-r,442-r,r*2,r*2,None,metal,2)
 elif m=='hand':
  l.poly([(177,719),(218,518),(191,410),(215,382),(245,450),(242,300),(267,282),(287,443),(298,245),(324,244),(327,443),(354,285),(379,296),(359,480),(407,405),(429,422),(391,550),(339,611),(321,743)],accent,light,4)
  for i in range(9):l.line(224+i*11,515,197+i*16,694,metal,2)
 elif m=='angel':
  for sign in (-1,1):
   l.poly([(301,403),(300+sign*176,238),(300+sign*253,280),(300+sign*206,408),(300+sign*106,518)],accent,metal,3)
   for i in range(10):l.line(301,434,300+sign*(126+i*12),274+i*8,light,2)
  person(l,300,506,.83,'shieldguard',metal,light,dark,accent)
 else:
  # Geometric time phenomena, each with a distinct focal symbol and etched flow direction.
  if m in ('eye','void'):l.poly([(80,446),(208,348),(392,348),(524,446),(391,537),(210,537)],accent,metal,3);dial(l,300,443,87,metal,light,dark,False);l.ellipse(268,405,64,78,dark,light,2)
  elif m in ('dial','field','spiral','constellation'):dial(l,300,448,174,metal,light,dark)
  elif m in ('blade','strike','meteor'):l.poly([(179,687),(305,255),(344,231),(328,331),(225,701)],light,metal,3);l.poly([(164,612),(313,574),(331,595),(168,639)],metal,light,2)
  elif m in ('sunburst','wind'):l.ellipse(196,311,210,210,metal,light,4)
  elif m=='river':l.poly([(100,278),(212,277),(421,526),(302,599),(496,750),(304,800),(156,610),(278,511)],accent,metal,3)
  if m=='wind':
   for j in range(9):l.poly([(35,352+j*21),(214,364+j*14),(505,240+j*28),(395,354+j*24),(208,409+j*13),(56,398+j*17)],None,light,2,170)
  if m=='sunburst':
   for j in range(24):
    a=j*math.tau/24;l.poly([(300+math.cos(a)*126,416+math.sin(a)*126),(300+math.cos(a+.05)*263,416+math.sin(a+.05)*263),(300+math.cos(a+.08)*130,416+math.sin(a+.08)*130)],light,None,1,120)
  if m=='meteor':
   for j in range(7):l.line(369+j*23,173,218+j*13,615,light,4,160)
   l.poly([(178,673),(135,719),(248,697),(305,749),(249,679),(298,644),(215,655)],metal,light,3)
  if m=='void':l.ellipse(206,335,189,211,dark,accent,9);l.ellipse(265,395,72,92,dark,None)
  if m=='spiral':
   points=[]
   for j in range(160):a=j*.15;r=12+j*.98;points.append((300+math.cos(a)*r,448+math.sin(a)*r))
   for i in range(len(points)-1):l.line(*points[i],*points[i+1],light,3)
  if m=='constellation':
   stars=[(180,246),(409,309),(462,469),(342,599),(124,533),(229,410)]
   for i,(x,y) in enumerate(stars):l.line(x,y,*stars[(i+2)%len(stars)],light,2);l.ellipse(x-9,y-9,18,18,light,metal,2)
  if m=='field':
   for j in range(6):l.poly([(123+j*22,648),(290+j*7,255),(475-j*21,643)],None,accent,3)
  if m in ('constellation','meteor','wind','sunburst','spiral','field','strike'):
   for i in range(26):
    a=i*.38;rad=50+i*7;x=300+math.cos(a)*rad;y=450+math.sin(a)*rad
    l.line(x,y,300+math.cos(a+.28)*rad,450+math.sin(a+.28)*rad,light if i%4==0 else metal,3 if i%4==0 else 1)
    if m=='constellation':l.ellipse(x-4,y-4,8,8,light)

def make(entry,index,kind):
 key=entry['key'];seed=int(hashlib.sha256(key.encode()).hexdigest()[:8],16);rng=random.Random(seed)
 dark,metal,light,accent=PALETTES[entry['school']];m=MOTIFS[key]
 ls=[Layer('01_atmosphere'),Layer('02_clock_city'),Layer('03_subject'),Layer('04_foreground'),Layer('05_engraving')]
 bg,city,fig,fg,grain=ls
 bg.rect(0,0,W,H,dark)
 # Banded ink sky and a luminous circular focal plane, not a flat icon background.
 for i in range(25):bg.rect(0,90+i*24,600,25,accent,None,1,max(3,34-i))
 for r in range(222,69,-14):bg.ellipse(300-r,304-r,r*2,r*2,metal,None,1,8)
 dial(bg,300,302,202,metal,light,dark,False)
 for i in range(23):
  x=i*29-22;y=rng.randint(170,425);ww=rng.randint(24,48)
  city.poly([(x,655),(x,y+30),(x+ww/2,y),(x+ww,y+30),(x+ww,655)],dark,metal,1)
  for yy in range(y+60,620,31):
   city.rect(x+7,yy,4,10,metal,None,1,130)
  if i%4==0:city.line(x+ww/2,y-45,x+ww/2,y,metal,2)
 # Tower offset per identity and bridge perspective.
 tx=72 if index%2 else 442;city.rect(tx,99,59,453,dark,metal,2);city.poly([(tx-13,111),(tx+30,42),(tx+74,111)],dark,metal,2);dial(city,tx+30,187,25,metal,light,dark)
 for y in (344,536):city.line(0,y,600,y+40,metal,2)
 for x in range(-120,730,65):city.line(300,461,x,800,metal,1,75)
 subject(fig,m,rng,metal,light,dark,accent)
 # Foreground stone sill, clockwork debris and symbolic particles.
 fg.poly([(0,745),(151,710),(276,739),(419,724),(600,751),(600,800),(0,800)],dark,metal,2)
 if entry['school']=='ETERNAL':
  for i in range(11):x=rng.randrange(600);fg.poly([(x,784),(x-21,686),(x+4,666),(x+24,772)],accent,metal,1)
 elif entry['school']=='DUSK':
  for i in range(34):x=rng.randrange(600);y=rng.randrange(400,779);fg.poly([(x,y),(x+5,y-14),(x+10,y+3)],light,None,1,100)
 elif entry['school']=='NIGHT':
  for i in range(30):x=rng.randrange(40,560);y=rng.randrange(78,695);fg.line(x-4,y,x+4,y,light,1,160);fg.line(x,y-4,x,y+4,light,1,160)
 else:gear(fg,42,784,65,metal);gear(fg,544,807,91,accent)
 # Original etched paper speckles and edge vignetting, fixed seed.
 for i in range(560):
  x=rng.randrange(600);y=rng.randrange(800);grain.line(x,y,x+rng.randint(2,11),y-rng.randint(0,3),light if i%3 else dark,1,25)
 for i in range(28):grain.rect(i,0,1,800,dark,None,1,110-i*3);grain.rect(599-i,0,1,800,dark,None,1,110-i*3)
 return dict(key=key,name=entry['name'],school=entry['school'],kind=kind,motif=m,seed=seed,layers=[dict(name=l.name,shapes=l.shapes) for l in ls]),ls

def frame(school):
 dark,metal,light,accent=PALETTES[school];l=Layer('frame')
 for inset,w in ((5,10),(18,2),(28,2)):l.rect(inset,inset,W-inset*2,H-inset*2,None,metal,w)
 for x,y,sx,sy in ((28,28,1,1),(572,28,-1,1),(28,772,1,-1),(572,772,-1,-1)):
  l.poly([(x,y),(x+sx*77,y),(x+sx*35,y+sy*14),(x+sx*14,y+sy*35),(x,y+sy*77)],metal,light,1)
  l.ellipse(x+sx*24-5,y+sy*24-5,10,10,accent,light,1)
 # School identity is bound as live metadata; no crest behind titles in compact Prefabs.
 return l

def generate():
 data=json.loads((ROOT/'docs/data/cards.json').read_text(encoding='utf-8'))
 source=ROOT/'ArtSource/Kairos/EtchedCards';source.mkdir(parents=True,exist_ok=True)
 entries=[]
 for kind,items in [('card',data['cards']),('keeper',data['keepers'])]:
  for i,e in enumerate(items):
   obj,ls=make(e,i,kind);entries.append(obj);folder=source/e['key'];folder.mkdir(exist_ok=True)
   svg='<svg xmlns="http://www.w3.org/2000/svg" xmlns:inkscape="http://www.inkscape.org/namespaces/inkscape" width="600" height="800" viewBox="0 0 600 800">\n'+''.join(f'<g id="{l.name}" inkscape:groupmode="layer" inkscape:label="{l.name}">\n{l.svg()}\n</g>' for l in ls)+'</svg>'
   (folder/'layered.svg').write_text(svg,encoding='utf-8')
 frames=[]
 for school in PALETTES:
  l=frame(school);frames.append(dict(key=school,shapes=l.shapes));(source/(school+'_frame.svg')).write_text(f'<svg xmlns="http://www.w3.org/2000/svg" width="600" height="800">{l.svg()}</svg>',encoding='utf-8')
 manifest=dict(version=1,style='Original dark clock-city woodcut; procedural vector illustration',width=W,height=H,entries=entries,frames=frames)
 tmp=ROOT/'Library/Locus/tmp/etched-card-render.json';tmp.parent.mkdir(parents=True,exist_ok=True);tmp.write_text(json.dumps(manifest,ensure_ascii=False),encoding='utf-8')
 slim=dict(version=1,style=manifest['style'],source_resolution=[W,H],runtime_resolution=[384,512],layers=['01_atmosphere','02_clock_city','03_subject','04_foreground','05_engraving'],entries=[{k:v for k,v in e.items() if k!='layers'} for e in entries])
 (source/'manifest.json').write_text(json.dumps(slim,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
 print('generated SVG masters:',len(entries),'render plan:',tmp)

def package():
 source=ROOT/'ArtSource/Kairos/EtchedCards'
 for folder in source.iterdir():
  if not folder.is_dir():continue
  layers=sorted(folder.glob('0*.png'));require=len(layers)==5
  if not require:raise RuntimeError('missing layers '+str(folder))
  image=ET.Element('image',{'w':str(W),'h':str(H),'name':folder.name,'version':'0.0.3'});stack=ET.SubElement(image,'stack')
  for i,l in reversed(list(enumerate(layers))):ET.SubElement(stack,'layer',{'name':l.stem,'src':'data/'+l.name,'x':'0','y':'0','opacity':'1.0','visibility':'visible','composite-op':'svg:src-over'})
  with zipfile.ZipFile(folder/'editable.ora','w') as z:
   z.writestr('mimetype','image/openraster',compress_type=zipfile.ZIP_STORED);z.writestr('stack.xml',ET.tostring(image,encoding='utf-8'),compress_type=zipfile.ZIP_DEFLATED)
   for l in layers:z.write(l,'data/'+l.name,compress_type=zipfile.ZIP_DEFLATED)
   z.write(folder/'merged.png','mergedimage.png');z.write(folder/'thumbnail.png','Thumbnails/thumbnail.png')
 print('packaged layered ORA masters')

if __name__=='__main__':
 p=argparse.ArgumentParser();p.add_argument('--package',action='store_true');a=p.parse_args();package() if a.package else generate()
