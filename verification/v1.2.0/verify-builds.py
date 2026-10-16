"""Independent ANI decoding and original-character pixel comparisons."""
from pathlib import Path
import argparse,concurrent.futures,hashlib,json,os,struct,subprocess,sys
import numpy as np
from PIL import Image,ImageDraw,ImageFont,ImageColor
ROOT=Path(__file__).resolve().parents[1]
ROWS=json.loads((ROOT/'targets.json').read_text(encoding='utf-8'))
parser=argparse.ArgumentParser();parser.add_argument('--repo');args=parser.parse_args()
def chunks(raw):
 pos=0
 while pos<len(raw):
  kind=raw[pos:pos+4];n=struct.unpack_from('<I',raw,pos+4)[0];yield kind,raw[pos+8:pos+8+n];pos+=8+n+n%2
 assert pos==len(raw)
def frames(path):
 raw=path.read_bytes();assert raw[:4]==b'RIFF' and raw[8:12]==b'ACON' and len(raw)-8==struct.unpack_from('<I',raw,4)[0]
 parts=dict(chunks(raw[12:]));head=struct.unpack('<9I',parts[b'anih']);assert parts[b'LIST'][:4]==b'fram'
 result=[]
 for kind,cur in chunks(parts[b'LIST'][4:]):
  assert kind==b'icon' and struct.unpack_from('<3H',cur)==(0,2,1)
  _,_,_,_,hx,hy,n,offset=struct.unpack_from('<4B2H2I',cur,6)
  length,w,twice_h,planes,bits=struct.unpack_from('<IiiHH',cur,offset);h=twice_h//2
  assert (length,planes,bits)==(40,1,32) and len(cur)==offset+n
  im=Image.frombytes('RGBA',(w,h),cur[offset+40:offset+40+w*h*4],'raw','BGRA',0,-1)
  assert 0<=hx<w and 0<=hy<h;result.append((im,(hx,hy)))
 assert len(result)==head[1]==head[2]
 return result,head[7]

def worker(name):
 row=next(r for r in ROWS if r['repository']==name);repo=Path(row['checkout'])
 sys.path.insert(0,str(repo/'source'));import art_renderer as art
 palettes=json.loads((repo/'source/character-palettes.json').read_text(encoding='utf-8'))
 layout=json.loads((repo/'large-arrow/source/layout-same-character.json').read_text(encoding='utf-8'))
 file_count=frame_count=char_count=0;gaps=[];source_files=0;palette_checks=[]
 for edition,p in [('original',repo),('plus',repo/'large-arrow')]:
  baseline=json.loads((ROOT/'source/raw-source-baseline.json').read_text(encoding='utf-8'))
  for e in baseline:
   if e['repository']!=name or e['edition']!=edition:continue
   target=p/e['path']
   raw=target.read_bytes() if e['kind']=='binary' else json.dumps(json.loads(target.read_text(encoding='utf-8-sig')),sort_keys=True,ensure_ascii=False,separators=(',',':')).encode('utf-8')
   assert hashlib.sha256(raw).hexdigest()==e['sha256'],target
   if e['kind']=='binary':source_files+=1
  manifest=json.loads((p/'source/cursor_manifest.json').read_text(encoding='utf-8'))
  expected=[32,48,64,96,128,256] if edition=='original' else [32,48,64,72,96,128,256]
  assert len(manifest)==len(row['profile']['characters'])*17*len(expected)
  assert sorted({e['size'] for e in manifest})==expected
  for e in manifest:
   cid=e['character'];role=e['role'];size=e['size'];images,jiffies=frames(p/e['file'])
   assert len(images)==e['frames'] and jiffies==e['jiffies']
   for phase,(im,hot) in enumerate(images):
    assert im.size==(e['render_size'],e['render_size']) and list(hot)==e['hotspot']
    png=Image.open(p/f'source/frames/{cid}/{size}/{role}-{phase:02}.png').convert('RGBA')
    assert im.tobytes()==png.tobytes(),(name,edition,cid,role,size,phase,'PNG/ANI mismatch')
    if edition=='plus':
     if hasattr(art,'ROLE_POSE'):base=art.action_frame(cid,art.ROLE_POSE[role],phase)
     else:
      source=art.action_frame(cid,role,phase);base=Image.new('RGBA',(256,256));base.alpha_composite(source,((256-source.width)//2,248-source.height))
     char=base.resize((size,size),Image.Resampling.LANCZOS)
     placement=layout[cid][role][str(size)];dx,dy=placement['offsets'][phase]
     mask=np.asarray(char)[:,:,3]>0;actual=np.asarray(im.crop((dx,dy,dx+size,dy+size)))
     assert np.array_equal(actual[mask],np.asarray(char)[mask]),(name,cid,role,size,phase,'Character changed')
     box=char.getbbox();assert 0<=dx+box[0]<dx+box[2]<=im.width and 0<=dy+box[1]<dy+box[3]<=im.height
     error=abs(placement['gaps'][phase]-placement['reference_gaps'][phase]);assert error<=1;gaps.append(error);char_count+=1
    frame_count+=1
   if role=='Arrow' and size in [96,256]:
    pixels=np.asarray(images[0][0]);region=pixels[:size//2,:size//2,:]
    target=np.array(ImageColor.getrgb(palettes[cid]['INK']))
    found=np.any((np.abs(region[:,:,:3].astype(int)-target).max(axis=2)<=4)&(region[:,:,3]>=240))
    assert found,(name,cid,size,edition,'Expected themed outline not in arrow pixels')
    palette_checks.append({'character':cid,'edition':edition,'size':size,'outline':palettes[cid]['INK'],'verified':bool(found)})
   file_count+=1
 result={'repository':name,'aniFiles':file_count,'decodedFrames':frame_count,'characterIdentityFrames':char_count,'maxGapError':max(gaps),'rawSourceFilesUnchanged':source_files,'paletteChecks':palette_checks,'success':True}
 print(json.dumps(result,ensure_ascii=False))
if args.repo:worker(args.repo)
else:
 results=[];(ROOT/'verification').mkdir(exist_ok=True)
 def run(row):
  r=subprocess.run([sys.executable,'-X','utf8',str(Path(__file__).resolve()),'--repo',row['repository']],capture_output=True,env=dict(os.environ,PYTHONDONTWRITEBYTECODE='1'))
  if r.returncode:raise RuntimeError(row['repository']+': '+r.stderr.decode('utf-8',errors='backslashreplace')[-1800:])
  result=json.loads(r.stdout.decode('utf-8'));(ROOT/'verification'/(row['repository']+'.json')).write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8');print(row['repository'],'verified',result['aniFiles'],'ANI',flush=True);return result
 with concurrent.futures.ThreadPoolExecutor(max_workers=3) as pool:results=list(pool.map(run,ROWS))
 receipt={'repositories':len(results),'aniFiles':sum(r['aniFiles'] for r in results),'decodedFrames':sum(r['decodedFrames'] for r in results),'characterIdentityFrames':sum(r['characterIdentityFrames'] for r in results),'maxGapError':max(r['maxGapError'] for r in results),'rawSourceFilesUnchanged':sum(r['rawSourceFilesUnchanged'] for r in results),'results':results}
 (ROOT/'build-verification.json').write_text(json.dumps(receipt,ensure_ascii=False,indent=2),encoding='utf-8')
 # One comparable sheet for independent visual review, with actual bitmap pixels.
 font=ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',18);small=ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',14)
 character_total=sum(len(r['profile']['characters']) for r in ROWS)
 sheet=Image.new('RGB',(1380,((character_total+2)//3)*230+65),'#eaf0f3');d=ImageDraw.Draw(sheet);d.text((20,15),f'{character_total}角色 · 原版96 / Plus96 · 人物不变，指针随角色配色',font=font,fill='#23334d')
 index=0
 for row in ROWS:
  repo=Path(row['checkout'])
  for c in row['profile']['characters']:
   cid=c['id'];x=20+(index%3)*460;y=65+(index//3)*230;index+=1
   d.text((x,y),c['display'],font=font,fill='#23334d')
   for i,(edition,p) in enumerate([('原版',repo),('Plus',repo/'large-arrow')]):
    im=frames(p/f'cursors/{cid}/96/Arrow.ani')[0][0][0];tile=Image.new('RGB',(136,145),'#d8e2db');tile.paste(im,(0,0),im);sheet.paste(tile,(x+i*145,y+32));d.text((x+i*145,y+180),edition,font=small,fill='#23334d')
   palette=json.loads((repo/'source/character-palettes.json').read_text(encoding='utf-8'))[cid]
   for i,key in enumerate(['INK','MINT','OUTER']):d.rectangle((x+300,y+35+i*40,x+324,y+60+i*40),fill=palette[key]);d.text((x+330,y+35+i*40),key,font=small,fill='#23334d')
 preview=ROOT/'preview';preview.mkdir(exist_ok=True);sheet.save(preview/'全系列角色_96px原版Plus与配色.png')
 print(json.dumps({k:v for k,v in receipt.items() if k!='results'},ensure_ascii=False))
