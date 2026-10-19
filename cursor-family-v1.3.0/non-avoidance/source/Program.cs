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
 int pendingReloadToken,lastReloadToken;
 string current;long nextStatus,nextDisplayCheck;int refresh=60,switches,statusWriteRetries;bool closing;
 internal bool ShouldRun {get{return !closing;}}
 internal Controller(EventWaitHandle signal){stop=signal;IntPtr handle=dispatch.Handle;
  var rows=(object[])new JavaScriptSerializer().DeserializeObject(File.ReadAllText(Path.Combine(root,"characters.json")));
  foreach(Dictionary<string,object> row in rows)schemes.Add((string)row["scheme"],(string)row["id"]);
  popups=new PopupCache();try{host=new AvatarHost(popups);}catch{popups.Dispose();dispatch.Dispose();throw;}dispatch.DiagnosticStage=delegate{return host.Stage;};dispatch.NativeReloaded=delegate(int token){pendingReloadToken=token;};control.Interval=500;control.Tick+=Keep;
  try{Keep(null,EventArgs.Empty);if(!closing)control.Start();}catch{Dispose();throw;}
 }
 string Selected(){using(var key=Registry.CurrentUser.OpenSubKey(@"Control Panel\Cursors")){
  string id;if(key==null||Convert.ToInt32(key.GetValue("CursorBaseSize",32))!=32||!schemes.TryGetValue(key.GetValue("","") as string,out id))return null;
  foreach(string role in NativeCursor.Names){string value=Environment.ExpandEnvironmentVariables(key.GetValue(role,"") as string??"");if(String.IsNullOrWhiteSpace(value)||!String.Equals(Path.GetFullPath(value),Path.Combine(root,"assets",id,role,"symbol.ani"),StringComparison.OrdinalIgnoreCase))return null;}
  return id;
 }}
 void Keep(object sender,EventArgs args){try{
  if(stop.WaitOne(0)||host.Error!=null){if(host.Error!=null)throw new InvalidOperationException(host.Error);ExitThread();return;}
  string selected=Selected();if(selected==null){if(Program.InstallerOperationActive())return;ExitThread();return;}
  if(clock.ElapsedMilliseconds>=nextDisplayCheck){refresh=DisplayRefresh.ForCursor();nextDisplayCheck=clock.ElapsedMilliseconds+2000;}
  if(selected!=current||pendingReloadToken!=0){int reloadToken=pendingReloadToken;var data=new CharacterFrames(root,selected);if(Selected()!=selected)return;if(!NativeCursor.ApplyPerson32(root,selected,delegate{return Selected()==selected;}))return;if(Selected()!=selected){NativeCursor.ReloadCurrentScheme();return;}var handles=NativeCursor.Handles();Array.Copy(handles,data.Handles,17);pendingReloadToken=0;lastReloadToken=reloadToken;current=selected;switches++;host.Publish(data,refresh);nextStatus=0;}
  else host.PublishRefresh(refresh);
  Save(false);
 }catch(Exception e){File.AppendAllText(Path.Combine(root,"error.txt"),e+Environment.NewLine);ExitThread();}}
 void Save(bool force){if(!force&&clock.ElapsedMilliseconds<nextStatus)return;nextStatus=clock.ElapsedMilliseconds+2000;
  var state=new Dictionary<string,object>();using(var self=Process.GetCurrentProcess()){state["pid"]=self.Id;state["startedUtcTicks"]=self.StartTime.ToUniversalTime().Ticks;}
  state["utc"]=DateTime.UtcNow.ToString("o");state["nativeReloadToken"]=lastReloadToken;state["active"]=!closing;state["character"]=current;state["loadedCharacters"]=closing?0:1;state["characterSwitches"]=switches;state["statusWriteRetries"]=statusWriteRetries;state["frameTicks"]=Interlocked.Read(ref host.Ticks);state["uploads"]=Interlocked.Read(ref host.Uploads);state["moves"]=Interlocked.Read(ref host.Moves);
  state["trackingStage"]=host.Stage;state["trackingNativeThreadId"]=host.NativeThreadId;state["trackingHz"]=refresh;state["highResolutionTrackingTimer"]=host.HighResolution;state["trackingThreadAlive"]=host.Running;state["rawMouseInput"]=host.RawInput;state["callbackIntervalMaxMs"]=host.MaximumIntervalMs;state["avatarHwnd"]=Interlocked.Read(ref host.AvatarWindow);state["controlHwnd"]=dispatch.Handle.ToInt64();state["activeWindowHooks"]=popups.HookCount;state["nativeWindowEvents"]=Interlocked.Read(ref popups.Events);state["nativePopupChecks"]=Interlocked.Read(ref popups.Checks);state["semanticQueries"]=Interlocked.Read(ref popups.SemanticQueries);state["pendingSemanticQueries"]=PopupCache.PendingSemanticQueries;state["popupQueuePeak"]=popups.PeakPending;state["popupQueueDropped"]=Interlocked.Read(ref popups.Dropped);state["popupRects"]=popups.Rectangles;state["systemSymbolCanvas"]=32;state["avoidance"]=false;state["frozenOriginalLayout"]=true;state["characterCanvas"]=new[]{host.CanvasWidth,host.CanvasHeight};state["logicalSize"]=host.LogicalSize;state["baseCharacter"]=host.CharacterId;state["edition"]=host.Edition;state["decodedFrameBytes"]=host.DecodedFrameBytes;state["uniqueFrames"]=host.UniqueFrames;state["guardProcess"]=false;state["trayIcon"]=false;
  string target=Path.Combine(root,"status.json"),temp=target+".tmp";
  // A status reader may hold a handle without FILE_SHARE_DELETE. Telemetry
  // must not end an otherwise healthy cursor session because replace is busy.
  try{File.WriteAllText(temp,new JavaScriptSerializer().Serialize(state));if(File.Exists(target))File.Replace(temp,target,null);else File.Move(temp,target);}
  catch(IOException){statusWriteRetries++;nextStatus=0;}
 }
 protected override void ExitThreadCore(){if(!closing){closing=true;control.Stop();host.Dispose();popups.Dispose();Program.RemoveOwnedStartup();Save(true);}base.ExitThreadCore();}
 protected override void Dispose(bool disposing){if(disposing){if(!closing){closing=true;control.Stop();host.Dispose();popups.Dispose();}control.Dispose();dispatch.Dispose();}base.Dispose(disposing);}
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
