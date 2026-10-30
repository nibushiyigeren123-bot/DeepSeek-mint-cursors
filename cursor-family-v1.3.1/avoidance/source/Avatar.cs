using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal sealed class Avatar : Form {
 [StructLayout(LayoutKind.Sequential)] struct Header {public uint size;public int width,height;public ushort planes,bits;public uint compression,image;public int x,y;public uint used,important;}
 [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr dc,ref Header header,uint use,out IntPtr pixels,IntPtr section,uint offset);
 readonly IntPtr dc;
 IntPtr bitmap,originalBitmap,pixels;
 int width,height,previousCount;
 byte[] work,previousFrame;
 readonly Rectangle[] cuts=new Rectangle[256],previousCuts=new Rectangle[256];
 Point previousPosition;
 bool first=true,cleaned;
 long nextTopmostCheckTicks;
 internal long Uploads,Moves;
 internal bool FreezeAnimation=false,HiddenForTest=false,InputPending;
 protected override bool ShowWithoutActivation {get{return true;}}
 protected override CreateParams CreateParams {get{var p=base.CreateParams;p.ExStyle|=0x80000|0x20|0x08000000|0x80;return p;}}
 internal Avatar(int size){
  FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;TopMost=true;Text="AICursorFamilyAvatarR5";
  dc=Win.CreateCompatibleDC(IntPtr.Zero);
  if(dc==IntPtr.Zero)throw new System.ComponentModel.Win32Exception();
  try{EnsureSize(size,size);IntPtr handle=Handle;}
  catch{if(bitmap!=IntPtr.Zero){Win.SelectObject(dc,originalBitmap);Win.DeleteObject(bitmap);}Win.DeleteDC(dc);cleaned=true;throw;}
 }
 // [代码块] avoidance.avatar.handle-created | 范围：第 34-34 行（人工核验）
 // [连接] WinForms 句柄创建/重建 → OnHandleCreated → EnsureTopmost。
 // [修改] 2026-10-30 cursor-final：创建真实 HWND 后检查扩展样式，不只依赖托管 TopMost 属性。
 // [功能] 在句柄建立时确认人物处于置顶窗口组。
 protected override void OnHandleCreated(EventArgs args){base.OnHandleCreated(args);EnsureTopmost(true);}
 // [代码块] avoidance.avatar.topmost | 范围：第 39-44 行（人工核验）
 // [连接] OnHandleCreated/Render → EnsureTopmost → Win.Style；缺位才 Win.SetWindowPos。
 // [修改] 2026-10-30 cursor-final：新增每500ms只读实际 WS_EX_TOPMOST，丢失才恢复。
 // [功能] 保持人物高于普通窗口，不反复重排同组系统提示和输入法窗口或重置悬停。
 void EnsureTopmost(bool force){
  long now=Stopwatch.GetTimestamp();
  if(!force&&now<nextTopmostCheckTicks)return;
  nextTopmostCheckTicks=now+Stopwatch.Frequency/2;
  if((Win.Style(Handle,-20)&0x8)==0&&!Win.SetWindowPos(Handle,new IntPtr(-1),0,0,0,0,0x613))throw new System.ComponentModel.Win32Exception();
 }
 void EnsureSize(int w,int h){
  if(w<1||h<1||w>512||h>512)throw new InvalidOperationException("Character surface out of range.");
  if(width==w&&height==h)return;
  var newWork=new byte[checked(w*h*4)];
  var header=new Header{size=40,width=w,height=-h,planes=1,bits=32};IntPtr newPixels;
  IntPtr next=CreateDIBSection(dc,ref header,0,out newPixels,IntPtr.Zero,0);
  if(next==IntPtr.Zero)throw new System.ComponentModel.Win32Exception();
  IntPtr replaced=Win.SelectObject(dc,next);
  if(replaced==IntPtr.Zero||replaced==new IntPtr(-1)){Win.DeleteObject(next);throw new System.ComponentModel.Win32Exception();}
  if(bitmap==IntPtr.Zero)originalBitmap=replaced;
  else Win.DeleteObject(bitmap);
  bitmap=next;pixels=newPixels;work=newWork;width=w;height=h;
  first=true;previousFrame=null;previousCount=0;
 }
 protected override void WndProc(ref Message m){
  if(m.Msg==0xFF)InputPending=true;
#if TEST_INSTRUMENTATION
  if(m.Msg==0x8020){HiddenForTest=m.WParam!=IntPtr.Zero;if(HiddenForTest)Hide();m.Result=new IntPtr(1);return;}
  if(m.Msg==0x8021){FreezeAnimation=m.WParam!=IntPtr.Zero;m.Result=new IntPtr(1);return;}
#endif
  if(m.Msg==0x84){m.Result=new IntPtr(-1);return;}
  if(m.Msg==0x21){m.Result=new IntPtr(3);return;}
  base.WndProc(ref m);
 }
 internal static byte[] LoadFrame(string path,int w,int h){
  using(var source=new Bitmap(path))using(var image=new Bitmap(w,h,PixelFormat.Format32bppPArgb)){
   if(source.Width!=w||source.Height!=h)throw new InvalidOperationException("Character frame differs from manifest surface.");
   using(var g=Graphics.FromImage(image))g.DrawImageUnscaled(source,0,0);
   var d=image.LockBits(new Rectangle(0,0,w,h),ImageLockMode.ReadOnly,PixelFormat.Format32bppPArgb);
   try{var bytes=new byte[w*h*4];for(int y=0;y<h;y++)Marshal.Copy(IntPtr.Add(d.Scan0,y*d.Stride),bytes,y*w*4,w*4);return bytes;}
   finally{image.UnlockBits(d);}
  }
 }
 // [代码块] avoidance.avatar.render | 范围：第 82-106 行（人工核验）
 // [连接] AvatarHost.Run → Render；UpdateLayeredWindow/SetWindowPos 绘制移动；EnsureTopmost 维护层级。
 // [修改] 2026-10-30 cursor-final：首次显示后确认置顶，其余帧只进行限频检查；人物尺寸、像素与裁切逻辑不变。
 // [功能] 复用人物表面，按当前帧和原有裁切规则绘制，并无激活显示。
 internal void Render(byte[] frame,Point location,Rectangle[] boxes,int w,int h){
  if(frame==null||frame.Length!=checked(w*h*4))throw new InvalidOperationException("Invalid frame byte count.");
  EnsureSize(w,h);
  int count=0;var bounds=new Rectangle(location,new Size(width,height));
  for(int i=0;i<boxes.Length&&count<cuts.Length;i++){
   var hit=Rectangle.Intersect(bounds,boxes[i]);if(hit.Width<=0||hit.Height<=0)continue;
   hit.Offset(-location.X,-location.Y);cuts[count++]=hit;
  }
  bool changed=first||previousFrame!=frame||count!=previousCount;
  if(!changed)for(int i=0;i<count;i++)if(cuts[i]!=previousCuts[i]){changed=true;break;}
  if(changed){
   Buffer.BlockCopy(frame,0,work,0,work.Length);
   for(int i=0;i<count;i++){var r=cuts[i];for(int y=r.Top;y<r.Bottom;y++)Array.Clear(work,(y*width+r.Left)*4,r.Width*4);}
   Marshal.Copy(work,0,pixels,work.Length);
   var p=new Win.Point(location.X,location.Y);var size=new Win.Size(width,height);var zero=new Win.Point();var blend=new Win.Blend{Alpha=255,Format=1};
   if(!Win.UpdateLayeredWindow(Handle,IntPtr.Zero,ref p,ref size,dc,ref zero,0,ref blend,2))throw new System.ComponentModel.Win32Exception();
   Uploads++;
  }else if(location!=previousPosition){
   if(!Win.SetWindowPos(Handle,IntPtr.Zero,location.X,location.Y,0,0,0x1|0x4|0x10))throw new System.ComponentModel.Win32Exception();
   Moves++;
  }
  Array.Copy(cuts,previousCuts,count);previousCount=count;previousFrame=frame;previousPosition=location;first=false;
  bool showing=!Visible;if(showing)Show();
  EnsureTopmost(showing);
 }
#if TEST_INSTRUMENTATION
 internal byte[] SurfaceForTest(){var result=new byte[work.Length];Marshal.Copy(pixels,result,0,result.Length);return result;}
#endif
 protected override void Dispose(bool disposing){
  if(disposing&&!cleaned){cleaned=true;if(bitmap!=IntPtr.Zero){Win.SelectObject(dc,originalBitmap);Win.DeleteObject(bitmap);bitmap=IntPtr.Zero;}Win.DeleteDC(dc);}
  base.Dispose(disposing);
 }
}
