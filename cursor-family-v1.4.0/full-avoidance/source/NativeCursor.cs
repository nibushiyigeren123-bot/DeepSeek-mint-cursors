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
 // [代码块] prototype.native.validate | 范围：第 19-26 行（括号核验）。
 // [连接] ApplyProfile加载阶段及提交后的共享读回 → Validate；不在绘制循环调用。
 // [修改] 2026-10-31 plan v2：校验当前unaware逻辑画布及精确热点，保留完整ANI句柄。
 static void Validate(IntPtr handle,int size,CharacterFrames.Role role){
  IconInfo info;if(!GetIconInfo(handle,out info))throw new System.ComponentModel.Win32Exception();
  try{
   BitmapInfo bitmap;
   if(GetObject(info.color!=IntPtr.Zero?info.color:info.mask,Marshal.SizeOf(typeof(BitmapInfo)),out bitmap)==0||bitmap.width!=size||(info.color!=IntPtr.Zero?bitmap.height:bitmap.height/2)!=size||info.x!=role.SymbolX||info.y!=role.SymbolY)
    throw new InvalidOperationException("Native logical canvas or hotspot differs from profile.");
  }finally{if(info.mask!=IntPtr.Zero)DeleteObject(info.mask);if(info.color!=IntPtr.Zero)DeleteObject(info.color);}
 }
 // [代码块] prototype.native.apply | 范围：第 31-62 行（括号核验）。
 // [连接] Controller.Keep → ApplyProfile；stillOwned复核方案/17路径/目标偏好；下游SetSystemCursor。
 // [修改] 2026-10-31 plan v2：先验证全部17句柄，再成组提交；部分提交失权/失败按当前注册方案刷新。
 // [功能] expectedNativeSize为unaware逻辑基准；150% aware位图会放大，不是各DPI物理恒定。
 internal static bool ApplyProfile(string root,CharacterFrames profile,Func<bool> stillOwned){
  IntPtr previousContext=SetThreadDpiAwarenessContext(new IntPtr(-1));
  if(previousContext==IntPtr.Zero)throw new System.ComponentModel.Win32Exception();
  var loaded=new IntPtr[17];bool touched=false,complete=false;
  try{
   for(int i=0;i<17;i++){
    string path=Path.Combine(root,"assets",profile.Id,Names[i],"symbol.ani");
    loaded[i]=LoadImageW(IntPtr.Zero,path,2,profile.ExpectedNativeSize,profile.ExpectedNativeSize,16);
    if(loaded[i]==IntPtr.Zero)throw new System.ComponentModel.Win32Exception();
    Validate(loaded[i],profile.ExpectedNativeSize,profile.Roles[i]);
   }
   for(int i=0;i<17;i++){
    if(!stillOwned())return false;
    if(!SetSystemCursor(loaded[i],(uint)Ids[i]))throw new System.ComponentModel.Win32Exception();
    loaded[i]=IntPtr.Zero;touched=true; // 成功后系统接管原ANI，保留动画，不销毁第二次。
   }
   for(int i=0;i<17;i++){
    if(!stillOwned())return false;
    Validate(LoadCursor(IntPtr.Zero,new IntPtr(Ids[i])),profile.ExpectedNativeSize,profile.Roles[i]);
   }
   if(!stillOwned())return false;
   complete=true;return true;
  }finally{
   try{
    foreach(IntPtr handle in loaded)if(handle!=IntPtr.Zero)DestroyCursor(handle);
    if(touched&&!complete)ReloadCurrentScheme();
   }finally{
    // 包含false、校验异常及回退异常的所有路径均恢复原线程DPI上下文。
    if(SetThreadDpiAwarenessContext(previousContext)==IntPtr.Zero)throw new System.ComponentModel.Win32Exception();
   }
  }
 }
 // [功能] 纯几何：可见右/下边界相对点击热点的增长量，100%为零；由CachePhysicalLayout调用。
 // [修改] plan v3：向上取整覆盖缩放边缘，仅整体平移人物，不缩放/重采样任何帧。
 // [代码块] prototype.native.layout-growth | 范围：第 66-68 行（括号核验）。
 internal static int LayoutGrowth(int logicalEdge,int logicalHot,int physicalSize,int logicalSize,int physicalHot){
  return Math.Max(0,(int)Math.Ceiling((double)logicalEdge*physicalSize/logicalSize)-physicalHot-(logicalEdge-logicalHot));
 }
 // [功能] ApplyProfile已finally恢复主线程aware上下文后只读取一次17role实际geometry。
 // [连接] Controller.Keep → CachePhysicalLayout → Role不可变缓存 → AvatarHost.Render。
 // [修改] plan v3：按所有phase边界并集固定偏移，禁止在动画循环GetIconInfo/GDI扫描。
 // [代码块] prototype.native.physical-cache | 范围：第 73-85 行（括号核验）。
 internal static void CachePhysicalLayout(CharacterFrames profile){
  for(int i=0;i<17;i++){
   var role=profile.Roles[i];IconInfo info;IntPtr handle=LoadCursor(IntPtr.Zero,new IntPtr(Ids[i]));
   if(!GetIconInfo(handle,out info))throw new System.ComponentModel.Win32Exception();
   try{
    BitmapInfo bitmap;if(GetObject(info.color!=IntPtr.Zero?info.color:info.mask,Marshal.SizeOf(typeof(BitmapInfo)),out bitmap)==0)throw new System.ComponentModel.Win32Exception();
    role.PhysicalWidth=bitmap.width;role.PhysicalHeight=info.color!=IntPtr.Zero?bitmap.height:bitmap.height/2;role.PhysicalHotX=checked((int)info.x);role.PhysicalHotY=checked((int)info.y);
    if(role.PhysicalWidth<1||role.PhysicalHeight<1||role.PhysicalHotX<0||role.PhysicalHotY<0||role.PhysicalHotX>=role.PhysicalWidth||role.PhysicalHotY>=role.PhysicalHeight)throw new InvalidOperationException("Invalid physical native geometry.");
    role.LayoutOffsetX=LayoutGrowth(role.BoundsRight,role.SymbolX,role.PhysicalWidth,profile.ExpectedNativeSize,role.PhysicalHotX);
    role.LayoutOffsetY=LayoutGrowth(role.BoundsBottom,role.SymbolY,role.PhysicalHeight,profile.ExpectedNativeSize,role.PhysicalHotY);
   }finally{if(info.mask!=IntPtr.Zero)DeleteObject(info.mask);if(info.color!=IntPtr.Zero)DeleteObject(info.color);}
  }
 }
 internal static readonly string[] Names={"Arrow","Help","AppStarting","Wait","Crosshair","IBeam","NWPen","No","SizeNS","SizeWE","SizeNWSE","SizeNESW","SizeAll","UpArrow","Hand","Pin","Person"};
 internal static readonly int[] Ids={32512,32651,32650,32514,32515,32513,32631,32648,32645,32644,32642,32643,32646,32516,32649,32671,32672};
 [StructLayout(LayoutKind.Sequential)] internal struct Info {public uint size,flags;public IntPtr cursor;public Win.Point point;}
 [DllImport("user32.dll")] internal static extern bool GetCursorInfo(ref Info value);
 [DllImport("user32.dll",EntryPoint="LoadCursorW")] internal static extern IntPtr LoadCursor(IntPtr module,IntPtr id);
 internal static IntPtr[] Handles(){var result=new IntPtr[17];for(int i=0;i<17;i++)result[i]=LoadCursor(IntPtr.Zero,new IntPtr(Ids[i]));return result;}
}
