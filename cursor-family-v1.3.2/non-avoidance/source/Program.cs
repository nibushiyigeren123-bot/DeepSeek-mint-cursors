using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

internal sealed class Controller : ApplicationContext {
 sealed class Dispatch : Control {
  internal Func<int> DiagnosticStage;internal Action<int> NativeReloaded;
  protected override void WndProc(ref Message m){
#if TEST_INSTRUMENTATION
   if(m.Msg==0x8030){Thread.Sleep(Math.Max(0,Math.Min(10000,m.WParam.ToInt32())));m.Result=new IntPtr(1);return;}
   if(m.Msg==0x8032){m.Result=new IntPtr(DiagnosticStage==null?-1:DiagnosticStage());return;}
#endif
   if(m.Msg==0x8031){if(NativeReloaded!=null)NativeReloaded(m.WParam.ToInt32());m.Result=new IntPtr(1);return;}
   base.WndProc(ref m);
  }
 }
 readonly string root=AppDomain.CurrentDomain.BaseDirectory;readonly EventWaitHandle stop;
 readonly PopupCache popups;readonly AvatarHost host;readonly Dispatch dispatch=new Dispatch();
 readonly System.Windows.Forms.Timer control=new System.Windows.Forms.Timer();readonly Stopwatch clock=Stopwatch.StartNew();
 readonly Dictionary<string,string> schemes=new Dictionary<string,string>();
 readonly Dictionary<string,int> nativeSizes=new Dictionary<string,int>();
 int pendingReloadToken,lastReloadToken;
 int currentNativeSize,nativeRefreshRevision,appliedNativeRefreshRevision;
 CharacterFrames currentFrames;
 string current;long nextStatus,nextDisplayCheck,selectionMissingSince=-1;int refresh=60,switches,statusWriteRetries;bool closing,everActivated;
 volatile bool sessionEnding;
 const long StartupRecoveryMilliseconds=30000,SelectionDebounceMilliseconds=2000;
 internal bool ShouldRun {get{return !closing;}}
 // [代码块] original.controller.start | 范围：第 39-44 行（括号核验）。
 // [连接] Program.Main → Controller；绑定 SystemEvents 与 control.Tick → Keep。
 // [修改] 2026-10-31 plan v2：读取profile原生尺寸并订阅Mouse偏好通知；保留30秒启动恢复。
 // [功能] 建立控制与渲染线程；订阅登录生命周期后开始方案核对。
 internal Controller(EventWaitHandle signal){stop=signal;IntPtr handle=dispatch.Handle;
  var rows=(object[])new JavaScriptSerializer().DeserializeObject(File.ReadAllText(Path.Combine(root,"characters.json")));
  foreach(Dictionary<string,object> row in rows){string id=(string)row["id"];int size=Convert.ToInt32(row["expectedNativeSize"]);if(Array.IndexOf(new[]{32,64,96,128,256},size)<0)throw new InvalidDataException("Unsupported profile native size.");schemes.Add((string)row["scheme"],id);nativeSizes.Add(id,size);}
  popups=new PopupCache();try{host=new AvatarHost(popups);}catch{popups.Dispose();dispatch.Dispose();throw;}dispatch.DiagnosticStage=delegate{return host.Stage;};dispatch.NativeReloaded=delegate(int token){pendingReloadToken=token;};control.Interval=500;control.Tick+=Keep;
  try{SystemEvents.SessionEnding+=OnSessionEnding;SystemEvents.SessionEnded+=OnSessionEnded;SystemEvents.UserPreferenceChanged+=OnPreferenceChanged;SystemEvents.DisplaySettingsChanged+=OnDisplaySettingsChanged;Keep(null,EventArgs.Empty);if(!closing)control.Start();}catch{Dispose();throw;}
 }
 // [代码块] prototype.controller.selected | 范围：第 48-52 行（括号核验）。
 // [连接] Keep/ApplyProfile所有权回调 → 注册表方案、17路径与profile逻辑尺寸。
 // [修改] 2026-10-31 plan v2：采用每profile目标尺寸，外部大小/主题变更仍判失权，不写回抢选。
 string Selected(){using(var key=Registry.CurrentUser.OpenSubKey(@"Control Panel\Cursors")){
  string id;if(key==null||!schemes.TryGetValue(key.GetValue("","") as string,out id)||Convert.ToInt32(key.GetValue("CursorBaseSize",32))!=nativeSizes[id])return null;
  foreach(string role in NativeCursor.Names){string value=Environment.ExpandEnvironmentVariables(key.GetValue(role,"") as string??"");if(String.IsNullOrWhiteSpace(value)||!String.Equals(Path.GetFullPath(value),Path.Combine(root,"assets",id,role,"symbol.ani"),StringComparison.OrdinalIgnoreCase))return null;}
  return id;
 }}
 // [代码块] prototype.controller.preference | 范围：第 56-56 行（括号核验）。
 // [连接] SystemEvents鼠标偏好通知 → 控制线程下个Keep；不在事件线程加载/GDI读回。
 // [修改] 2026-10-31 plan v2：同一路径被Windows刷新时重新提交整组，沿用原500ms控制计时。
 void OnPreferenceChanged(object sender,UserPreferenceChangedEventArgs args){if(args.Category==UserPreferenceCategory.Mouse)Interlocked.Increment(ref nativeRefreshRevision);}
 // [修改] plan v3：显示设置变更仅标记下个500ms控制周期，集中刷新实际geometry缓存。
 // [代码块] prototype.controller.display | 范围：第 59-59 行（括号核验）。
 void OnDisplaySettingsChanged(object sender,EventArgs args){Interlocked.Increment(ref nativeRefreshRevision);}
 // [代码块] original.controller.session-ending | 范围：第 64-64 行（括号核验）。
 // [连接] SystemEvents.SessionEnding → sessionEnding；Keep 读取该标志。
 // [修改] 2026-10-30 cursor-final：注销/关机通知优先保留自启动，不在事件线程操作窗口。
 // [功能] 标记会话即将结束，避免暂时失配被当成用户切换方案。
 void OnSessionEnding(object sender,SessionEndingEventArgs args){sessionEnding=true;}
 // [代码块] original.controller.session-ended | 范围：第 69-69 行（括号核验）。
 // [连接] SystemEvents.SessionEnded → sessionEnding；Keep 读取该标志。
 // [修改] 2026-10-30 cursor-final：补充会话结束通知，Dispose 解除静态事件订阅。
 // [功能] 保留会话结束路径中的登录项。
 void OnSessionEnded(object sender,SessionEndedEventArgs args){sessionEnding=true;}
 // [代码块] original.controller.suspend | 范围：第 74-74 行（括号核验）。
 // [连接] Keep → SuspendSelection → AvatarHost.Publish(null,refresh)。
 // [修改] 2026-10-30 cursor-final：失配即清除当前人物与已发布帧，不抢选或写回注册表。
 // [功能] 暂停人物显示，让状态文件与渲染线程都反映未激活。
 void SuspendSelection(){if(current!=null){host.Publish(null,refresh);current=null;currentFrames=null;currentNativeSize=0;nextStatus=0;}}
 // [代码块] original.controller.keep | 范围：第 79-109 行（括号核验）。
 // [连接] 构造函数/control.Tick → Keep；读取 Selected，发布 AvatarHost，调用 Save/ExitThread。
 // [修改] 2026-10-31 plan v2：激活/重载/鼠标设置刷新时提交完整17原生符号；保持30秒启动与2秒失配去抖。
 // [功能] 维护当前方案、人物数据与自启动退出原因，安装期间重置失配计时。
 void Keep(object sender,EventArgs args){try{
  if(stop.WaitOne(0)||host.Error!=null){if(host.Error!=null)throw new InvalidOperationException(host.Error);ExitThread();return;}
  string selected=Selected();long now=clock.ElapsedMilliseconds;
  if(selected==null){
   SuspendSelection();
   if(Program.InstallerOperationActive()||now<StartupRecoveryMilliseconds){selectionMissingSince=-1;Save(false);return;}
   // 从未成功激活或正在结束会话时只退出；登录项留给下一次正常登录恢复。
   if(!everActivated||sessionEnding){ExitThread();return;}
   if(selectionMissingSince<0)selectionMissingSince=now;
   if(now-selectionMissingSince>=SelectionDebounceMilliseconds){
    if(!sessionEnding)Program.RemoveOwnedStartup();
    ExitThread();return;
   }
   Save(false);return;
  }
  selectionMissingSince=-1;
  if(now>=nextDisplayCheck){refresh=DisplayRefresh.ForCursor();nextDisplayCheck=now+2000;}
  int refreshRevision=Volatile.Read(ref nativeRefreshRevision);
  if(selected!=current||pendingReloadToken!=0||refreshRevision!=appliedNativeRefreshRevision){
   int reloadToken=pendingReloadToken;var data=new CharacterFrames(root,selected);
   if(data.ExpectedNativeSize!=nativeSizes[selected])throw new InvalidDataException("Catalog and frame native sizes differ.");
   if(Selected()!=selected){SuspendSelection();Save(false);return;}
   if(!NativeCursor.ApplyProfile(root,data,delegate{return Selected()==selected;})){SuspendSelection();Save(false);return;}
   if(Selected()!=selected){SuspendSelection();NativeCursor.ReloadCurrentScheme();Save(false);return;}
   NativeCursor.CachePhysicalLayout(data);
   if(Selected()!=selected){SuspendSelection();NativeCursor.ReloadCurrentScheme();Save(false);return;}
   var handles=NativeCursor.Handles();Array.Copy(handles,data.Handles,17);pendingReloadToken=0;lastReloadToken=reloadToken;
   current=selected;currentFrames=data;currentNativeSize=data.ExpectedNativeSize;appliedNativeRefreshRevision=refreshRevision;everActivated=true;switches++;host.Publish(data,refresh);nextStatus=0;
  }else host.PublishRefresh(refresh);
  Save(false);
 }catch(Exception e){File.AppendAllText(Path.Combine(root,"error.txt"),e+Environment.NewLine);ExitThread();}}
 // [代码块] original.controller.status | 范围：第 114-127 行（括号核验）。
 // [连接] Keep/ExitThreadCore → Save → status.json；人物身份来自 current 与 AvatarHost。
 // [修改] 2026-10-31 plan v2：报告实际profile原生逻辑画布与提交修订号；暂停清零，不声称各DPI物理恒定。
 // [功能] 输出当前渲染与线程遥测；文件占用时沿用重试。
 void Save(bool force){if(!force&&clock.ElapsedMilliseconds<nextStatus)return;nextStatus=clock.ElapsedMilliseconds+2000;
  var state=new Dictionary<string,object>();using(var self=Process.GetCurrentProcess()){state["pid"]=self.Id;state["startedUtcTicks"]=self.StartTime.ToUniversalTime().Ticks;}
  state["utc"]=DateTime.UtcNow.ToString("o");state["nativeReloadToken"]=lastReloadToken;state["active"]=!closing&&current!=null;state["character"]=current;state["loadedCharacters"]=!closing&&current!=null?1:0;state["characterSwitches"]=switches;state["statusWriteRetries"]=statusWriteRetries;state["frameTicks"]=Interlocked.Read(ref host.Ticks);state["uploads"]=Interlocked.Read(ref host.Uploads);state["moves"]=Interlocked.Read(ref host.Moves);
  state["trackingStage"]=host.Stage;state["trackingNativeThreadId"]=host.NativeThreadId;state["trackingHz"]=refresh;state["highResolutionTrackingTimer"]=host.HighResolution;state["trackingThreadAlive"]=host.Running;state["rawMouseInput"]=host.RawInput;state["callbackIntervalMaxMs"]=host.MaximumIntervalMs;state["avatarHwnd"]=Interlocked.Read(ref host.AvatarWindow);state["controlHwnd"]=dispatch.Handle.ToInt64();state["activeWindowHooks"]=popups.HookCount;state["nativeWindowEvents"]=Interlocked.Read(ref popups.Events);state["nativePopupChecks"]=Interlocked.Read(ref popups.Checks);state["semanticQueries"]=Interlocked.Read(ref popups.SemanticQueries);state["pendingSemanticQueries"]=PopupCache.PendingSemanticQueries;state["popupQueuePeak"]=popups.PeakPending;state["popupQueueDropped"]=Interlocked.Read(ref popups.Dropped);state["popupRects"]=popups.Rectangles;state["systemSymbolCanvas"]=currentNativeSize;state["systemSymbolDpiBasis"]="unaware-logical";state["completeNativeSymbols"]=true;state["nativeRefreshRevision"]=appliedNativeRefreshRevision;state["avoidance"]=false;state["frozenOriginalLayout"]=true;state["characterCanvas"]=new[]{host.CanvasWidth,host.CanvasHeight};state["logicalSize"]=host.LogicalSize;state["baseCharacter"]=host.CharacterId;state["edition"]=host.Edition;state["decodedFrameBytes"]=host.DecodedFrameBytes;state["uniqueFrames"]=host.UniqueFrames;state["guardProcess"]=false;state["trayIcon"]=false;
  // [修改] plan v3：公开缓存的17role实际geometry与固定间距，让VM核对真实窗口位置。
  var geometry=new List<object>();bool originalLayout=true;
  if(currentFrames!=null)for(int i=0;i<17;i++){var r=currentFrames.Roles[i];geometry.Add(new Dictionary<string,object>{{"role",NativeCursor.Names[i]},{"physicalCanvas",new[]{r.PhysicalWidth,r.PhysicalHeight}},{"physicalHotspot",new[]{r.PhysicalHotX,r.PhysicalHotY}},{"logicalVisibleBounds",new[]{r.BoundsLeft,r.BoundsTop,r.BoundsRight,r.BoundsBottom}},{"bodyOffset",new[]{r.LayoutOffsetX,r.LayoutOffsetY}}});if(r.LayoutOffsetX!=0||r.LayoutOffsetY!=0)originalLayout=false;}
  state["nativePhysicalGeometry"]=geometry;state["dpiLayoutCompensation"]=true;state["frozenOriginalLayoutAt100Percent"]=true;state["frozenOriginalLayout"]=originalLayout;
  string target=Path.Combine(root,"status.json"),temp=target+".tmp";
  // A status reader may hold a handle without FILE_SHARE_DELETE. Telemetry
  // must not end an otherwise healthy cursor session because replace is busy.
  try{File.WriteAllText(temp,new JavaScriptSerializer().Serialize(state));if(File.Exists(target))File.Replace(temp,target,null);else File.Move(temp,target);}
  catch(IOException){statusWriteRetries++;nextStatus=0;}
 }
 // [代码块] original.controller.exit | 范围：第 132-132 行（括号核验）。
 // [连接] ApplicationContext/Keep → ExitThreadCore → AvatarHost.Dispose、PopupCache.Dispose、Save。
 // [修改] 2026-10-30 cursor-final：移除无条件删除 Run；显式 --stop 与确认方案切换各自负责删除。
 // [功能] 结束本次进程的渲染与控制，普通退出、异常退出和会话结束保留登录项。
 protected override void ExitThreadCore(){if(!closing){closing=true;control.Stop();SuspendSelection();host.Dispose();popups.Dispose();Save(true);}base.ExitThreadCore();}
 // [代码块] original.controller.dispose | 范围：第 137-137 行（括号核验）。
 // [连接] Program.Main 的 using/构造失败 → Dispose；解绑 SystemEvents 静态事件。
 // [修改] 2026-10-31 plan v2：同时解绑Mouse偏好与生命周期事件；释放资源不删除Run。
 // [功能] 释放控制器持有的定时器、窗口与跟踪线程。
 protected override void Dispose(bool disposing){if(disposing){SystemEvents.SessionEnding-=OnSessionEnding;SystemEvents.SessionEnded-=OnSessionEnded;SystemEvents.UserPreferenceChanged-=OnPreferenceChanged;SystemEvents.DisplaySettingsChanged-=OnDisplaySettingsChanged;if(!closing){closing=true;control.Stop();SuspendSelection();host.Dispose();popups.Dispose();}control.Dispose();dispatch.Dispose();}base.Dispose(disposing);}
}
internal static class Program {
 internal const string StartupName="AICursorFamilyNonAvoidanceR1";
 internal static bool InstallerOperationActive(){using(var identity=WindowsIdentity.GetCurrent()){try{using(var mutex=Mutex.OpenExisting("Local\\AICursorFamilyInstallerOriginalR1-"+identity.User.Value)){bool acquired;try{acquired=mutex.WaitOne(0);}catch(AbandonedMutexException){acquired=true;}if(acquired)mutex.ReleaseMutex();return !acquired;}}catch(WaitHandleCannotBeOpenedException){return false;}}}

 static string Name(string suffix){using(var identity=WindowsIdentity.GetCurrent())using(var self=Process.GetCurrentProcess())return "Local\\AICursorFamilyOriginalR1-"+identity.User.Value+"-"+self.SessionId+"-"+suffix;}
 internal static void RemoveOwnedStartup(){using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run",true)){if(key!=null&&String.Equals(key.GetValue(StartupName) as string,"\""+Application.ExecutablePath+"\"",StringComparison.OrdinalIgnoreCase))key.DeleteValue(StartupName,false);}}
 [STAThread] static int Main(string[] args){try{
  if(args.Length==1&&args[0]=="--stop"){try{using(var signal=EventWaitHandle.OpenExisting(Name("stop")))signal.Set();}catch(WaitHandleCannotBeOpenedException){}RemoveOwnedStartup();try{using(var mutex=Mutex.OpenExisting(Name("mutex"))){bool owns;try{owns=mutex.WaitOne(8000);}catch(AbandonedMutexException){owns=true;}if(owns)mutex.ReleaseMutex();return owns?0:2;}}catch(WaitHandleCannotBeOpenedException){return 0;}}
  bool created;using(var mutex=new Mutex(true,Name("mutex"),out created)){if(!created)return 2;try{using(var signal=new EventWaitHandle(false,EventResetMode.ManualReset,Name("stop"))){signal.Reset();Win.SetProcessDPIAware();Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);using(var context=new Controller(signal)){if(context.ShouldRun)Application.Run(context);}}}finally{mutex.ReleaseMutex();}}return 0;
 }catch(Exception e){File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"fatal.txt"),e+Environment.NewLine);return 1;}}
}
