using System;
using System.Collections.Generic;
using System.Text;
using System.Runtime.InteropServices;
using System.Drawing;
using System.Drawing.Imaging;
public static class ScopedAudit {
 delegate bool EP(IntPtr h,IntPtr l);
 [DllImport("user32.dll")]static extern bool EnumWindows(EP cb,IntPtr p);
 [DllImport("user32.dll")]static extern bool EnumChildWindows(IntPtr p,EP cb,IntPtr l);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetWindowText(IntPtr h,StringBuilder s,int n);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetClassName(IntPtr h,StringBuilder s,int n);
 [DllImport("user32.dll")]static extern IntPtr GetParent(IntPtr h);
 [DllImport("user32.dll")]static extern int GetDlgCtrlID(IntPtr h);
 [DllImport("user32.dll")]static extern IntPtr GetDlgItem(IntPtr h,int id);
 [DllImport("user32.dll")]static extern bool IsWindowEnabled(IntPtr h);
 [DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr h,uint m,IntPtr w,IntPtr l);
 [DllImport("user32.dll",CharSet=CharSet.Unicode,EntryPoint="SendMessageW")]static extern IntPtr SendText(IntPtr h,uint m,IntPtr w,StringBuilder l);
 [DllImport("user32.dll")]static extern bool PostMessage(IntPtr h,uint m,IntPtr w,IntPtr l);
 [DllImport("user32.dll",SetLastError=true)]public static extern bool SystemParametersInfoW(uint a,uint p,IntPtr v,uint flags);
 [DllImport("user32.dll")]static extern bool DrawIconEx(IntPtr dc,int x,int y,IntPtr h,int w,int ht,uint step,IntPtr brush,uint flags);
 [StructLayout(LayoutKind.Sequential)]struct MI {public int dx,dy;public uint data,flags,time;public UIntPtr extra;}
 [StructLayout(LayoutKind.Sequential)]struct INPUT {public uint type;public MI mouse;}
 [DllImport("user32.dll",SetLastError=true)]static extern uint SendInput(uint n,INPUT[] data,int size);
 public static void Wake(){INPUT i=new INPUT();i.mouse.dx=1;i.mouse.flags=1;if(SendInput(1,new INPUT[]{i},Marshal.SizeOf(typeof(INPUT)))!=1)throw new Exception("SendInput failed");}
 public static string Text(IntPtr h){StringBuilder s=new StringBuilder(512);GetWindowText(h,s,s.Capacity);return s.ToString();}
 public static string Class(IntPtr h){StringBuilder s=new StringBuilder(128);GetClassName(h,s,s.Capacity);return s.ToString();}
 public static IntPtr MouseDialog(){IntPtr result=IntPtr.Zero;EnumWindows(delegate(IntPtr h,IntPtr p){string t=Text(h);if(Class(h)=="#32770"&&(t.Contains("\u9f20\u6807")||t.Contains("Mouse")))result=h;return true;},IntPtr.Zero);return result;}
 public static string Select(IntPtr root,string wanted){IntPtr combo=IntPtr.Zero;int idx=-1;string chosen=null;EnumChildWindows(root,delegate(IntPtr h,IntPtr p){if(Class(h)=="ComboBox"){int n=SendMessage(h,0x146,IntPtr.Zero,IntPtr.Zero).ToInt32();for(int i=0;i<n;i++){StringBuilder s=new StringBuilder(512);SendText(h,0x148,new IntPtr(i),s);string t=s.ToString();bool match=wanted=="WINDOWS_DEFAULT"?((t.StartsWith("Windows ")&&t.Contains("\u9ed8\u8ba4")&&!t.Contains("\u5927"))||t.StartsWith("Windows Default (")):t==wanted;if(match){combo=h;idx=i;chosen=t;break;}}}return true;},IntPtr.Zero);if(combo==IntPtr.Zero||idx<0)throw new Exception("Scheme missing: "+wanted);SendMessage(combo,0x14E,new IntPtr(idx),IntPtr.Zero);SendMessage(GetParent(combo),0x111,new IntPtr((1<<16)|(GetDlgCtrlID(combo)&65535)),combo);IntPtr apply=GetDlgItem(root,0x3021);if(apply==IntPtr.Zero||GetParent(apply)!=root)throw new Exception("Direct Apply button missing");PostMessage(apply,0xF5,IntPtr.Zero,IntPtr.Zero);return chosen;}
 public static void ConfirmTestSchemeReplace(){EnumWindows(delegate(IntPtr h,IntPtr p){if(Class(h)=="#32770"&&Text(h).Contains("\u66ff\u6362\u65b9\u6848")){IntPtr yes=GetDlgItem(h,6);if(yes!=IntPtr.Zero)PostMessage(yes,0xF5,IntPtr.Zero,IntPtr.Zero);}return true;},IntPtr.Zero);}
 public static bool ApplyDone(IntPtr root){IntPtr b=GetDlgItem(root,0x3021);return b!=IntPtr.Zero&&!IsWindowEnabled(b);}
 public static void Close(IntPtr root){PostMessage(root,0x10,IntPtr.Zero,IntPtr.Zero);}
 public static object Current(string file,uint phase){LabCursorProbe.CI ci=new LabCursorProbe.CI();ci.size=Marshal.SizeOf(typeof(LabCursorProbe.CI));if(!LabCursorProbe.GetCursorInfo(ref ci)||ci.cursor==IntPtr.Zero)throw new Exception("Current cursor unavailable");int[] d=LabCursorProbe.Dimensions(ci.cursor);NativeDibCapture.Save(ci.cursor,d[0],d[1],phase,file);return new{visible=ci.flags==1,flags=ci.flags,bitmap=d,handle=ci.cursor.ToInt64(),x=ci.point.x,y=ci.point.y};}
}

public static class NativeDibCapture {
 [StructLayout(LayoutKind.Sequential)]struct BI{public uint size;public int width,height;public ushort planes,bits;public uint compression,image;public int x,y;public uint used,important;}
 [DllImport("gdi32.dll")]static extern IntPtr CreateCompatibleDC(IntPtr dc);
 [DllImport("gdi32.dll")]static extern bool DeleteDC(IntPtr dc);
 [DllImport("gdi32.dll")]static extern bool DeleteObject(IntPtr h);
 [DllImport("gdi32.dll")]static extern IntPtr SelectObject(IntPtr dc,IntPtr h);
 [DllImport("gdi32.dll")]static extern IntPtr CreateDIBSection(IntPtr dc,ref BI info,uint usage,out IntPtr pixels,IntPtr section,uint offset);
 [DllImport("user32.dll")]static extern bool DrawIconEx(IntPtr dc,int x,int y,IntPtr h,int w,int height,uint phase,IntPtr brush,uint flags);
 public static void Save(IntPtr cursor,int width,int height,uint phase,string file){
  int stride=(width*3+3)/4*4;byte[] buffer=new byte[stride*height];
  for(int y=0;y<height;y++)for(int x=0;x<width;x++){int i=y*stride+x*3;buffer[i]=219;buffer[i+1]=226;buffer[i+2]=216;}
  IntPtr dc=CreateCompatibleDC(IntPtr.Zero),pixels,bitmap=IntPtr.Zero,old=IntPtr.Zero;
  if(dc==IntPtr.Zero)throw new Exception("Native capture DC failed");
  try{BI info=new BI();info.size=40;info.width=width;info.height=-height;info.planes=1;info.bits=24;bitmap=CreateDIBSection(dc,ref info,0,out pixels,IntPtr.Zero,0);if(bitmap==IntPtr.Zero)throw new Exception("Native capture bitmap failed");old=SelectObject(dc,bitmap);Marshal.Copy(buffer,0,pixels,buffer.Length);
   if(!DrawIconEx(dc,0,0,cursor,width,height,phase,IntPtr.Zero,3))throw new Exception("Native cursor draw failed");Marshal.Copy(pixels,buffer,0,buffer.Length);
   using(Bitmap result=new Bitmap(width,height,PixelFormat.Format24bppRgb)){BitmapData locked=result.LockBits(new Rectangle(0,0,width,height),ImageLockMode.WriteOnly,PixelFormat.Format24bppRgb);try{for(int y=0;y<height;y++)Marshal.Copy(buffer,y*stride,new IntPtr(locked.Scan0.ToInt64()+y*locked.Stride),width*3);}finally{result.UnlockBits(locked);}result.Save(file,ImageFormat.Png);}
  }finally{if(old!=IntPtr.Zero)SelectObject(dc,old);if(bitmap!=IntPtr.Zero)DeleteObject(bitmap);DeleteDC(dc);}
 }
}
