"""Validate cursor binary data and load/draw every cursor with Windows User32."""
from pathlib import Path
import ctypes as C
from ctypes import wintypes as W
import hashlib,json,struct,sys,tempfile
from PIL import Image,ImageDraw,ImageFont,ImageChops
ROOT=Path(__file__).resolve().parents[1]
REPO=ROOT if (ROOT/'cursors').exists() else ROOT/'public'/'astra-sol-mint-cursors'

def parse_cur(raw,expected):
 assert struct.unpack_from('<HHH',raw)==(0,2,1)
 w,h,colors,res,hx,hy,length,offset=struct.unpack_from('<BBBBHHII',raw,6)
 w,h=w or 256,h or 256
 assert (w,h)==(expected['size'],)*2
 assert [hx,hy]==expected['hotspot'] and offset==22 and offset+length==len(raw)
 header=struct.unpack_from('<IiiHHIIiiII',raw,offset)
 assert header[:6]==(40,w,h*2,1,32,0)
 stride=((w+31)//32)*4
 assert length==40+w*h*4+stride*h
 pixels=raw[offset+40:offset+40+w*h*4]
 image=Image.frombytes('RGBA',(w,h),pixels,'raw','BGRA',0,-1)
 assert image.getbbox() is not None
 return image

def chunks(raw,start=0):
 at=start
 while at<len(raw):
  kind,length=struct.unpack_from('<4sI',raw,at);end=at+8+length
  assert end<=len(raw);yield kind,raw[at+8:end];at=end+(length%2)
 assert at==len(raw)

def frames(raw,entry):
 if entry['frames']==1:return [parse_cur(raw,entry)]
 assert raw[:4]==b'RIFF' and raw[8:12]==b'ACON' and struct.unpack_from('<I',raw,4)[0]==len(raw)-8
 out=[];header=None
 for kind,data in chunks(raw,12):
  if kind==b'anih':header=struct.unpack('<9I',data)
  if kind==b'LIST' and data[:4]==b'fram':
   for k,icon in chunks(data,4):
    assert k==b'icon';out.append(parse_cur(icon,entry))
 assert header==(36,entry['frames'],entry['frames'],0,0,32,1,entry.get('jiffies',6),1)
 assert len(out)==entry['frames']
 assert len({im.tobytes() for im in out})>=3
 return out

class ICONINFO(C.Structure):
 _fields_=[('fIcon',W.BOOL),('xHotspot',W.DWORD),('yHotspot',W.DWORD),('hbmMask',W.HBITMAP),('hbmColor',W.HBITMAP)]
class BMIHEADER(C.Structure):
 _fields_=[('size',W.DWORD),('width',W.LONG),('height',W.LONG),('planes',W.WORD),('bits',W.WORD),('compression',W.DWORD),('sizeImage',W.DWORD),('xppm',W.LONG),('yppm',W.LONG),('colorsUsed',W.DWORD),('colorsImportant',W.DWORD)]
class BMI(C.Structure):_fields_=[('header',BMIHEADER),('colors',W.DWORD*3)]

def windows_api():
 u=C.WinDLL('user32',use_last_error=True);g=C.WinDLL('gdi32',use_last_error=True)
 u.LoadImageW.argtypes=[W.HINSTANCE,W.LPCWSTR,W.UINT,C.c_int,C.c_int,W.UINT];u.LoadImageW.restype=W.HANDLE
 u.GetIconInfo.argtypes=[W.HANDLE,C.POINTER(ICONINFO)];u.GetIconInfo.restype=W.BOOL
 u.DrawIconEx.argtypes=[W.HDC,C.c_int,C.c_int,W.HANDLE,C.c_int,C.c_int,W.UINT,W.HBRUSH,W.UINT];u.DrawIconEx.restype=W.BOOL
 u.DestroyCursor.argtypes=[W.HANDLE];u.DestroyCursor.restype=W.BOOL
 g.CreateCompatibleDC.argtypes=[W.HDC];g.CreateCompatibleDC.restype=W.HDC
 g.CreateDIBSection.argtypes=[W.HDC,C.POINTER(BMI),W.UINT,C.POINTER(C.c_void_p),W.HANDLE,W.DWORD];g.CreateDIBSection.restype=W.HBITMAP
 g.SelectObject.argtypes=[W.HDC,W.HANDLE];g.SelectObject.restype=W.HANDLE
 g.DeleteObject.argtypes=[W.HANDLE];g.DeleteObject.restype=W.BOOL
 g.DeleteDC.argtypes=[W.HDC];g.DeleteDC.restype=W.BOOL
 g.GdiFlush.restype=W.BOOL
 return u,g


def load_native_cursor(u,raw,size):
 u.CreateIconFromResourceEx.argtypes=[C.c_void_p,W.DWORD,W.BOOL,W.DWORD,C.c_int,C.c_int,W.UINT]
 u.CreateIconFromResourceEx.restype=W.HANDLE
 if raw[:4]==b'RIFF':bits=raw
 else:
  hx,hy,length,offset=struct.unpack_from('<HHII',raw,10)
  bits=struct.pack('<HH',hx,hy)+raw[offset:offset+length]
 buffer=C.create_string_buffer(bits)
 return u.CreateIconFromResourceEx(buffer,len(bits),False,0x00030000,size,size,0)

def draw_native(u,g,handle,size,step,bg):
 dc=g.CreateCompatibleDC(None);assert dc
 bits=C.c_void_p();bmi=BMI();bmi.header=BMIHEADER(40,size,-size,1,32,0,size*size*4,0,0,0,0)
 bitmap=g.CreateDIBSection(dc,C.byref(bmi),0,C.byref(bits),None,0);assert bitmap
 prior=g.SelectObject(dc,bitmap)
 try:
  r,bg_g,b=bg;raw=bytes((b,bg_g,r,255))*size*size;C.memmove(bits,raw,len(raw))
  assert u.DrawIconEx(dc,0,0,handle,size,size,step,None,3),C.get_last_error()
  g.GdiFlush();result=C.string_at(bits,size*size*4)
  return Image.frombytes('RGBA',(size,size),result,'raw','BGRA').convert('RGB')
 finally:g.SelectObject(dc,prior);g.DeleteObject(bitmap);g.DeleteDC(dc)

def main():
 entries=json.loads((REPO/'source'/'cursor_manifest.json').read_text(encoding='utf-8'))
 u,g=windows_api() if sys.platform=='win32' else (None,None)
 records=[];native=[]
 reference_dir=tempfile.TemporaryDirectory(prefix="astra-cursor-native-")
 reference_file=Path(reference_dir.name)/"frame.cur"
 for e in entries:
  path=REPO/e['file'];raw=path.read_bytes();decoded=frames(raw,e)
  icons=[icon for kind,data in chunks(raw,12) if kind==b'LIST' and data[:4]==b'fram' for k,icon in chunks(data,4)]
  handle=load_native_cursor(u,raw,e['size']) if u else None
  if u:assert handle,f'Windows load failed: {path}, error {C.get_last_error()}'
  try:
   renders=[]
   if u:
    info=ICONINFO();assert u.GetIconInfo(handle,C.byref(info))
    try:assert info.fIcon==0 and [info.xHotspot,info.yHotspot]==e['hotspot']
    finally:
     if info.hbmMask:g.DeleteObject(info.hbmMask)
     if info.hbmColor:g.DeleteObject(info.hbmColor)
    for f in range(e['frames']):
     render=draw_native(u,g,handle,e['size'],f,(239,248,242));renders.append(render)
     png=Image.open(REPO/f'source/frames/{e["character"]}/{e["size"]}/{e["role"]}-{f:02}.png').convert('RGBA')
     assert png.tobytes()==decoded[f].tobytes(),'Source PNG and ANI pixels differ'

     expected_source=Image.alpha_composite(Image.new('RGBA',png.size,(239,248,242,255)),png).convert('RGB')
     delta=ImageChops.difference(render,expected_source)
     assert max(v for channel in delta.getextrema() for v in channel)<=1,'Native color does not reproduce source PNG'

     reference_file.write_bytes(icons[f])
     reference=load_native_cursor(u,icons[f],e['size']);assert reference
     try:
      expected=draw_native(u,g,reference,e['size'],0,(239,248,242))
      assert render.tobytes()==expected.tobytes(),'Native ANI frame differs from native CUR reference'
      dark=draw_native(u,g,handle,e['size'],f,(21,39,33))
      assert dark.tobytes()==draw_native(u,g,reference,e['size'],0,(21,39,33)).tobytes()
     finally:u.DestroyCursor(reference)
    if e['frames']>1:assert len({im.tobytes() for im in renders})>=3
    if e['size']==64:native.append((e, renders[0],draw_native(u,g,handle,64,0,(21,39,33))))
   records.append({'file':e['file'],'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),'hotspot':e['hotspot'],'frames':e['frames'],'native_loaded':bool(handle),'native_draw_steps':len(renders)})
  finally:
   if handle:u.DestroyCursor(handle)
 if native:
  sheet=Image.new('RGB',(1440,680),'#152721');d=ImageDraw.Draw(sheet)
  font=ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',20)
  d.text((24,20),'Windows 原生加载与绘制 · 明暗背景 / 64 px',font=font,fill='#daf9e8')
  for i,(e,light,dark) in enumerate(native):
   x=20+(i%9)*157;y=80+(i//9)*146
   sheet.paste(light,(x,y));sheet.paste(dark,(x+70,y))
   d.text((x,y+78),e['character']+' '+e['role'],font=ImageFont.truetype('C:/Windows/Fonts/arial.ttf',13),fill='#d6eee0')
  sheet.save(REPO/'previews'/'windows-native-review.png')
 reference_dir.cleanup()
 output={'cursor_files':len(entries),'schemes':len({(e['character'],e['size']) for e in entries}),'role_count':17,'native_verified':u is not None,'records':records}
 (REPO/'validation.json').write_text(json.dumps(output,ensure_ascii=False,indent=2),encoding='utf-8')
 print(json.dumps({k:v for k,v in output.items() if k!='records'}))
if __name__=='__main__':main()
