using System;
using System.Runtime.InteropServices;
public static class CursorTrialControl {
 // [代码块] full.control.integrity | 范围：第 7-13 行（声明与只读查询块）。
 // [功能/连接] 控制脚本副作用前置校验 → 精确进程路径/Token完整性；只读句柄全部finally释放。
 // [修改] v1.4.0：普通CMD遇管理员启动的已有实例时明确拒绝，避免先改方案再通信失败。
 [DllImport("kernel32.dll",SetLastError=true)] static extern IntPtr OpenProcess(uint access,bool inherit,int pid);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool QueryFullProcessImageName(IntPtr process,uint flags,System.Text.StringBuilder path,ref int length);
 [DllImport("advapi32.dll",SetLastError=true)] static extern bool OpenProcessToken(IntPtr process,uint access,out IntPtr token);
 [DllImport("advapi32.dll",SetLastError=true)] static extern bool GetTokenInformation(IntPtr token,int info,IntPtr data,int length,out int needed);
 [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
 public static string ProcessPath(int pid){IntPtr p=OpenProcess(0x1000,false,pid);if(p==IntPtr.Zero)throw new System.ComponentModel.Win32Exception();try{int n=32768;var b=new System.Text.StringBuilder(n);if(!QueryFullProcessImageName(p,0,b,ref n))throw new System.ComponentModel.Win32Exception();return b.ToString();}finally{CloseHandle(p);}}
 public static int ProcessIntegrity(int pid){IntPtr p=OpenProcess(0x1000,false,pid),t=IntPtr.Zero,b=IntPtr.Zero;try{if(p==IntPtr.Zero||!OpenProcessToken(p,8,out t))throw new System.ComponentModel.Win32Exception();int n;GetTokenInformation(t,25,IntPtr.Zero,0,out n);if(n<=0)throw new System.ComponentModel.Win32Exception();b=Marshal.AllocHGlobal(n);if(!GetTokenInformation(t,25,b,n,out n))throw new System.ComponentModel.Win32Exception();string sid=new System.Security.Principal.SecurityIdentifier(Marshal.ReadIntPtr(b)).Value;return int.Parse(sid.Substring(sid.LastIndexOf('-')+1));}finally{if(b!=IntPtr.Zero)Marshal.FreeHGlobal(b);if(t!=IntPtr.Zero)CloseHandle(t);if(p!=IntPtr.Zero)CloseHandle(p);}}

 [System.Runtime.InteropServices.DllImport("user32.dll",SetLastError=true)]static extern bool PostMessageW(System.IntPtr hwnd,uint message,System.IntPtr w,System.IntPtr l);
 [System.Runtime.InteropServices.DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(System.IntPtr hwnd,out uint process);
 public static bool RequestNativeReload(long hwnd,int expectedPid,int token){uint pid;GetWindowThreadProcessId(new System.IntPtr(hwnd),out pid);return pid==(uint)expectedPid&&PostMessageW(new System.IntPtr(hwnd),0x8031,new System.IntPtr(token),System.IntPtr.Zero);}

 [StructLayout(LayoutKind.Sequential)] struct II { public int icon; public uint x,y; public IntPtr mask,color; }
 [StructLayout(LayoutKind.Sequential)] struct BM { public int type,width,height,stride; public ushort planes,bits; public IntPtr pixels; }
 [DllImport("user32.dll",EntryPoint="LoadCursorW")] static extern IntPtr LoadCursor(IntPtr module,IntPtr id);
 [DllImport("user32.dll")] static extern bool GetIconInfo(IntPtr h,out II info);
 [DllImport("gdi32.dll",EntryPoint="GetObjectW")] static extern int GetObject(IntPtr h,int bytes,out BM info);
 [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr h);
 [DllImport("user32.dll",SetLastError=true)] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
 [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr LoadImageW(IntPtr module,string file,uint type,int width,int height,uint flags);
 [DllImport("user32.dll",SetLastError=true)] static extern bool SetSystemCursor(IntPtr cursor,uint id);
 [DllImport("user32.dll")] static extern bool DestroyCursor(IntPtr cursor);
 static readonly int[] Ids={32512,32651,32650,32514,32515,32513,32631,32648,32645,32644,32642,32643,32646,32516,32649,32671,32672};
 [DllImport("user32.dll")] public static extern bool SystemParametersInfo(uint action,uint parameter,IntPtr value,uint flags);
 // [代码块] prototype.control.dimensions | 范围：第 34-36 行（词法括号核验）。
 // [连接] control.ps1 Status/Enable校验 → 本函数；只读共享位图，finally恢复调用线程。
 // [修改] 2026-10-31 plan v2：返回unaware逻辑尺寸，不依赖pwsh或cmd启动时的DPI环境。
 public static int[] Dimensions(int id) {
  int[] geometry=Geometry(id);return new[]{geometry[0],geometry[1]};
 }
 // [代码块] prototype.control.geometry | 范围：第 39-43 行（词法括号核验）。
 // [连接] Snapshot-Native记录实际位图和热点；与Dimensions一样限定unaware并恢复。
 public static int[] Geometry(int id) {
  IntPtr previous=SetThreadDpiAwarenessContext(new IntPtr(-1));
  if(previous==IntPtr.Zero)throw new System.ComponentModel.Win32Exception();
  try{return GeometryCurrent(LoadCursor(IntPtr.Zero,new IntPtr(id)));}finally{if(SetThreadDpiAwarenessContext(previous)==IntPtr.Zero)throw new System.ComponentModel.Win32Exception();}
 }
 // [代码块] prototype.control.current-bitmap | 范围：第 46-50 行（词法括号核验）。
 // [连接] Dimensions限定unaware后调用；GetIconInfo创建的位图在finally释放。
 static int[] GeometryCurrent(IntPtr handle) {
  II icon; if(!GetIconInfo(handle,out icon))throw new InvalidOperationException("Cannot read shared cursor.");
  try { BM bitmap; if(GetObject(icon.color!=IntPtr.Zero?icon.color:icon.mask,Marshal.SizeOf(typeof(BM)),out bitmap)==0)throw new InvalidOperationException("Cannot read shared bitmap.");return new[]{bitmap.width,icon.color!=IntPtr.Zero?bitmap.height:bitmap.height/2,(int)icon.x,(int)icon.y}; }
  finally { if(icon.color!=IntPtr.Zero)DeleteObject(icon.color);if(icon.mask!=IntPtr.Zero)DeleteObject(icon.mask); }
 }
 // [代码块] prototype.control.geometry-match | 范围：第 53-53 行（词法括号核验）。
 // [功能] 比较已观测几何；不把registry数字当作实际共享尺寸。
 static void Match(IntPtr handle,int size,int x,int y){var g=GeometryCurrent(handle);if(g[0]!=size||g[1]!=size||g[2]!=x||g[3]!=y)throw new InvalidOperationException("Restored cursor geometry differs from observed baseline.");}
 // [代码块] prototype.control.restore-native | 范围：第 58-93 行（词法括号核验）。
 // [连接] control.ps1 Restore先核对原登记路径并SPI刷新；本函数决定保留SPI结果或按原尺寸补交。
 // [修改] v3：SPI共享尺寸正确时保留Windows原路径变体，不用私有LoadImage热点覆盖它。
 // [功能] 原文件仍须能按记录尺寸解码；尺寸不足才严格按旧热点补交。空路径只可保留正确尺寸SPI。
 public static bool RestoreNative(string[] files,int[] sizes,int[] hotX,int[] hotY,Func<bool> stillOwned){
  if(files.Length!=17||sizes.Length!=17||hotX.Length!=17||hotY.Length!=17)throw new ArgumentException("Seventeen observed baseline roles required.");
  IntPtr previous=SetThreadDpiAwarenessContext(new IntPtr(-1));if(previous==IntPtr.Zero)throw new System.ComponentModel.Win32Exception();
  var loaded=new IntPtr[17];var preserveSpi=new bool[17];var expected=new int[17][];bool touched=false,complete=false;
  try{
   for(int i=0;i<17;i++){
    if(!stillOwned())return false;
    if(sizes[i]<1||sizes[i]>512)throw new InvalidOperationException("Unsupported observed baseline size.");
    var shared=GeometryCurrent(LoadCursor(IntPtr.Zero,new IntPtr(Ids[i])));
    preserveSpi[i]=shared[0]==sizes[i]&&shared[1]==sizes[i];
    // 同尺寸SPI结果以本次真实共享热点作稳定读回；旧观测热点只约束需要手动补交的分支。
    expected[i]=preserveSpi[i]?shared:new[]{sizes[i],sizes[i],hotX[i],hotY[i]};
    if(String.IsNullOrWhiteSpace(files[i])){
     if(!preserveSpi[i])throw new InvalidOperationException("Windows did not restore the observed size for a built-in cursor without a source file.");
     continue;
    }
    loaded[i]=LoadImageW(IntPtr.Zero,files[i],2,sizes[i],sizes[i],16);
    if(loaded[i]==IntPtr.Zero)throw new System.ComponentModel.Win32Exception();
    var source=GeometryCurrent(loaded[i]);
    if(source[0]!=sizes[i]||source[1]!=sizes[i])throw new InvalidOperationException("Original cursor source did not decode at the observed size.");
    if(!preserveSpi[i])Match(loaded[i],sizes[i],hotX[i],hotY[i]);
   }
   for(int i=0;i<17;i++){
    if(!stillOwned())return false;
    if(preserveSpi[i])continue;
    if(!SetSystemCursor(loaded[i],(uint)Ids[i]))throw new System.ComponentModel.Win32Exception();
    loaded[i]=IntPtr.Zero;touched=true;
   }
   for(int i=0;i<17;i++){if(!stillOwned())return false;Match(LoadCursor(IntPtr.Zero,new IntPtr(Ids[i])),expected[i][0],expected[i][2],expected[i][3]);}
   if(!stillOwned())return false;
   complete=true;return true;
  }finally{
   try{foreach(IntPtr handle in loaded)if(handle!=IntPtr.Zero)DestroyCursor(handle);if(touched&&!complete&&!SystemParametersInfo(0x57,0,IntPtr.Zero,0))throw new InvalidOperationException("Current scheme refresh failed during restore rollback.");}
   finally{if(SetThreadDpiAwarenessContext(previous)==IntPtr.Zero)throw new System.ComponentModel.Win32Exception();}
  }
 }
}
