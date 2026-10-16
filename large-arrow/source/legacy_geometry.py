"""Build a separate LargeArrow edition from each repository's original character sources."""
from pathlib import Path
import functools,json,math
import numpy as np
from scipy.ndimage import maximum_filter
from PIL import Image
import original_renderer as original
import symbol_geometry as geometry
P=Path(__file__).resolve().parents[1]
PROFILE=json.loads((P/'source/package.json').read_text(encoding='utf-8'))
SIZES=[32,48,64,72,128,256]
geometry.MINT=getattr(original,'MINT',PROFILE['accent'])
symbol=geometry.symbol;native_symbol=geometry.native_symbol

@functools.lru_cache(None)
def character_layer(cid,role,phase,size):
 if hasattr(original,'ROLE_POSE'):
  canvas=original.action_frame(cid,original.ROLE_POSE[role],phase)
 else:
  source=original.action_frame(cid,role,phase);canvas=Image.new('RGBA',(256,256))
  canvas.alpha_composite(source,((256-source.width)//2,248-source.height))
 result=canvas.resize((size,size),Image.Resampling.LANCZOS)
 if size!=48:result.putalpha(result.getchannel('A').point(lambda a:a if a>=8 else 0))
 return result

@functools.lru_cache(None)
def scaled_symbol(role,size,phase):
 if size==72:return native_symbol(role,48,phase,symbol)
 aa=4;ratio=(size*48/72)/256
 prototype,_=symbol(role,phase,4);box=prototype.getbbox()
 k=4*(120/max(box[2]-box[0],box[3]-box[1]))*ratio*aa
 high,semantic=symbol(role,phase,k);bounds=high.getbbox();badge=high.crop(bounds)
 pad=max(1,round(6*ratio*aa))
 padded=Image.new('RGBA',(badge.width+2*pad,badge.height+2*pad));padded.alpha_composite(badge,(pad,pad))
 alpha=np.asarray(padded.getchannel('A'))
 white_radius=max(1,round(6*ratio*aa));dark_radius=max(1,round(3*ratio*aa))
 white=Image.new('RGBA',padded.size,geometry.WHITE);white.putalpha(Image.fromarray(maximum_filter(alpha,size=2*white_radius+1,mode='constant',cval=0)))
 dark=Image.new('RGBA',padded.size,geometry.OUTER);dark.putalpha(Image.fromarray(maximum_filter(alpha,size=2*dark_radius+1,mode='constant',cval=0)))
 white.alpha_composite(dark);white.alpha_composite(padded)
 high_canvas=Image.new('RGBA',(size*aa,size*aa));origin=round(3*ratio*aa)
 high_canvas.alpha_composite(white,(origin,origin))
 result=high_canvas.resize((size,size),Image.Resampling.LANCZOS)
 result.putalpha(result.getchannel('A').point(lambda a:a if a>=8 else 0))
 hot=tuple(round((value*k-bounds[axis]+pad+origin)/aa) for axis,value in enumerate(semantic))
 return result,hot

def layers(cid,role,phase,size,origin):
 ratio=size/72;tile=round(48*ratio)
 char=character_layer(cid,role,phase,tile);badge,hot=scaled_symbol(role,size,phase)
 cx,cy=[math.ceil(v*ratio) for v in origin];sx=sy=math.floor(2*ratio)
 c=Image.new('RGBA',(size,size));s=c.copy();c.paste(char,(cx,cy));s.paste(badge,(sx,sy))
 ca=np.asarray(c)[:,:,3];sa=np.asarray(s)[:,:,3]
 assert int(ca.sum())==int(np.asarray(char)[:,:,3].sum()),(cid,role,size,'character clipped')
 assert int(sa.sum())==int(np.asarray(badge)[:,:,3].sum()),(cid,role,size,'symbol clipped')
 assert not np.any((ca>0)&(sa>0)),(cid,role,size,phase,'nonzero alpha overlap')
 array=np.zeros((size,size,4),dtype=np.uint8);array[sa>0]=np.asarray(s)[sa>0];array[ca>0]=np.asarray(c)[ca>0]
 return Image.fromarray(array),(hot[0]+sx,hot[1]+sy),char

def choose_origin(cid,role,count,reference):
 # Search only once on the base layout, then freeze it across every phase/size.
 # Astra's already approved origin is mandatory and must never be moved.
 candidates=[reference] if cid=='astra' else sorted([(x,y) for x in range(25) for y in range(25)],key=lambda p:((p[0]-reference[0])**2+(p[1]-reference[1])**2,abs(p[1]-reference[1]),p))
 for candidate in candidates:
  try:
   for n in [72,32,48,64,128,256]:
    for phase in range(count):layers(cid,role,phase,n,candidate)
   return list(candidate)
  except AssertionError:pass
 raise AssertionError((cid,role,'no unoverlapped proportional placement'))

def main():
 baseline=json.loads((P/'source/original_manifest.json').read_text(encoding='utf-8'))
 base=json.loads((P/'source/layout-base.json').read_text(encoding='utf-8'))
 frozen=P/'source/layout.json';layout=json.loads(frozen.read_text(encoding='utf-8')) if frozen.exists() else {'base_canvas':72,'symbol_origin':[2,2],'character_origins':{}}
 ref={(e['character'],e['role']):e for e in baseline if e['size']==48}
 entries=[];cases=[]
 codec=original.codec if hasattr(original,'codec') else original
 for ch in PROFILE['characters']:
  cid=ch['id'];origins=layout['character_origins'].setdefault(cid,{})
  for role in base['character_origins']:
   e=ref[cid,role];count=e['frames']
   if role not in origins:origins[role]=choose_origin(cid,role,count,base['character_origins'][role])
   for size in SIZES:
    images=[];hots=[]
    for phase in range(count):
     im,hot,char=layers(cid,role,phase,size,origins[role]);images.append(im);hots.append(hot)
     target=P/f'source/frames/{cid}/{size}/{role}-{phase:02}.png';target.parent.mkdir(parents=True,exist_ok=True);im.save(target)
     cases.append(dict(character=cid,role=role,size=size,phase=phase,character_origin=origins[role],disjoint=True,unclipped=True,original48_character_retained=(size==72)))
    assert len(set(hots))==1
    target=P/f'cursors/{cid}/{size}/{role}.ani';target.parent.mkdir(parents=True,exist_ok=True);target.write_bytes(codec.ani_bytes(images,hots[0],e['jiffies']))
    entries.append(dict(e,character=cid,size=size,file=target.relative_to(P).as_posix(),hotspot=list(hots[0]),character_tile=round(48*size/72)))
  print(cid,'six native sizes complete',flush=True)
 frozen.write_text(json.dumps(layout,ensure_ascii=False,indent=2),encoding='utf-8')
 (P/'source/cursor_manifest.json').write_text(json.dumps(entries,ensure_ascii=False,indent=2),encoding='utf-8')
 (P/'source/layout-validation.json').write_text(json.dumps(dict(all_frames_disjoint_and_unclipped=True,frozen_proportional_layout=True,cases=cases),ensure_ascii=False),encoding='utf-8')
 print(json.dumps(dict(ani=len(entries),png=len(cases))),flush=True)
if __name__=='__main__':main()
