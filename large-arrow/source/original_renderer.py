"""Compile existing attributed community DeepSeek artwork into editable Windows cursors."""
from pathlib import Path
import base64, hashlib, io, json, math, shutil, struct, zipfile
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]
REPO = ROOT if Path(__file__).parent.name == 'source' else ROOT / 'public' / 'DeepSeek-mint-cursors'
SIZES = (32, 48, 64, 128, 256)
ROLES = [
 ('Arrow','正常选择'),('Help','帮助选择'),('AppStarting','后台运行'),
 ('Wait','忙碌'),('Crosshair','精确选择'),('IBeam','文本选择'),
 ('NWPen','手写'),('No','不可用'),('SizeNS','垂直调整'),
 ('SizeWE','水平调整'),('SizeNWSE','对角调整1'),('SizeNESW','对角调整2'),
 ('SizeAll','移动'),('UpArrow','候选选择'),('Hand','链接选择'),
 ('Pin','位置选择'),('Person','人员选择')]
NAMES = {'deepseek':'DeepSeek 小鲸鱼'}
ACTION_ROWS={'idle':(0,6),'running-right':(1,8),'running-left':(2,8),'waving':(3,4),'jumping':(4,5),'failed':(5,8),'waiting':(6,6),'working':(7,6),'review':(8,6)}
ROLE_ACTION={'Arrow':'idle','Help':'review','AppStarting':'waiting','Wait':'working','Crosshair':'review','IBeam':'working','NWPen':'review','No':'failed','SizeNS':'jumping','SizeWE':'running-right','SizeNWSE':'running-right','SizeNESW':'running-left','SizeAll':'running-right','UpArrow':'jumping','Hand':'waving','Pin':'waiting','Person':'waving'}
ACTION_CACHE={}
COMPACT_ACTIONS=True
INK='#182844'; WHITE='#f7fbff'; MINT='#6aa4f1'; GOLD='#e8c66a'

def save_json(path, value):
 path.parent.mkdir(parents=True,exist_ok=True)
 path.write_text(json.dumps(value,ensure_ascii=False,indent=2),encoding='utf-8')

def cursor_bytes(image, hotspot):
 """32-bit bottom-up BGRA DIB plus DWORD-aligned 1-bit AND mask."""
 w,h=image.size; assert 1 <= w <= 256 and 1 <= h <= 256
 rgba=image.tobytes(); pixels=bytearray(); stride=((w+31)//32)*4
 mask=bytearray(stride*h)
 for out_y,y in enumerate(range(h-1,-1,-1)):
  for x in range(w):
   r,g,b,a=rgba[(y*w+x)*4:(y*w+x+1)*4]
   pixels.extend((b,g,r,a))
   if a==0:mask[out_y*stride+x//8] |= 1 << (7-x%8)
 dib=struct.pack('<IiiHHIIiiII',40,w,h*2,1,32,0,len(pixels),0,0,0,0)+pixels+mask
 return struct.pack('<HHH',0,2,1)+struct.pack('<BBBBHHII',w % 256,h % 256,0,0,*hotspot,len(dib),22)+dib

def chunk(kind,data):
 return kind+struct.pack('<I',len(data))+data+(b'\0' if len(data)%2 else b'')

def ani_bytes(images,hotspot,jiffies=8):
 frames=b''.join(chunk(b'icon',cursor_bytes(im,hotspot)) for im in images)
 header=struct.pack('<9I',36,len(images),len(images),0,0,32,1,jiffies,1)
 body=b'ACON'+chunk(b'anih',header)+chunk(b'LIST',b'fram'+frames)
 return b'RIFF'+struct.pack('<I',len(body))+body

def symbol(role, frame):
 """Draw symbol at 4x, in a 64-unit design space."""
 k=4; im=Image.new('RGBA',(256,256)); d=ImageDraw.Draw(im)
 def pts(p):return [(round(x*k),round(y*k)) for x,y in p]
 def line(p,fill=WHITE,width=2.5):
  d.line(pts(p),fill=INK,width=round((width+2)*k),joint='curve')
  d.line(pts(p),fill=fill,width=round(width*k),joint='curve')
 def poly(p,fill=WHITE):
  d.polygon(pts(p),fill=fill);d.line(pts(p+[p[0]]),fill=INK,width=6,joint='curve')
 def ellipse(box,fill=WHITE):
  d.ellipse(tuple(round(a*k) for a in box),fill=fill,outline=INK,width=6)
 def arrow(p):poly(p,MINT)
 hs=(4,4)
 if role in ('Arrow','Help','AppStarting'):
  poly([(4,4),(5,31),(12,24),(19,37),(25,33),(18,21),(29,20)])
  if role=='Help':
   ellipse((27,3,45,21),MINT)
   f=ImageFont.truetype('C:/Windows/Fonts/arialbd.ttf',15*k)
   d.text((32*k,2*k),'?',font=f,fill=INK)
  if role=='AppStarting':
   ellipse((28,5,46,23),WHITE)
   for i in range(8):
    a=2*math.pi*(i+frame)/8; x=37+6*math.cos(a);y=14+6*math.sin(a)
    d.ellipse(((x-1.8)*k,(y-1.8)*k,(x+1.8)*k,(y+1.8)*k),fill=MINT if i<4 else INK)
 elif role=='Wait':
  hs=(19,19);ellipse((5,5,33,33),WHITE)
  a=2*math.pi*frame/8
  line([(19,19),(19+10*math.sin(a),19-10*math.cos(a))],MINT,3)
  line([(19,19),(12,16)],GOLD,2)
 elif role=='Crosshair':
  hs=(19,19);line([(19,3),(19,35)]);line([(3,19),(35,19)])
  ellipse((15,15,23,23),MINT)
 elif role=='IBeam':
  hs=(16,20);line([(16,4),(16,36)],MINT)
  line([(8,4),(24,4)]);line([(8,36),(24,36)])
 elif role=='NWPen':
  hs=(5,34);poly([(5,34),(9,23),(28,4),(35,11),(16,30)],MINT)
  poly([(5,34),(9,23),(16,30)],WHITE)
 elif role=='No':
  hs=(19,19);ellipse((4,4,34,34),WHITE);line([(9,9),(29,29)],'#ed8991',4)
 elif role.startswith('Size'):
  hs=(19,19)
  dirs={'SizeNS':[(0,-1),(0,1)],'SizeWE':[(-1,0),(1,0)],
   'SizeNWSE':[(-.707,-.707),(.707,.707)],'SizeNESW':[(.707,-.707),(-.707,.707)],
   'SizeAll':[(0,-1),(0,1),(-1,0),(1,0)]}[role]
  for dx,dy in dirs:
   tx=19+16*dx;ty=19+16*dy;nx=-dy;ny=dx
   line([(19,19),(tx,ty)],MINT,3)
   arrow([(tx,ty),(tx-7*dx+5*nx,ty-7*dy+5*ny),(tx-7*dx-5*nx,ty-7*dy-5*ny)])
 elif role=='UpArrow':
  hs=(19,4);poly([(19,4),(6,18),(14,18),(14,35),(24,35),(24,18),(32,18)],MINT)
 elif role=='Hand':
  hs=(14,4)
  poly([(10,31),(5,22),(6,18),(10,18),(12,22),(12,6),(14,4),(17,5),
   (18,17),(21,13),(24,14),(26,15),(30,15),(32,18),(34,20),(34,30),(29,37),(16,37)],WHITE)
  line([(21,17),(21,26)],MINT,1.2);line([(27,18),(27,26)],MINT,1.2)
 elif role=='Pin':
  hs=(19,35);ellipse((7,4,31,28),MINT)
  poly([(9,20),(19,35),(29,20)],MINT);ellipse((14,10,24,20),WHITE)
 elif role=='Person':
  hs=(19,10);ellipse((12,4,26,18),MINT)
  poly([(6,34),(8,26),(13,22),(25,22),(30,26),(32,34)],WHITE)
 return im,hs

ACTION_SPECS={
 'idle':('dense-idle.png',[0,4,8,12,16,20,22,23,24,26,28,31]),
 'running-right':('dense-walk.png',[0,4,8,12,16,20,24,28]),
 'running-left':('dense-walk.png',[0,4,8,12,16,20,24,28]),
 'waving':('dense-wave.png',[0,4,8,12,16,20,24,28]),
 'jumping':('dense-jump.png',[0,4,8,12,16,20,24,28]),
 'failed':('expressions.png',[11]*8),
 'waiting':('dense-idle.png',[8,12,16,20,24,26,28,31]),
 'working':('expressions.png',[6]*8),
 'review':('expressions.png',[4]*8)
}
def frame_count(role):
 return len(ACTION_SPECS[ROLE_ACTION[role]][1])

def action_group(action):
 if action not in ACTION_CACHE:
  compact=REPO/'source/compact-actions.json'
  if COMPACT_ACTIONS and compact.exists():
   info=json.loads(compact.read_text(encoding='utf-8'))['groups'][action]
   atlas=Image.open(REPO/'source/characters/deepseek-actions.png').convert('RGBA')
   ACTION_CACHE[action]=[atlas.crop((i*256,info['row']*256,i*256+info['width'],info['row']*256+info['height'])) for i in range(info['count'])]
   return ACTION_CACHE[action]
  name,indices=ACTION_SPECS[action]
  atlas=Image.open(REPO/'source/characters'/name).convert('RGBA')
  assert atlas.width==2048 and atlas.height in (2048,4096)
  tiles=[atlas.crop(((i%4)*512,(i//4)*512,(i%4+1)*512,(i//4+1)*512)) for i in indices]
  if action=='running-left':tiles=[im.transpose(Image.Transpose.FLIP_LEFT_RIGHT) for im in tiles]
  boxes=[im.getbbox() for im in tiles];assert all(boxes)
  bbox=(min(b[0] for b in boxes),min(b[1] for b in boxes),max(b[2] for b in boxes),max(b[3] for b in boxes))
  assert bbox[0]>0 and bbox[1]>0 and bbox[2]<512 and bbox[3]<512,(action,bbox)
  factor=min(240/(bbox[2]-bbox[0]),240/(bbox[3]-bbox[1]))
  size=(round((bbox[2]-bbox[0])*factor),round((bbox[3]-bbox[1])*factor))
  normalized=[im.crop(bbox).resize(size,Image.Resampling.LANCZOS) for im in tiles]
  if action in ('failed','working','review'):
   # Existing static community poses get a gentle, deterministic breathing offset.
   # No facial features or character design are redrawn.
   sequence=[]
   for i,im in enumerate(normalized):
    canvas=Image.new('RGBA',(im.width+4,im.height+4))
    canvas.alpha_composite(im,(2,2-round(math.sin(2*math.pi*i/8)*2)))
    sequence.append(canvas)
   normalized=sequence
  ACTION_CACHE[action]=normalized
 return ACTION_CACHE[action]

def action_frame(character,role,frame):
 assert character=='deepseek'
 group=action_group(ROLE_ACTION[role]);return group[frame%len(group)]

def compile_frame(character,role,size,frame=0):
 canvas=Image.new('RGBA',(256,256))
 # Full-body character dominates all five canvas sizes; no opaque pixels cropped.
 source=action_frame(character,role,frame)
 canvas.alpha_composite(source,((256-source.width)//2,248-source.height))
 icon,hs=symbol(role,frame)
 box=icon.getbbox();badge=icon.crop(box)
 factor=66/max(badge.size)
 badge=badge.resize((round(badge.width*factor),round(badge.height*factor)),Image.Resampling.LANCZOS)
 canvas.alpha_composite(badge,(3,3))
 result=canvas.resize((size,size),Image.Resampling.LANCZOS)
 hs=tuple(max(0,min(size-1,round(((value*4-box[axis])*factor+3)*size/256))) for axis,value in enumerate(hs))
 return result,hs


def main():
 entries=[]
 for size in SIZES:
  for role,label in ROLES:
   frames=[]
   for f in range(frame_count(role)):
    im,hs=compile_frame('deepseek',role,size,f);frames.append(im)
    p=REPO/'source/frames/deepseek'/str(size)/f'{role}-{f:02}.png'
    p.parent.mkdir(parents=True,exist_ok=True);im.save(p)
   jiffies=12 if role=='Arrow' else 7 if role in ('AppStarting','Wait') else 8
   p=REPO/'cursors/deepseek'/str(size)/f'{role}.ani';p.parent.mkdir(parents=True,exist_ok=True)
   p.write_bytes(ani_bytes(frames,hs,jiffies))
   entries.append({'character':'deepseek','size':size,'role':role,'label':label,'file':p.relative_to(REPO).as_posix(),'hotspot':hs,'frames':len(frames),'jiffies':jiffies,'frame_ms':jiffies*1000/60,'action':ROLE_ACTION[role]})
 save_json(REPO/'source/cursor_manifest.json',entries)
 save_json(REPO/'source/action-recipe.json',{'source_grid':512,'source_columns':4,'action_specs':ACTION_SPECS,'mirrored_action':'running-left','static_poses_breathing':['failed','working','review'],'character_design_unchanged':True,'normalization':'per-action shared transparent bounds and scale; shared anchor, no opaque pixels cropped','symbol_box_max':66,'character_box_max':240})
 P=REPO/'previews';P.mkdir(exist_ok=True)
 F=ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',22);small=ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',15)
 sheet=Image.new('RGB',(1440,500),'#142239');d=ImageDraw.Draw(sheet)
 d.text((30,20),'DeepSeek 小鲸鱼 · 17 种动态状态',font=F,fill='#deebff')
 d.text((30,60),'沿用CN社区小鲸鱼形象与动画 · 五档全身放大布局',font=small,fill='#a9c5ef')
 for i,(role,label) in enumerate(ROLES):
  x=28+(i%9)*156;y=110+(i//9)*160
  d.rounded_rectangle((x,y,x+144,y+140),12,fill='#eff5fc')
  im,_=compile_frame('deepseek',role,64);sheet.paste(im,(x+40,y+12),im)
  d.text((x+6,y+103),label,font=small,fill='#20395b')
 sheet.save(P/'cursor-overview.png')
 sizes=Image.new('RGB',(1920,820),'#142239');d=ImageDraw.Draw(sizes)
 d.text((40,22),'五档实际尺寸：32 / 48 / 64 / 128 / 256 px（未放大）',font=F,fill='#deebff')
 for i,size in enumerate(SIZES):
  x=30+i*380;d.rounded_rectangle((x,90,x+350,730),12,fill='#edf4fc')
  d.rectangle((x+10,410,x+340,715),fill='#142239')
  im,_=compile_frame('deepseek','Arrow',size)
  sizes.paste(im,(x+(350-size)//2,130+(256-size)//2),im)
  sizes.paste(im,(x+(350-size)//2,425+(256-size)//2),im)
  d.text((x+125,745),f'{size}px',font=F,fill='#deebff')
 sizes.save(P/'actual-sizes.png')
 cards=[]
 for f in range(32):
  card=Image.new('RGB',(700,180),'#142239');d=ImageDraw.Draw(card)
  for i,role in enumerate(('Arrow','Hand','SizeAll','SizeNS','Help','Wait','No')):
   im,_=compile_frame('deepseek',role,64,f);card.paste(im,(15+i*99,55),im)
   d.text((14+i*99,15),dict(ROLES)[role],font=small,fill='#deebff')
  cards.append(card)
 cards[0].save(P/'animation.gif',save_all=True,append_images=cards[1:],duration=120,loop=0,disposal=2)
 print(json.dumps({'cursor_files':len(entries),'frames':sum(e['frames'] for e in entries),'sizes':list(SIZES)},ensure_ascii=False))

if __name__=='__main__':main()
