using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

internal static class Win {
 [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X,Y; public Point(int x,int y){X=x;Y=y;} }
 [StructLayout(LayoutKind.Sequential)] internal struct Size { public int X,Y; public Size(int x,int y){X=x;Y=y;} }
 [StructLayout(LayoutKind.Sequential)] internal struct Rect {public int L,T,R,B;}
 [StructLayout(LayoutKind.Sequential,Pack=1)] internal struct Blend {public byte Op,Flags,Alpha,Format;}
 internal delegate bool EnumProc(IntPtr h,IntPtr p);
 [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumProc fn,IntPtr p);
 [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll",EntryPoint="GetWindowLongW")] internal static extern int Style(IntPtr h,int n);
 [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr h,out Rect r);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] internal static extern int GetClassName(IntPtr h,StringBuilder s,int n);
 [DllImport("user32.dll",SetLastError=true)] internal static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int w,int height,uint flags);
 [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] internal static extern IntPtr SetCursor(IntPtr cursor);
 [DllImport("user32.dll")] internal static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")] internal static extern IntPtr WindowFromPoint(Point p);
 [DllImport("user32.dll")] internal static extern IntPtr SendMessage(IntPtr h,uint msg,IntPtr w,IntPtr l);
 [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)] internal static extern IntPtr LoadImage(IntPtr m,string f,uint type,int w,int h,uint flags);
 [DllImport("user32.dll")] internal static extern bool DestroyCursor(IntPtr h);
 [DllImport("user32.dll")] internal static extern IntPtr GetDC(IntPtr h);
 [DllImport("user32.dll")] internal static extern int ReleaseDC(IntPtr h,IntPtr dc);
 [DllImport("gdi32.dll")] internal static extern IntPtr CreateCompatibleDC(IntPtr dc);
 [DllImport("gdi32.dll")] internal static extern IntPtr SelectObject(IntPtr dc,IntPtr o);
 [DllImport("gdi32.dll")] internal static extern bool DeleteObject(IntPtr o);
 [DllImport("gdi32.dll")] internal static extern bool DeleteDC(IntPtr dc);
 [DllImport("user32.dll",SetLastError=true)] internal static extern bool UpdateLayeredWindow(IntPtr h,IntPtr dc,ref Point p,ref Size size,IntPtr src,ref Point srcp,uint key,ref Blend blend,uint flags);
 [DllImport("user32.dll")] internal static extern bool SetProcessDPIAware();
 internal static List<IntPtr> Windows(){var a=new List<IntPtr>();EnumWindows(delegate(IntPtr h,IntPtr p){if(IsWindowVisible(h))a.Add(h);return true;},IntPtr.Zero);return a;}
 internal static string Class(IntPtr h){var s=new StringBuilder(256);GetClassName(h,s,256);return s.ToString();}
}
