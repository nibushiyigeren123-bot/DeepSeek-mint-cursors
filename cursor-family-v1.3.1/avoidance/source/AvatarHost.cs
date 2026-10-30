using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Web.Script.Serialization;

// This STA owns both the HWND and its timer. It never dispatches through the
// registry/file/UIA control thread. All data crossing in is immutable.
internal sealed class AvatarHost : IDisposable {
 [StructLayout(LayoutKind.Sequential)] struct Message {internal IntPtr hwnd;internal uint id;internal UIntPtr w;internal IntPtr l;internal uint time;internal Win.Point point;internal uint extra;}
 [StructLayout(LayoutKind.Sequential)] struct RawDevice {internal ushort page,usage;internal uint flags;internal IntPtr target;}
 [DllImport("user32.dll")] static extern bool PeekMessage(out Message message,IntPtr window,uint min,uint max,uint remove);
 [DllImport("user32.dll")] static extern bool TranslateMessage(ref Message message);
 [DllImport("user32.dll")] static extern IntPtr DispatchMessage(ref Message message);
 [DllImport("user32.dll",SetLastError=true)] static extern uint MsgWaitForMultipleObjectsEx(uint count,IntPtr[] handles,uint milliseconds,uint mask,uint flags);
 [DllImport("user32.dll",SetLastError=true)] static extern bool RegisterRawInputDevices(RawDevice[] devices,uint count,uint size);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr CreateWaitableTimerEx(IntPtr attr,string name,uint flags,uint access);
 [DllImport("kernel32.dll",SetLastError=true)] static extern bool SetWaitableTimer(IntPtr timer,ref long due,int period,IntPtr completion,IntPtr arg,bool resume);
 [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
 [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
 readonly ManualResetEvent stop=new ManualResetEvent(false),ready=new ManualResetEvent(false);
 readonly AutoResetEvent update=new AutoResetEvent(false);readonly Thread thread;readonly PopupCache popups;
 CharacterFrames frames;int refresh=60;internal volatile string Error;internal volatile bool HighResolution,RawInput;
 internal long Ticks,Uploads,Moves,AvatarWindow;internal int ThreadId;internal double MaximumIntervalMs;
 internal volatile int Stage;internal uint NativeThreadId;
 internal int CanvasWidth {get{var f=Volatile.Read(ref frames);return f==null?0:f.Width;}}
 internal int CanvasHeight {get{var f=Volatile.Read(ref frames);return f==null?0:f.Height;}}
 internal int LogicalSize {get{var f=Volatile.Read(ref frames);return f==null?0:f.LogicalSize;}}
 internal string CharacterId {get{var f=Volatile.Read(ref frames);return f==null?null:f.CharacterId;}}
 internal string Edition {get{var f=Volatile.Read(ref frames);return f==null?null:f.Edition;}}
 internal long DecodedFrameBytes {get{var f=Volatile.Read(ref frames);return f==null?0:f.DecodedFrameBytes;}}
 internal int UniqueFrames {get{var f=Volatile.Read(ref frames);return f==null?0:f.UniqueFrames;}}
 internal bool Running {get{return thread.IsAlive;}}
 internal AvatarHost(PopupCache popups){this.popups=popups;thread=new Thread(Run){IsBackground=true,Name="AI cursor position and avatar"};thread.SetApartmentState(ApartmentState.STA);thread.Start();try{if(!ready.WaitOne(5000))throw new TimeoutException("Avatar STA did not start.");if(Error!=null)throw new InvalidOperationException(Error);}catch{stop.Set();if(thread.Join(5000)){stop.Dispose();update.Dispose();ready.Dispose();}throw;}}
 internal void Publish(CharacterFrames value,int hz){Volatile.Write(ref frames,value);Volatile.Write(ref refresh,Math.Max(24,Math.Min(1000,hz)));update.Set();}
 internal void PublishRefresh(int hz){hz=Math.Max(24,Math.Min(1000,hz));if(hz!=Volatile.Read(ref refresh)){Volatile.Write(ref refresh,hz);update.Set();}}
 void Run(){IntPtr timer=IntPtr.Zero;try{
  ThreadId=Thread.CurrentThread.ManagedThreadId;NativeThreadId=GetCurrentThreadId();
  using(var avatar=new Avatar(1)){
   AvatarWindow=avatar.Handle.ToInt64();timer=CreateWaitableTimerEx(IntPtr.Zero,null,2,0x1F0003);HighResolution=timer!=IntPtr.Zero;if(timer==IntPtr.Zero)timer=CreateWaitableTimerEx(IntPtr.Zero,null,0,0x1F0003);if(timer==IntPtr.Zero)throw new System.ComponentModel.Win32Exception();
   var device=new[]{new RawDevice{page=1,usage=2,flags=0x100,target=avatar.Handle}};RawInput=RegisterRawInputDevices(device,1,(uint)Marshal.SizeOf(typeof(RawDevice)));
   var waits=new[]{stop.SafeWaitHandle.DangerousGetHandle(),update.SafeWaitHandle.DangerousGetHandle(),timer};
   var clock=Stopwatch.StartNew();long lastTick=0,roleStart=0;int lastRole=-1,still=0;Win.Point previous=new Win.Point(int.MinValue,int.MinValue);CharacterFrames previousFrames=null;double next=0,nextAllowed=0;ready.Set();
   while(!stop.WaitOne(0)){
    var now=clock.Elapsed.TotalMilliseconds;
    if(now>=next||(avatar.InputPending&&now>=nextAllowed)){
     var current=Volatile.Read(ref frames);int rate=Volatile.Read(ref refresh);double interval=1000.0/rate;
     Stage=1;var ci=new NativeCursor.Info{size=(uint)Marshal.SizeOf(typeof(NativeCursor.Info))};bool visible=current!=null&&NativeCursor.GetCursorInfo(ref ci)&&(ci.flags&1)!=0;
     if(current!=previousFrames){lastRole=-1;previousFrames=current;}
     int role=-1;if(visible)for(int i=0;i<17;i++)if(ci.cursor==current.Handles[i]){role=i;break;}
     if(lastTick!=0){double gap=(clock.ElapsedTicks-lastTick)*1000.0/Stopwatch.Frequency;if(gap>MaximumIntervalMs)MaximumIntervalMs=gap;}lastTick=clock.ElapsedTicks;
     if(role<0||avatar.HiddenForTest){Stage=3;if(avatar.Visible)avatar.Hide();still++;}
     else {
      if(role!=lastRole){lastRole=role;roleStart=clock.ElapsedMilliseconds;}
      var entry=current.Roles[role];int phase=avatar.FreezeAnimation?0:(int)((clock.ElapsedMilliseconds-roleStart)/entry.Milliseconds)%entry.Frames.Length;
      Stage=2;avatar.Render(entry.Frames[phase],new Point(ci.point.X-entry.X,ci.point.Y-entry.Y),popups.Rectangles,current.Width,current.Height);
      if(ci.point.X==previous.X&&ci.point.Y==previous.Y)still++;else still=0;previous=ci.point;
     }
     Interlocked.Increment(ref Ticks);Interlocked.Exchange(ref Uploads,avatar.Uploads);Interlocked.Exchange(ref Moves,avatar.Moves);
     avatar.InputPending=false;nextAllowed=clock.Elapsed.TotalMilliseconds+interval;
     // Native mouse input wakes immediately. A 60Hz fallback also covers
     // injected/remote cursor motion, which may not produce raw input.
     next=clock.Elapsed.TotalMilliseconds+(still>=3?Math.Max(interval,1000.0/60):interval);
    }
    Stage=7;long due=-Math.Max(1L,(long)Math.Ceiling((next-clock.Elapsed.TotalMilliseconds)*10000));
    if(!SetWaitableTimer(timer,ref due,0,IntPtr.Zero,IntPtr.Zero,false))throw new System.ComponentModel.Win32Exception();
    // A late/coalesced waitable-timer signal must not leave this thread in an
    // infinite wait. The bounded wait is a local fallback, not a global timer
    // resolution change; overdue frames are still skipped rather than queued.
    uint budget=(uint)Math.Max(1,Math.Min(17,Math.Ceiling(next-clock.Elapsed.TotalMilliseconds)));
    Stage=8;uint wake=MsgWaitForMultipleObjectsEx(3,waits,budget,0x04FF,4);Stage=9;
    if(wake==0)break;if(wake==1){next=0;continue;}if(wake==0xFFFFFFFF)throw new System.ComponentModel.Win32Exception();
    Stage=10;Message m;int drained=0;while(drained++<128&&PeekMessage(out m,IntPtr.Zero,0,0,1)){TranslateMessage(ref m);DispatchMessage(ref m);}
    if(avatar.InputPending)next=Math.Min(next,nextAllowed);
   }
  }
 }catch(Exception e){Error=e.ToString();}finally{AvatarWindow=0;ready.Set();if(timer!=IntPtr.Zero)CloseHandle(timer);}}
 public void Dispose(){stop.Set();if(!thread.Join(5000))throw new InvalidOperationException("Avatar thread did not stop.");stop.Dispose();update.Dispose();ready.Dispose();}
}
