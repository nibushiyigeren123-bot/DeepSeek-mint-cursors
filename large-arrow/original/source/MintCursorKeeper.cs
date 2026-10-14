using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

internal static class MintCursorKeeper {
 internal const string Version="1.1.0";
 internal const string RegistryPath=@"Control Panel\Cursors";
 internal const string ObjectPrefix="DeepSeekMintCursorKeeper";
 internal static readonly string[] Roles={"Arrow","Help","AppStarting","Wait","Crosshair","IBeam","NWPen","No","SizeNS","SizeWE","SizeNWSE","SizeNESW","SizeAll","UpArrow","Hand","Pin","Person"};
 internal static readonly uint[] Ids={32512,32651,32650,32514,32515,32513,32631,32648,32645,32644,32642,32643,32646,32516,32649,32671,32672};
 internal static readonly string Root=Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)).FullName;
 internal static readonly string Disabled=Path.Combine(Root,"keeper","disabled.flag");
 internal static readonly string Status=Path.Combine(Root,"keeper","status.txt");
 internal static readonly string LogPath=Path.Combine(Root,"keeper","keeper.log");
 internal static string Name(string suffix){return @"Local\"+ObjectPrefix+"-"+WindowsIdentity.GetCurrent().User.Value+"-"+Process.GetCurrentProcess().SessionId+"-"+suffix;}
 internal static void Log(string text){
  try{if(File.Exists(LogPath)&&new FileInfo(LogPath).Length>65536){string old=LogPath+".previous";if(File.Exists(old))File.Delete(old);File.Move(LogPath,old);}File.AppendAllText(LogPath,DateTime.UtcNow.ToString("o")+" "+text+Environment.NewLine,new UTF8Encoding(false));}catch(IOException){}catch(UnauthorizedAccessException){}
 }
 internal static void WriteStatus(string state){
  try{File.WriteAllText(Status,"version="+Version+"\r\npid="+Process.GetCurrentProcess().Id+"\r\nutc="+DateTime.UtcNow.ToString("o")+"\r\n"+state+"\r\n",new UTF8Encoding(false));}catch(IOException){}catch(UnauthorizedAccessException){}
 }
 internal sealed class Selection {
  internal string Character,Scheme,Signature;internal int Size;internal string[] Files;
 }
 internal static Selection ReadSelection(){
  using(RegistryKey key=Registry.CurrentUser.OpenSubKey(RegistryPath)){
   if(key==null)return null;
   string scheme=key.GetValue("","") as string;
   Match match=Regex.Match(scheme??"",@"^(DeepSeek) Mint (32|48|64|128|256)px$",RegexOptions.IgnoreCase);
   if(!match.Success)return null;
   Selection s=new Selection();s.Character=match.Groups[1].Value.ToLowerInvariant();s.Size=Int32.Parse(match.Groups[2].Value);s.Scheme=scheme;s.Files=new string[17];
   StringBuilder signature=new StringBuilder(s.Character+"|"+s.Size);
   string selectedRoot=null;
   string windowsRoot=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"Cursors","DeepSeekMintCursors");
   for(int i=0;i<17;i++){
    string path=key.GetValue(Roles[i],"") as string;if(String.IsNullOrWhiteSpace(path))return null;
    path=Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
    if(i==0){foreach(string candidate in new string[]{Root,windowsRoot}){string expected=Path.GetFullPath(Path.Combine(candidate,"cursors",s.Character,s.Size.ToString(),Roles[i]+".ani"));if(String.Equals(path,expected,StringComparison.OrdinalIgnoreCase))selectedRoot=candidate;}if(selectedRoot==null)return null;}
    string expectedPath=Path.GetFullPath(Path.Combine(selectedRoot,"cursors",s.Character,s.Size.ToString(),Roles[i]+".ani"));
    if(!String.Equals(path,expectedPath,StringComparison.OrdinalIgnoreCase)||!File.Exists(path))return null;
    s.Files[i]=path;signature.Append('|').Append(path.ToLowerInvariant());
   }
   // Re-read the name to reject a selection changing while the 17 values were read.
   if(!String.Equals(scheme,key.GetValue("","") as string,StringComparison.Ordinal))return null;
   s.Signature=signature.ToString();return s;
  }
 }
 internal static bool StillSelected(Selection s){Selection now=ReadSelection();return now!=null&&now.Signature==s.Signature&&!File.Exists(Disabled);}
 internal static bool AllExact(Selection s){try{for(int i=0;i<17;i++){if(!Native.Exact(Native.Current(Ids[i]),s.Size))return false;}return true;}catch(InvalidOperationException){return false;}}
 internal static bool Apply(Selection s,out bool sizeConfirmed){
   sizeConfirmed=false;
  IntPtr[] owned=new IntPtr[17];bool touched=false;
  try{
   for(int i=0;i<17;i++){owned[i]=Native.Load(s.Files[i],s.Size);if(owned[i]==IntPtr.Zero)throw new InvalidOperationException("Cannot load "+Roles[i]+" at "+s.Size+"px; error="+Marshal.GetLastWin32Error());}
   for(int i=0;i<17;i++){
    if(!StillSelected(s)){if(touched&&!Native.Reload())Log("Selection-change refresh failed; error="+Marshal.GetLastWin32Error());return false;}
    if(!Native.Replace(owned[i],Ids[i]))throw new InvalidOperationException("Cannot replace "+Roles[i]+"; error="+Marshal.GetLastWin32Error());
    owned[i]=IntPtr.Zero;touched=true;
   }
   if(!StillSelected(s)){if(!Native.Reload())Log("Final verification refresh failed; error="+Marshal.GetLastWin32Error());return false;}
   sizeConfirmed=AllExact(s);
   return true;
  }catch{if(touched&&!Native.Reload())Log("Rollback refresh failed; error="+Marshal.GetLastWin32Error());throw;}
  finally{foreach(IntPtr handle in owned)if(handle!=IntPtr.Zero)Native.Free(handle);}
 }
 internal sealed class KeeperContext:ApplicationContext {
  private readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer();
  private readonly EventWaitHandle stop;
  private string pending,lastApplied,lastState;private bool sizeReadbackUnavailable;private int sameTicks;private DateTime retryAfter=DateTime.MinValue;
  internal KeeperContext(EventWaitHandle stopEvent){stop=stopEvent;timer.Interval=500;timer.Tick+=Tick;timer.Start();WriteStatus("state=watching");}
  private void Tick(object sender,EventArgs args){
   if(stop.WaitOne(0)||File.Exists(Disabled)){timer.Stop();WriteStatus("state=stopped");ExitThread();return;}
   if(DateTime.UtcNow<retryAfter)return;
   try{
    Selection s=ReadSelection();
    if(s==null){pending=null;lastApplied=null;sameTicks=0;retryAfter=DateTime.MinValue;if(lastState!="inactive"){lastState="inactive";WriteStatus("state=inactive-other-scheme");}return;}
    if(pending!=s.Signature){pending=s.Signature;sameTicks=1;retryAfter=DateTime.MinValue;return;}
    sameTicks++;if(sameTicks<2||DateTime.UtcNow<retryAfter)return;
    if(lastApplied==s.Signature&&(sizeReadbackUnavailable||AllExact(s)))return;
    bool sizeConfirmed;
     if(Apply(s,out sizeConfirmed)){
      lastApplied=s.Signature;lastState=s.Signature;sizeReadbackUnavailable=!sizeConfirmed;
      WriteStatus("state="+(sizeConfirmed?"applied":"readback-unconfirmed")+"\r\ncharacter="+s.Character+"\r\nsize="+s.Size+"\r\nroles=17");
      Log("Applied "+s.Character+" "+s.Size+"px; all 17 replacements succeeded.");
      if(!sizeConfirmed)Log("WARNING: Cursor bitmap size does not confirm the requested size. Automatic correction suspended for this selection to avoid flicker; actual display size depends on Windows.");
     }
    else{lastApplied=null;retryAfter=DateTime.UtcNow.AddSeconds(2);}
   }catch(Exception e){lastApplied=null;retryAfter=DateTime.UtcNow.AddSeconds(5);Log("Correction failed: "+e.GetType().Name+": "+e.Message);WriteStatus("state=error\r\nerror="+e.GetType().Name);}
  }
  protected override void Dispose(bool disposing){if(disposing)timer.Dispose();base.Dispose(disposing);}
 }
 [STAThread] private static int Main(string[] args){
  try{
   if(args.Length==1&&args[0]=="--status"){
    try{using(Mutex m=Mutex.OpenExisting(Name("mutex"))){bool free=false;try{free=m.WaitOne(0);}catch(AbandonedMutexException){free=true;}if(free)m.ReleaseMutex();return free?1:0;}}catch(WaitHandleCannotBeOpenedException){return 1;}
   }
   if(args.Length==1&&args[0]=="--stop"){
    try{using(EventWaitHandle e=EventWaitHandle.OpenExisting(Name("stop")))e.Set();}catch(WaitHandleCannotBeOpenedException){}
    try{using(Mutex m=Mutex.OpenExisting(Name("mutex"))){bool free=false;try{free=m.WaitOne(8000);}catch(AbandonedMutexException){free=true;}if(free)m.ReleaseMutex();return free?0:2;}}catch(WaitHandleCannotBeOpenedException){return 0;}
   }
   if(args.Length!=0)return 3;if(File.Exists(Disabled))return 0;
   bool created;using(Mutex mutex=new Mutex(true,Name("mutex"),out created)){
    bool owns=created;if(!owns){try{owns=mutex.WaitOne(0);}catch(AbandonedMutexException){owns=true;}}
    if(!owns)return 0;
    try{using(EventWaitHandle stop=new EventWaitHandle(false,EventResetMode.ManualReset,Name("stop"))){stop.Reset();using(KeeperContext context=new KeeperContext(stop))Application.Run(context);}}finally{mutex.ReleaseMutex();}
   }
   return 0;
  }catch(Exception e){Log("Process failed: "+e);return 1;}
 }
 internal static class Native {
  [StructLayout(LayoutKind.Sequential)] private struct IconInfo{public int isIcon;public uint x,y;public IntPtr mask,color;}
  [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo{public int type,width,height,widthBytes;public ushort planes,bits;public IntPtr pixels;}
  [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern IntPtr LoadImageW(IntPtr module,string path,uint type,int width,int height,uint flags);
  [DllImport("user32.dll",EntryPoint="LoadCursorW",SetLastError=true)] private static extern IntPtr LoadCursorW(IntPtr module,IntPtr id);
  [DllImport("user32.dll",SetLastError=true)] private static extern bool SetSystemCursor(IntPtr cursor,uint id);
  [DllImport("user32.dll")] private static extern bool DestroyCursor(IntPtr cursor);
  [DllImport("user32.dll",SetLastError=true)] private static extern bool GetIconInfo(IntPtr cursor,out IconInfo info);
  [DllImport("gdi32.dll",EntryPoint="GetObjectW")] private static extern int GetObjectW(IntPtr bitmap,int bytes,out BitmapInfo info);
  [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr bitmap);
  [DllImport("user32.dll",SetLastError=true)] private static extern bool SystemParametersInfo(uint action,uint parameter,IntPtr value,uint flags);
  internal static IntPtr Current(uint id){return LoadCursorW(IntPtr.Zero,new IntPtr((long)id));}
  [DllImport("user32.dll",SetLastError=true)] private static extern IntPtr CreateIconFromResourceEx([In] byte[] bits,uint length,[MarshalAs(UnmanagedType.Bool)] bool icon,uint version,int width,int height,uint flags);
  private static IntPtr LoadCursorPixels(string path,int size){
   byte[] bits=File.ReadAllBytes(path);
   if(bits.Length>=12&&bits[0]==82&&bits[1]==73&&bits[2]==70&&bits[3]==70&&bits[8]==65&&bits[9]==67&&bits[10]==79&&bits[11]==78)
    return CreateIconFromResourceEx(bits,(uint)bits.Length,false,0x00030000,size,size,0);
   return LoadImageW(IntPtr.Zero,path,2,size,size,16);
  }
  internal static IntPtr Load(string path,int size){return LoadCursorPixels(path,size);}
  internal static bool Replace(IntPtr owned,uint id){return SetSystemCursor(owned,id);}
  internal static void Free(IntPtr owned){DestroyCursor(owned);}
  internal static bool Reload(){return SystemParametersInfo(0x57,0,IntPtr.Zero,0);}
  internal static bool Exact(IntPtr cursor,int size){
   IconInfo icon;if(cursor==IntPtr.Zero||!GetIconInfo(cursor,out icon))throw new InvalidOperationException("Cannot read live cursor; error="+Marshal.GetLastWin32Error());
   try{BitmapInfo bitmap;if(GetObjectW(icon.color!=IntPtr.Zero?icon.color:icon.mask,Marshal.SizeOf(typeof(BitmapInfo)),out bitmap)==0)throw new InvalidOperationException("Cannot read cursor bitmap; error="+Marshal.GetLastWin32Error());return bitmap.width==size&&(icon.color!=IntPtr.Zero?bitmap.height:bitmap.height/2)==size;}
   finally{if(icon.mask!=IntPtr.Zero)DeleteObject(icon.mask);if(icon.color!=IntPtr.Zero)DeleteObject(icon.color);}
  }
 }
}
