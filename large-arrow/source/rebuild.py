"""Canonical release renderer: explicit character palettes and unchanged character pixels."""
from pathlib import Path
import json,sys,math,struct,hashlib,functools
import numpy as np
from PIL import Image
from scipy.ndimage import distance_transform_edt
P=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(P/'source'))
import art_renderer as art
PROFILE=json.loads((P/'source/package.json').read_text(encoding='utf-8'))
PALETTES=json.loads((P/'source/character-palettes.json').read_text(encoding='utf-8'))
PLUS=PROFILE['edition']=='large-arrow'
SIZES=[32,48,64,72,96,128,256] if PLUS else [32,48,64,96,128,256]
ROLES=(art.codec.ROLES if hasattr(art,'codec') else art.ROLES)
MANIFEST=P/'source/cursor_manifest.json'
BASELINE=json.loads((P/'source/baseline-manifest.json').read_text(encoding='utf-8'))
REF={(e['character'],e['role']):e for e in BASELINE if e['size']==48}
if PLUS:
 import legacy_geometry as legacy
 geometry=legacy.geometry
 OLD_LAYOUT=json.loads((P/'source/layout-legacy.json').read_text(encoding='utf-8'))
 LAYOUT_PATH=P/'source/layout-same-character.json'
 LAYOUT=json.loads(LAYOUT_PATH.read_text(encoding='utf-8')) if LAYOUT_PATH.is_file() else {}
else:LAYOUT={}

def pixels_for(size):return int(math.ceil(size*4/3/32)*32) if PLUS else size
def chunk(kind,data):return kind+struct.pack('<I',len(data))+data+(b'\0' if len(data)%2 else b'')
def cursor_bytes(image,hotspot):
 w,h=image.size;assert 1<=w<=512 and 1<=h<=512
 rgba=np.asarray(image);pixels=rgba[::-1,:,[2,1,0,3]].tobytes();stride=(w+31)//32*4
 mask=np.zeros((h,stride),np.uint8);bits=np.packbits((rgba[::-1,:,3]==0).astype(np.uint8),axis=1);mask[:,:bits.shape[1]]=bits
 dib=struct.pack('<IiiHHIIiiII',40,w,h*2,1,32,0,len(pixels),0,0,0,0)+pixels+mask.tobytes()
 return struct.pack('<3H',0,2,1)+struct.pack('<4B2H2I',w if w<256 else 0,h if h<256 else 0,0,0,*hotspot,len(dib),22)+dib
def ani_bytes(images,hot,jiffies):
 header=struct.pack('<9I',36,len(images),len(images),0,0,32,1,jiffies,1)
 body=b'ACON'+chunk(b'anih',header)+chunk(b'LIST',b'fram'+b''.join(chunk(b'icon',cursor_bytes(im,hot)) for im in images))
 return b'RIFF'+struct.pack('<I',len(body))+body

@functools.lru_cache(None)
def character(cid,role,phase,size):
 if hasattr(art,'ROLE_POSE'):canvas=art.action_frame(cid,art.ROLE_POSE[role],phase)
 else:
  source=art.action_frame(cid,role,phase);canvas=Image.new('RGBA',(256,256));canvas.alpha_composite(source,((256-source.width)//2,248-source.height))
 return canvas.resize((size,size),Image.Resampling.LANCZOS)

def set_palette(cid):
 palette=PALETTES[cid]
 target=art.codec if hasattr(art,'codec') else art
 for key in ('INK','WHITE','MINT','GOLD'):setattr(target,key,palette[key])
 if hasattr(art,'PROFILE'):art.PROFILE['accent']=palette['MINT']
 if PLUS:
  for key in ('INK','WHITE','MINT','GOLD'):setattr(geometry,key,palette[key])
  geometry.OUTER=palette['OUTER']
  legacy.scaled_symbol.cache_clear()

def solve_placement(cid,role,size,chars,symbols,target_gaps):
 canvas=pixels_for(size)
 coords=[np.nonzero(np.asarray(im)[:,:,3]>0) for im in chars]
 distances=[distance_transform_edt(~(np.asarray(im)[:,:,3]>0)) for im in symbols]
 boxes=[im.getbbox() for im in chars]
 xmax=canvas-max(b[2] for b in boxes);ymax=canvas-max(b[3] for b in boxes)
 frozen=LAYOUT.setdefault(cid,{}).setdefault(role,{})
 def evaluate(x,y):return [float(d[c[0]+y,c[1]+x].min()) for d,c in zip(distances,coords)]
 if str(size) in frozen:
  offsets=[tuple(x) for x in frozen[str(size)]['offsets']]
  gaps=[float(d[c[0]+y,c[1]+x].min()) for d,c,(x,y) in zip(distances,coords,offsets)]
 else:
  if size==96 or '96' not in frozen:
   candidates=[(x,y) for x in range(0,xmax+1) for y in range(0,ymax+1)]
  else:
   bx,by=frozen['96']['offsets'][0];cx,cy=round(bx*size/96),round(by*size/96)
   candidates=[(x,y) for x in range(max(0,cx-5),min(xmax,cx+5)+1) for y in range(max(0,cy-5),min(ymax,cy+5)+1)]
  scored=[]
  for x,y in candidates:
   gaps=evaluate(x,y)
   if min(gaps)<=0:continue
   errors=[abs(a-b) for a,b in zip(gaps,target_gaps)]
   scored.append((max(errors),sum(errors),x*x+y*y,y,x,gaps))
  assert scored,(cid,role,size,'no safe origin')
  best=min(scored);_,_,_,y,x,gaps=best;offsets=[(x,y)]*len(chars)
  if best[0]>1:
   # Small per-phase translation keeps the requested contour gap, without
   # changing any character pixels or scaling its animation artwork.
   corrected=[]
   for i,(d,c,target) in enumerate(zip(distances,coords,target_gaps)):
    alternatives=[]
    for ax in range(max(0,x-4),min(xmax,x+4)+1):
     for ay in range(max(0,y-4),min(ymax,y+4)+1):
      gap=float(d[c[0]+ay,c[1]+ax].min())
      if gap>0:alternatives.append((abs(gap-target),(ax-x)**2+(ay-y)**2,ay,ax,gap))
    assert alternatives
    choice=min(alternatives)
    if choice[0]>1:
     # A new silhouette may need a larger translation than a scaled96 origin.
     # Search outward without changing the character or loosening the gap bound.
     for radius in range(5,max(xmax,ymax)+1):
      ring=set()
      for step in range(-radius,radius+1):
       ring.update([(x-radius,y+step),(x+radius,y+step),(x+step,y-radius),(x+step,y+radius)])
      for ax,ay in ring:
       if not(0<=ax<=xmax and 0<=ay<=ymax):continue
       gap=float(d[c[0]+ay,c[1]+ax].min())
       if gap>0:alternatives.append((abs(gap-target),(ax-x)**2+(ay-y)**2,ay,ax,gap))
      choice=min(alternatives)
      if choice[0]<=1:break
    err,_,ay,ax,gap=choice;assert err<=1,(cid,role,size,i,'gap error',err)
    corrected.append((ax,ay));gaps[i]=gap
   offsets=corrected
  frozen[str(size)]={'offsets':[list(p) for p in offsets],'render_size':canvas,'reference_gaps':target_gaps,'gaps':gaps}
 error=max(abs(a-b) for a,b in zip(gaps,target_gaps));assert error<=1,(cid,role,size,error)
 return offsets,gaps,error

def main():
 entries=[];checks=[]
 for info in PROFILE['characters']:
  cid=info['id'];set_palette(cid)
  for role,label in ROLES:
   ref=REF[cid,role];count=ref['frames']
   # Solve 96 first so all other sizes start from its proportional placement.
   for size in ([96]+[n for n in SIZES if n!=96] if PLUS else SIZES):
    canvas=pixels_for(size);images=[];hots=[]
    chars=[character(cid,role,i,size) for i in range(count)]
    if PLUS:
     symbols=[];targets=[]
     for phase in range(count):
      badge,hot=legacy.scaled_symbol(role,size,phase);sx=sy=math.floor(2*size/72)
      symbol=Image.new('RGBA',(canvas,canvas));symbol.paste(badge,(sx,sy));symbols.append(symbol);hots.append((hot[0]+sx,hot[1]+sy))
      tile=round(48*size/72);old=legacy.character_layer(cid,role,phase,tile)
      cx,cy=[math.ceil(v*size/72) for v in OLD_LAYOUT['character_origins'][cid][role]]
      oc=Image.new('RGBA',(canvas,canvas));oc.paste(old,(cx,cy))
      targets.append(float(distance_transform_edt(~(np.asarray(symbol)[:,:,3]>0))[np.asarray(oc)[:,:,3]>0].min()))
     offsets,gaps,error=solve_placement(cid,role,size,chars,symbols,targets)
     for phase,(char,symbol,(dx,dy)) in enumerate(zip(chars,symbols,offsets)):
      char_layer=Image.new('RGBA',(canvas,canvas));char_layer.paste(char,(dx,dy))
      ca=np.asarray(char_layer);sa=np.asarray(symbol);cm=ca[:,:,3]>0;sm=sa[:,:,3]>0
      assert not np.any(cm&sm),(cid,role,size,phase,'overlap')
      assert int(ca[:,:,3].sum())==int(np.asarray(char)[:,:,3].sum()),(cid,role,size,phase,'clipping')
      result=ca.copy();result[sm]=sa[sm];image=Image.fromarray(result)
      mask=np.asarray(char)[:,:,3]>0
      assert np.array_equal(np.asarray(image.crop((dx,dy,dx+size,dy+size)))[mask],np.asarray(char)[mask])
      checks.append({'character':cid,'role':role,'size':size,'phase':phase,'render_size':canvas,'offset':[dx,dy],'characterPixelsIdentical':True,
                     'character_pixel_sha256':hashlib.sha256(np.asarray(char)[mask].tobytes()).hexdigest(),'character_bounds':char.getbbox(),'noOverlap':True,'noClipping':True,'gap':gaps[phase],'reference_gap':targets[phase],'gapError':abs(gaps[phase]-targets[phase])})
      images.append(image)
    else:
     for phase in range(count):
      image,hot=art.compile_frame(cid,role,size,phase);images.append(image);hots.append(tuple(hot))
    assert len(set(hots))==1,(cid,role,size,'hotspot changes')
    for phase,image in enumerate(images):
     out=P/f'source/frames/{cid}/{size}/{role}-{phase:02}.png';out.parent.mkdir(parents=True,exist_ok=True);image.save(out)
    out=P/f'cursors/{cid}/{size}/{role}.ani';out.parent.mkdir(parents=True,exist_ok=True);encoded=ani_bytes(images,hots[0],ref['jiffies']);out.write_bytes(encoded)
    entry=dict(ref,size=size,file=out.relative_to(P).as_posix(),hotspot=list(hots[0]),render_size=canvas,character_reference_size=size,sha256=hashlib.sha256(encoded).hexdigest())
    entry.pop('character_tile',None);entries.append(entry)
   print(cid,role,'Plus' if PLUS else 'original','complete',flush=True)
  character.cache_clear()
  if PLUS:legacy.character_layer.cache_clear()
 if PLUS:
  LAYOUT_PATH.write_text(json.dumps(LAYOUT,ensure_ascii=False,indent=2),encoding='utf-8')
  (P/'source/character-layout-validation.json').write_text(json.dumps(checks,ensure_ascii=False,indent=2),encoding='utf-8')
 MANIFEST.write_text(json.dumps(entries,ensure_ascii=False,indent=2),encoding='utf-8')
 (P/'source/render-recipe.json').write_text(json.dumps({'version':'1.2.0','sizes':SIZES,'render_sizes':{n:pixels_for(n) for n in SIZES},'same_character_pixels_in_plus':True,'palette_source':'character-palettes.json','direct_original_sources':True,'edition':PROFILE['edition']},indent=2),encoding='utf-8')
 print(json.dumps({'characters':len(PROFILE['characters']),'ani':len(entries),'frames':sum(e['frames'] for e in entries),'edition':PROFILE['edition']}),flush=True)
if __name__=='__main__':main()
