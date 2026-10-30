using System;
using System.IO;
using System.Runtime.InteropServices;
internal static class NativeCursor {
 [StructLayout(LayoutKind.Sequential)] struct IconInfo {internal int icon;internal uint x,y;internal IntPtr mask,color;}
 [StructLayout(LayoutKind.Sequential)] struct BitmapInfo {internal int type,width,height,stride;internal ushort planes,bits;internal IntPtr pixels;}
 [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr LoadImageW(IntPtr module,string file,uint type,int w,int h,uint flags);
 [DllImport("user32.dll")] static extern bool GetIconInfo(IntPtr cursor,out IconInfo info);
 [DllImport("gdi32.dll",EntryPoint="GetObjectW")] static extern int GetObject(IntPtr bitmap,int size,out BitmapInfo info);
 [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr bitmap);
 [DllImport("user32.dll")] static extern bool DestroyCursor(IntPtr cursor);
 [DllImport("user32.dll",SetLastError=true)] static extern bool SetSystemCursor(IntPtr cursor,uint id);
 [DllImport("user32.dll",SetLastError=true)] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
 [DllImport("user32.dll")] static extern bool SystemParametersInfo(uint action,uint parameter,IntPtr value,uint flags);
 internal static void ReloadCurrentScheme(){if(!SystemParametersInfo(0x57,0,IntPtr.Zero,0))throw new InvalidOperationException("Cannot reload concurrently selected cursor scheme.");}
 // [代码块] original.native.person32 | 范围：第 20-41 行（人工核验）
 // [连接] Program.cs Controller.Keep → ApplyPerson32；stillOwned回调复核Selected的方案/17路径/偏好；下游SetSystemCursor。
 // [修改] 2026-10-30 cursor-final plan-v4：只在本函数限域切到unaware，与SPI方案的32逻辑基准一致，finally恢复线程上下文。
 // [功能] 保留原ANI帧与节奏，补载Person；拒绝非32源或失去所有权，不改进程DPI与用户全局偏好。
 internal static bool ApplyPerson32(string root,string id,Func<bool> stillOwned){
  IntPtr previousContext=SetThreadDpiAwarenessContext(new IntPtr(-1));
  if(previousContext==IntPtr.Zero)throw new System.ComponentModel.Win32Exception();
  IntPtr h=IntPtr.Zero;
  try{
   // 32是unaware逻辑基准：150%下aware读取为48，必须与其余16个SPI角色一致。
   string path=Path.Combine(root,"assets",id,"Person","symbol.ani");
   h=LoadImageW(IntPtr.Zero,path,2,32,32,16);
   if(h==IntPtr.Zero)throw new System.ComponentModel.Win32Exception();
   IconInfo info;if(!GetIconInfo(h,out info))throw new System.ComponentModel.Win32Exception();
   try{BitmapInfo bitmap;if(GetObject(info.color!=IntPtr.Zero?info.color:info.mask,Marshal.SizeOf(typeof(BitmapInfo)),out bitmap)==0||bitmap.width!=32||(info.color!=IntPtr.Zero?bitmap.height:bitmap.height/2)!=32)throw new InvalidOperationException("Refuse non-32 native compatibility cursor.");}
   finally{if(info.mask!=IntPtr.Zero)DeleteObject(info.mask);if(info.color!=IntPtr.Zero)DeleteObject(info.color);}
   if(!stillOwned())return false;
   if(!SetSystemCursor(h,32672))throw new System.ComponentModel.Win32Exception();
   h=IntPtr.Zero; // SetSystemCursor接管原ANI句柄，不克隆成静态帧，也不重复销毁。
   return true;
  }finally{
   if(h!=IntPtr.Zero)DestroyCursor(h);
   // 正常成功、失去所有权提前返回和加载/校验异常均经过此恢复。
   if(SetThreadDpiAwarenessContext(previousContext)==IntPtr.Zero)throw new System.ComponentModel.Win32Exception();
  }
 }
 internal static readonly string[] Names={"Arrow","Help","AppStarting","Wait","Crosshair","IBeam","NWPen","No","SizeNS","SizeWE","SizeNWSE","SizeNESW","SizeAll","UpArrow","Hand","Pin","Person"};
 internal static readonly int[] Ids={32512,32651,32650,32514,32515,32513,32631,32648,32645,32644,32642,32643,32646,32516,32649,32671,32672};
 [StructLayout(LayoutKind.Sequential)] internal struct Info {public uint size,flags;public IntPtr cursor;public Win.Point point;}
 [DllImport("user32.dll")] internal static extern bool GetCursorInfo(ref Info value);
 [DllImport("user32.dll",EntryPoint="LoadCursorW")] internal static extern IntPtr LoadCursor(IntPtr module,IntPtr id);
 internal static IntPtr[] Handles(){var result=new IntPtr[17];for(int i=0;i<17;i++)result[i]=LoadCursor(IntPtr.Zero,new IntPtr(Ids[i]));return result;}
}
