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
 [DllImport("user32.dll")] static extern bool SystemParametersInfo(uint action,uint parameter,IntPtr value,uint flags);
 internal static void ReloadCurrentScheme(){if(!SystemParametersInfo(0x57,0,IntPtr.Zero,0))throw new InvalidOperationException("Cannot reload concurrently selected cursor scheme.");}
 internal static bool ApplyPerson32(string root,string id,Func<bool> stillOwned){
  // A lab reload probe retained a stock Person image after SPI_SETCURSORS.
  // Fix only that role, once per owned selection. Never
  // write the user's cursor-size preference or install a larger shared cursor.
  string path=Path.Combine(root,"assets",id,"Person","symbol.ani");
  IntPtr h=LoadImageW(IntPtr.Zero,path,2,32,32,16);
  if(h==IntPtr.Zero)throw new System.ComponentModel.Win32Exception();
  try{
   IconInfo info;if(!GetIconInfo(h,out info))throw new System.ComponentModel.Win32Exception();
   try{BitmapInfo bitmap;if(GetObject(info.color!=IntPtr.Zero?info.color:info.mask,Marshal.SizeOf(typeof(BitmapInfo)),out bitmap)==0||bitmap.width!=32||(info.color!=IntPtr.Zero?bitmap.height:bitmap.height/2)!=32)throw new InvalidOperationException("Refuse non-32 native compatibility cursor.");}
   finally{if(info.mask!=IntPtr.Zero)DeleteObject(info.mask);if(info.color!=IntPtr.Zero)DeleteObject(info.color);}
   if(!stillOwned())return false;
   if(!SetSystemCursor(h,32672))throw new System.ComponentModel.Win32Exception();
   h=IntPtr.Zero; // SetSystemCursor consumes the owned cursor.
   return true;
  }finally{if(h!=IntPtr.Zero)DestroyCursor(h);}
 }
 internal static readonly string[] Names={"Arrow","Help","AppStarting","Wait","Crosshair","IBeam","NWPen","No","SizeNS","SizeWE","SizeNWSE","SizeNESW","SizeAll","UpArrow","Hand","Pin","Person"};
 internal static readonly int[] Ids={32512,32651,32650,32514,32515,32513,32631,32648,32645,32644,32642,32643,32646,32516,32649,32671,32672};
 [StructLayout(LayoutKind.Sequential)] internal struct Info {public uint size,flags;public IntPtr cursor;public Win.Point point;}
 [DllImport("user32.dll")] internal static extern bool GetCursorInfo(ref Info value);
 [DllImport("user32.dll",EntryPoint="LoadCursorW")] internal static extern IntPtr LoadCursor(IntPtr module,IntPtr id);
 internal static IntPtr[] Handles(){var result=new IntPtr[17];for(int i=0;i<17;i++)result[i]=LoadCursor(IntPtr.Zero,new IntPtr(Ids[i]));return result;}
}
