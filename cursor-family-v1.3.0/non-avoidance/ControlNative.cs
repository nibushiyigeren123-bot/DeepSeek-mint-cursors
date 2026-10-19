using System;
using System.Runtime.InteropServices;
public static class CursorTrialControl {
 [System.Runtime.InteropServices.DllImport("user32.dll",SetLastError=true)]static extern bool PostMessageW(System.IntPtr hwnd,uint message,System.IntPtr w,System.IntPtr l);
 [System.Runtime.InteropServices.DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(System.IntPtr hwnd,out uint process);
 public static bool RequestNativeReload(long hwnd,int expectedPid,int token){uint pid;GetWindowThreadProcessId(new System.IntPtr(hwnd),out pid);return pid==(uint)expectedPid&&PostMessageW(new System.IntPtr(hwnd),0x8031,new System.IntPtr(token),System.IntPtr.Zero);}

 [StructLayout(LayoutKind.Sequential)] struct II { public int icon; public uint x,y; public IntPtr mask,color; }
 [StructLayout(LayoutKind.Sequential)] struct BM { public int type,width,height,stride; public ushort planes,bits; public IntPtr pixels; }
 [DllImport("user32.dll",EntryPoint="LoadCursorW")] static extern IntPtr LoadCursor(IntPtr module,IntPtr id);
 [DllImport("user32.dll")] static extern bool GetIconInfo(IntPtr h,out II info);
 [DllImport("gdi32.dll",EntryPoint="GetObjectW")] static extern int GetObject(IntPtr h,int bytes,out BM info);
 [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr h);
 [DllImport("user32.dll")] public static extern bool SystemParametersInfo(uint action,uint parameter,IntPtr value,uint flags);
 public static int[] Dimensions(int id) {
  II icon; if(!GetIconInfo(LoadCursor(IntPtr.Zero,new IntPtr(id)),out icon))throw new InvalidOperationException("Cannot read shared cursor.");
  try { BM bitmap; if(GetObject(icon.color!=IntPtr.Zero?icon.color:icon.mask,Marshal.SizeOf(typeof(BM)),out bitmap)==0)throw new InvalidOperationException("Cannot read shared bitmap.");return new[]{bitmap.width,icon.color!=IntPtr.Zero?bitmap.height:bitmap.height/2}; }
  finally { if(icon.color!=IntPtr.Zero)DeleteObject(icon.color);if(icon.mask!=IntPtr.Zero)DeleteObject(icon.mask); }
 }
}
