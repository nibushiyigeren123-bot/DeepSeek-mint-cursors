using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal sealed class Avatar : Form {
 [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h,uint command);
 IntPtr lastRaisedWindow;
 void RestoreNonAvoidingZOrder(Point location){
  if(lastRaisedWindow!=IntPtr.Zero&&!Win.IsWindowVisible(lastRaisedWindow))lastRaisedWindow=IntPtr.Zero;
  var bounds=new Rectangle(location,new Size(width,height));
  IntPtr h=GetWindow(Handle,3);int checkedWindows=0;
  while(h!=IntPtr.Zero&&checkedWindows++<64){
   Win.Rect r;
   if(Win.IsWindowVisible(h)&&Win.GetWindowRect(h,out r)&&bounds.IntersectsWith(Rectangle.FromLTRB(r.L,r.T,r.R,r.B))){
    if(h!=lastRaisedWindow){
     // Reorder only when a visible covering window appeared. Repeating a
     // topmost operation every frame can keep resetting native hover timers.
     if(!Win.SetWindowPos(Handle,new IntPtr(-1),0,0,0,0,0x613))throw new System.ComponentModel.Win32Exception();
     lastRaisedWindow=h;
    }
    break;
   }
   h=GetWindow(h,3);
  }
 }

 [StructLayout(LayoutKind.Sequential)] struct Header {public uint size;public int width,height;public ushort planes,bits;public uint compression,image;public int x,y;public uint used,important;}
 [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr dc,ref Header header,uint use,out IntPtr pixels,IntPtr section,uint offset);
 readonly IntPtr dc;
 IntPtr bitmap,originalBitmap,pixels;
 int width,height,previousCount;
 byte[] work,previousFrame;
 readonly Rectangle[] cuts=new Rectangle[256],previousCuts=new Rectangle[256];
 Point previousPosition;
 bool first=true,cleaned;
 internal long Uploads,Moves;
 internal bool FreezeAnimation=false,HiddenForTest=false,InputPending;
 protected override bool ShowWithoutActivation {get{return true;}}
 protected override CreateParams CreateParams {get{var p=base.CreateParams;p.ExStyle|=0x80000|0x20|0x08000000|0x80;return p;}}
 internal Avatar(int size){
  FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;TopMost=true;Text="AICursorFamilyAvatarOriginalR1";
  dc=Win.CreateCompatibleDC(IntPtr.Zero);
  if(dc==IntPtr.Zero)throw new System.ComponentModel.Win32Exception();
  try{EnsureSize(size,size);IntPtr handle=Handle;}
  catch{if(bitmap!=IntPtr.Zero){Win.SelectObject(dc,originalBitmap);Win.DeleteObject(bitmap);}Win.DeleteDC(dc);cleaned=true;throw;}
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
  if(!Visible)Show();
  RestoreNonAvoidingZOrder(location);
 }
#if TEST_INSTRUMENTATION
 internal byte[] SurfaceForTest(){var result=new byte[work.Length];Marshal.Copy(pixels,result,0,result.Length);return result;}
#endif
 protected override void Dispose(bool disposing){
  if(disposing&&!cleaned){cleaned=true;if(bitmap!=IntPtr.Zero){Win.SelectObject(dc,originalBitmap);Win.DeleteObject(bitmap);bitmap=IntPtr.Zero;}Win.DeleteDC(dc);}
  base.Dispose(disposing);
 }
}
