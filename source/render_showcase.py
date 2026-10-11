"""Editable single-character demo rendered from the distributed cursor frames."""
from pathlib import Path
import json,math,subprocess,importlib.util
from PIL import Image,ImageDraw,ImageFont
R=Path(__file__).resolve().parents[1];V=R/'source/video';V.mkdir(exist_ok=True)
spec=importlib.util.spec_from_file_location('builder',R/'source/build_cursors.py');builder=importlib.util.module_from_spec(spec);spec.loader.exec_module(builder)
entries=json.loads((R/'source/cursor_manifest.json').read_text(encoding='utf-8'));states=[e for e in entries if e['size']==64]
F={n:ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',n) for n in (20,24,28,32,40,48,58)}
DURATION=47;START=4;ROLE_SECONDS=2;END_ROLES=38
cache={};labels={'idle':'待机与眨眼','review':'认真思考','waiting':'耐心等待','working':'抱着小鲸鱼忙碌','failed':'小失落','jumping':'轻轻跳跃','waving':'挥手打招呼','running-right':'向右走动','running-left':'向左走动'}
def txt(d,p,s,n=28,c='#dfebff'):d.text(p,s,font=F[n],fill=c)
def cursor(role='Arrow',size=64,frame=0):
 e=next(e for e in entries if e['role']==role and e['size']==size);key=(role,size,frame%e['frames'])
 if key not in cache:cache[key]=Image.open(R/'source/frames/deepseek'/str(size)/f'{role}-{key[2]:02}.png').convert('RGBA')
 return cache[key]
def pose(im,role='Arrow',frame=0,box=(120,340,330,410)):
 p=builder.action_frame('deepseek',role,frame);factor=min(box[2]/p.width,box[3]/p.height)
 p=p.resize((round(p.width*factor),round(p.height*factor)),Image.Resampling.LANCZOS);im.paste(p,(box[0]+(box[2]-p.width)//2,box[1]+box[3]-p.height),p)
def render(t):
 im=Image.new('RGB',(1920,1080),'#142239');d=ImageDraw.Draw(im)
 d.rounded_rectangle((40,38,1880,1038),30,outline='#345074',width=2)
 txt(d,(95,72),'DEEPSEEK · LITTLE WHALE',24,'#91bafa');txt(d,(95,119),'小鲸鱼来陪你点点鼠标啦',48)
 txt(d,(95,195),'CN社区形象与动画 · 17种动态状态 · 五档尺寸',28,'#a9c5ef')
 d.line((95,255,1825,255),fill='#345074',width=2)
 if t<START:
  pose(im,box=(185,300,470,535));txt(d,(820,335),'熟悉的小鲸鱼，跟着光标出发',40)
  for i,line in enumerate(('原来的深蓝长发、女仆裙与鲸尾','待机眨眼 / 挥手 / 走动 / 跳跃','五档都尽量放大全身形象','INF右键安装 + 各尺寸应用CMD')):txt(d,(820,439+i*78),line,28,'#bed2f0')
 elif t<END_ROLES:
  local=t-START;i=min(16,int(local/ROLE_SECONDS));e=states[i];phase=local-i*ROLE_SECONDS;role=e['role'];f=int(phase*1000/e['frame_ms'])%e['frames']
  pose(im,role,f);txt(d,(105,840),e['label'],32);txt(d,(105,890),labels[e['action']],24,'#a9c5ef')
  d.rounded_rectangle((535,305,1810,835),23,fill='#f1f6fd');txt(d,(585,338),'今天一起完成一点小事',32,'#243d62')
  d.rounded_rectangle((590,426,1135,512),12,fill='#dce8f9');txt(d,(616,447),'在这里输入文字',28,'#456081')
  d.rounded_rectangle((590,574,852,655),12,fill='#3f70b5');txt(d,(615,596),'打开分享页面',28,'#ffffff')
  d.rounded_rectangle((925,574,1175,655),12,fill='#dce3ed');txt(d,(958,596),'暂不可用',28,'#7f8fa6')
  txt(d,(590,741),'位置 ●    人员 ●    帮助 ?',24,'#456081')
  wave=math.sin(phase*math.pi);dx=round(35*wave) if role=='SizeAll' else 0;dy=round(20*wave) if role=='SizeAll' else 0
  dw=round(45*wave) if role in ('SizeWE','SizeNWSE','SizeNESW') else 0;dh=round(35*wave) if role in ('SizeNS','SizeNWSE','SizeNESW') else 0
  x1,y1,x2,y2=1260+dx,440+dy,1690+dx+dw,726+dy+dh
  d.rounded_rectangle((x1,y1,x2,y2),14,outline='#7190b8',width=3);txt(d,(x1+30,y1+27),'可移动、调整的窗口',24,'#456081')
  for gy in range(y1+90,y2-20,32):d.line((x1+30,gy,x2-25,gy),fill='#d4e1f3')
  if role=='NWPen':d.line([(x1+75+j*13,y1+160+round(15*math.sin(j*.5))) for j in range(max(2,int(phase*11)))],fill='#4e83c7',width=4)
  targets={'Arrow':(800,389),'Help':(930,755),'AppStarting':(720,612),'Wait':(1470,579),'Crosshair':(1470,579),'IBeam':(900,470),'NWPen':(x1+75+phase*120,y1+160),'No':(1045,612),'SizeNS':((x1+x2)//2,y2),'SizeWE':(x2,(y1+y2)//2),'SizeNWSE':(x2,y2),'SizeNESW':(x1,y2),'SizeAll':((x1+x2)//2,(y1+y2)//2),'UpArrow':(1180,420),'Hand':(720,612),'Pin':(666,755),'Person':(797,755)}
  prev=targets[states[max(0,i-1)]['role']];target=targets[role];ease=min(1,phase/.5);ease=ease*ease*(3-2*ease)
  px=prev[0]+(target[0]-prev[0])*ease;py=prev[1]+(target[1]-prev[1])*ease
  a=cursor(role,64,f).resize((128,128),Image.Resampling.NEAREST);hx,hy=e['hotspot'];im.paste(a,(round(px-hx*2),round(py-hy*2)),a)
  if phase>1 and role in ('Arrow','Hand'):
   rr=15+round((phase-1)*28);d.ellipse((px-rr,py-rr,px+rr,py+rr),outline='#70a6ed',width=3)
  txt(d,(580,876),f'{i+1:02}/17  {e["label"]}  ·  光标运动模拟 / 演示放大2倍',28,'#bed2f0')
 elif t<43:
  txt(d,(100,294),'按实际像素展示五档尺寸',40)
  for i,size in enumerate(builder.SIZES):
   x=110+i*347;d.rounded_rectangle((x,400,x+320,735),14,fill='#eff5fc');a=cursor(size=size);im.paste(a,(x+(320-size)//2,425+(256-size)//2),a);txt(d,(x+100,770),f'{size}px',32)
  txt(d,(450,880),'五档都减少留白、缩小符号，保留完整动作',28,'#bed2f0')
 else:
  pose(im,box=(175,335,390,440));txt(d,(730,337),'完整包与源码已整理到GitHub',40)
  txt(d,(734,447),'安装一档后，手动开启「尺寸保持」',32,'#bed2f0');txt(d,(734,518),'换大小：双击对应尺寸应用CMD',32,'#bed2f0')
  txt(d,(734,606),'DeepSeek-mint-cursors',40,'#91bafa');txt(d,(735,678),'完整下载地址见简介与评论使用说明',28,'#a9c5ef')
 txt(d,(95,974),'角色素材：YunYueSama / codex-deepseek-pet',20,'#a9c5ef');txt(d,(1410,974),'社区指针作品 · 非官方',20,'#a9c5ef')
 return im
def main():
 def stamp(s):
  ms=round(s*1000);h,ms=divmod(ms,3600000);m,ms=divmod(ms,60000);sec,ms=divmod(ms,1000);return f'{h:02}:{m:02}:{sec:02},{ms:03}'
 captions=[(0,4,'熟悉的小鲸鱼，这次变成了动态鼠标指针。')]+[(4+i*2,6+i*2,e['label']+' · '+labels[e['action']]) for i,e in enumerate(states)]+[(38,43,'32 / 48 / 64 / 128 / 256px，按实际大小对照。'),(43,47,'选好尺寸安装，开启尺寸保持，就让小鲸鱼陪着你吧。')]
 (V/'captions.srt').write_text('\n\n'.join(f'{i+1}\n{stamp(a)} --> {stamp(b)}\n{s}' for i,(a,b,s) in enumerate(captions))+'\n',encoding='utf-8')
 (V/'showcase-project.json').write_text(json.dumps({'seconds':DURATION,'size':[1920,1080],'fps':30,'scenes':[[0,4,'intro'],[4,38,'all 17 animated states'],[38,43,'five native sizes'],[43,47,'install and download']],'roles':[e['role'] for e in states],'role_seconds':2,'audio':'added separately using add_showcase_bgm.py'},ensure_ascii=False,indent=2),encoding='utf-8')
 clean=V/'showcase-clean.mp4';cmd=['ffmpeg','-hide_banner','-loglevel','error','-y','-f','rawvideo','-pix_fmt','rgb24','-s','1920x1080','-r','30','-i','pipe:0','-an','-c:v','libx264','-preset','fast','-crf','18','-pix_fmt','yuv420p','-movflags','+faststart',str(clean)]
 p=subprocess.Popen(cmd,stdin=subprocess.PIPE)
 try:
  for i in range(DURATION*30):p.stdin.write(render(i/30).tobytes())
 finally:p.stdin.close()
 assert p.wait()==0
 out=V/'showcase-captioned-silent.mp4'
 subprocess.run(['ffmpeg','-hide_banner','-loglevel','error','-y','-i',str(clean),'-vf',"subtitles=captions.srt:force_style='FontName=Microsoft YaHei,FontSize=24,MarginV=15,Outline=1'",'-an','-c:v','libx264','-preset','fast','-crf','18','-pix_fmt','yuv420p','-movflags','+faststart',str(out)],cwd=V,check=True)
 review=Image.new('RGB',(1920,1080))
 for i,t in enumerate((1,6,14,22,30,35,40,44,46)):review.paste(render(t).resize((640,360)),((i%3)*640,(i//3)*360))
 review.save(R/'previews/showcase-review.jpg',quality=94)
 print(json.dumps({'seconds':DURATION,'roles':17,'video':str(out)},ensure_ascii=False))
if __name__=='__main__':main()
