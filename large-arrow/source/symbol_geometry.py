from PIL import Image,ImageDraw,ImageFont
import math,functools
import numpy as np
from scipy.ndimage import maximum_filter
BODY_MAX=120
AA=4
INK='#173e35';WHITE='#f7fff9';MINT='#78d9b3';GOLD='#e8c66a'

def symbol(role, frame, k=4):
 """Draw symbol at 4x, in a 64-unit design space."""
 im=Image.new('RGBA',(math.ceil(64*k),math.ceil(64*k))); d=ImageDraw.Draw(im)
 def pts(p):return [(round(x*k),round(y*k)) for x,y in p]
 def line(p,fill=WHITE,width=2.5):
  d.line(pts(p),fill=INK,width=round((width+2)*k),joint='curve')
  d.line(pts(p),fill=fill,width=round(width*k),joint='curve')
 def poly(p,fill=WHITE):
  d.polygon(pts(p),fill=fill);d.line(pts(p+[p[0]]),fill=INK,width=max(1,round(1.5*k)),joint='curve')
 def ellipse(box,fill=WHITE):
  d.ellipse(tuple(round(a*k) for a in box),fill=fill,outline=INK,width=max(1,round(1.5*k)))
 def arrow(p):poly(p,MINT)
 hs=(4,4)
 if role in ('Arrow','Help','AppStarting'):
  poly([(4,4),(5,31),(12,24),(19,37),(25,33),(18,21),(29,20)])
  if role=='Help':
   ellipse((27,3,45,21),MINT)
   f=ImageFont.truetype('C:/Windows/Fonts/arialbd.ttf',max(1,round(15*k)))
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

def clean_alpha(im):
 im.putalpha(im.getchannel('A').point(lambda a:a if a>=8 else 0))
 return im

def native_symbol(role,size,phase,draw):
 # Determine proportional geometry from the original vector design, then rasterize
 # that geometry directly at four samples per final output pixel.
 prototype,semantic=draw(role,phase,4)
 box=prototype.getbbox();assert box
 design_scale=BODY_MAX/max(box[2]-box[0],box[3]-box[1])
 native_ratio=size/256
 k=4*design_scale*native_ratio*AA
 high,semantic=draw(role,phase,k)
 bounds=high.getbbox();assert bounds
 badge=high.crop(bounds)
 pad=max(1,round(6*native_ratio*AA))
 padded=Image.new('RGBA',(badge.width+pad*2,badge.height+pad*2));padded.alpha_composite(badge,(pad,pad))
 alpha=padded.getchannel('A')
 white_radius=max(1,round(6*native_ratio*AA));dark_radius=max(1,round(3*native_ratio*AA))
 white=Image.new('RGBA',padded.size,WHITE);white.putalpha(Image.fromarray(maximum_filter(np.asarray(alpha),size=white_radius*2+1,mode='constant',cval=0)))
 dark=Image.new('RGBA',padded.size,OUTER);dark.putalpha(Image.fromarray(maximum_filter(np.asarray(alpha),size=dark_radius*2+1,mode='constant',cval=0)))
 white.alpha_composite(dark);white.alpha_composite(padded)
 canvas=Image.new('RGBA',(size*AA,size*AA));origin=round(3*native_ratio*AA)
 assert origin+white.width<=size*AA and origin+white.height<=size*AA,'Symbol must fit without clipping'
 canvas.alpha_composite(white,(origin,origin))
 result=clean_alpha(canvas.resize((size,size),Image.Resampling.LANCZOS))
 hot=tuple(round((value*k-bounds[axis]+pad+origin)/AA) for axis,value in enumerate(semantic))
 assert all(0<=n<size for n in hot)
 return result,hot
