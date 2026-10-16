using System;
using System.IO;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
public static class LabCursorProbe {
 [StructLayout(LayoutKind.Sequential)] struct II {public int icon;public uint x,y;public IntPtr mask,color;}
 [StructLayout(LayoutKind.Sequential)] struct BM {public int type,width,height,stride;public ushort planes,bits;public IntPtr pixels;}
 [StructLayout(LayoutKind.Sequential)] public struct POINT {public int x,y;}
 [StructLayout(LayoutKind.Sequential)] public struct CI {public int size,flags;public IntPtr cursor;public POINT point;}
 [DllImport("user32.dll",EntryPoint="LoadCursorW")] public static extern IntPtr LoadCursor(IntPtr module,IntPtr id);
 [DllImport("user32.dll")] static extern bool GetIconInfo(IntPtr h,out II i);
 [DllImport("gdi32.dll",EntryPoint="GetObjectW")] static extern int GetObject(IntPtr h,int size,out BM b);
 [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr h);
 [DllImport("user32.dll")] static extern bool DrawIconEx(IntPtr dc,int x,int y,IntPtr h,int w,int height,uint step,IntPtr brush,uint flags);
 [DllImport("user32.dll")] public static extern bool GetCursorInfo(ref CI i);
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")] public static extern uint GetDpiForSystem();
 [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
 public static int[] Dimensions(IntPtr h){II i;if(!GetIconInfo(h,out i))throw new Exception("GetIconInfo");try{BM b;if(GetObject(i.color!=IntPtr.Zero?i.color:i.mask,Marshal.SizeOf(typeof(BM)),out b)==0)throw new Exception("GetObject");return new int[]{b.width,i.color!=IntPtr.Zero?b.height:b.height/2,(int)i.x,(int)i.y};}finally{if(i.color!=IntPtr.Zero)DeleteObject(i.color);if(i.mask!=IntPtr.Zero)DeleteObject(i.mask);}}
 public static int[] Size(int id){return Dimensions(LoadCursor(IntPtr.Zero,new IntPtr(id)));}
 public static void Save(int id,string path,int background){IntPtr h=LoadCursor(IntPtr.Zero,new IntPtr(id));int[] d=Dimensions(h);using(Bitmap b=new Bitmap(d[0],d[1],PixelFormat.Format32bppArgb)){using(Graphics g=Graphics.FromImage(b)){g.Clear(Color.FromArgb(background));IntPtr dc=g.GetHdc();try{if(!DrawIconEx(dc,0,0,h,d[0],d[1],0,IntPtr.Zero,3))throw new Exception("DrawIconEx");}finally{g.ReleaseHdc(dc);}}b.Save(path,ImageFormat.Png);}}
 public static string CaptureDesktop(string path,int left,int top,int width,int height){CI i=new CI();i.size=Marshal.SizeOf(typeof(CI));bool ok=GetCursorInfo(ref i);using(Bitmap b=new Bitmap(width,height)){using(Graphics g=Graphics.FromImage(b)){g.CopyFromScreen(left,top,0,0,new Size(width,height));if(ok&&i.flags==1){int[] d=Dimensions(i.cursor);IntPtr dc=g.GetHdc();try{DrawIconEx(dc,i.point.x-left-d[2],i.point.y-top-d[3],i.cursor,d[0],d[1],0,IntPtr.Zero,3);}finally{g.ReleaseHdc(dc);}}}b.Save(path,ImageFormat.Png);}return "CopyFromScreen plus GetCursorInfo/DrawIconEx cursor composite; hardware cursor capture not asserted; cursorVisible="+(ok&&i.flags==1);}
}
