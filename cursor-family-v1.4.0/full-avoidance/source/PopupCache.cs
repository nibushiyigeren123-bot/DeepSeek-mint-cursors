using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Automation;

// [代码块] full.popup.cache | 范围：第 12-73 行（本次词法括号核验）。
// [功能/连接] Controller 建立事件缓存；AvatarHost 只读取矩形，不在渲染线程查询窗口或 UIA。
// [修改] v1.4.0 扩展原生 tooltip/IME 类与工具窗口 tooltip 语义；队列、查询频率及查询并发均有上限。
internal sealed class PopupCache : IDisposable {
 delegate void Callback(IntPtr hook,uint evt,IntPtr hwnd,int obj,int child,uint thread,uint time);
 [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min,uint max,IntPtr module,Callback cb,uint process,uint thread,uint flags);
 [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h,out uint process);
 readonly Callback callback;readonly IntPtr[] hooks=new IntPtr[3];readonly object gate=new object();
 readonly HashSet<IntPtr> pending=new HashSet<IntPtr>();readonly Dictionary<IntPtr,Rectangle[]> items=new Dictionary<IntPtr,Rectangle[]>();
 readonly Dictionary<IntPtr,long> queried=new Dictionary<IntPtr,long>();
 internal static readonly Rectangle[] Empty=new Rectangle[0];
 Rectangle[] rectangles=Empty;bool queued;volatile bool disposed;static int semanticBusy;
 internal long Events,Checks,SemanticQueries,Dropped;internal int PeakPending;
 internal Rectangle[] Rectangles {get{return Volatile.Read(ref rectangles);}}
 internal int HookCount {get{int count=0;foreach(var h in hooks)if(h!=IntPtr.Zero)count++;return count;}}
 internal static int PendingSemanticQueries {get{return Volatile.Read(ref semanticBusy);}}
 internal PopupCache(bool enabled){
  callback=Event;if(!enabled)return;
  hooks[0]=SetWinEventHook(0x8000,0x8003,IntPtr.Zero,callback,0,0,2);
  hooks[1]=SetWinEventHook(0x800B,0x800B,IntPtr.Zero,callback,0,0,2);
  hooks[2]=SetWinEventHook(0x800C,0x800C,IntPtr.Zero,callback,0,0,2);
  if(HookCount!=3){Dispose();throw new InvalidOperationException("Popup event hooks failed.");}
  foreach(var h in Win.Windows())Enqueue(h);
 }
 // [代码块] full.popup.event | 范围：第 37-43 行（本次词法括号核验）。
 // [功能/连接] 系统事件 → 有界待处理集合；销毁/隐藏立即使旧裁切失效，其他事件交后台检查。
 // [修改] 子元素事件仅允许已识别输入宿主走合并检查，避免键入时全屏遍历。
 void Event(IntPtr hook,uint evt,IntPtr hwnd,int obj,int child,uint thread,uint time){
  if(disposed||hwnd==IntPtr.Zero)return;
  if(obj!=0||child!=0){string cls=Win.Class(hwnd);if(!cls.Contains("CoreWindow")&&!cls.Contains("Content")&&!cls.Contains("Input"))return;}
  Interlocked.Increment(ref Events);
  if((evt==0x8001||evt==0x8003)&&obj==0&&child==0){lock(gate){pending.Remove(hwnd);queried.Remove(hwnd);if(items.Remove(hwnd))Publish();}return;}
  Enqueue(hwnd);
 }
 void Enqueue(IntPtr h){lock(gate){if(disposed)return;if(pending.Count>=256){Dropped++;return;}pending.Add(h);PeakPending=Math.Max(PeakPending,pending.Count);if(queued)return;queued=true;ThreadPool.QueueUserWorkItem(Drain);}}
 void Drain(object unused){try{while(!disposed){IntPtr[] batch;lock(gate){if(pending.Count==0){return;}batch=new IntPtr[pending.Count];pending.CopyTo(batch);pending.Clear();}foreach(var h in batch){if(disposed)return;try{Inspect(h);}catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){}}}}finally{lock(gate){queued=false;if(!disposed&&pending.Count>0){queued=true;ThreadPool.QueueUserWorkItem(Drain);}}}}
 void Publish(){var boxes=new List<Rectangle>();foreach(var pair in items){foreach(var r in pair.Value){if(boxes.Count>=256)break;boxes.Add(r);}if(boxes.Count>=256)break;}Volatile.Write(ref rectangles,boxes.ToArray());}
 void Store(IntPtr h,Rectangle[] boxes){lock(gate){if(disposed)return;if(!Win.IsWindowVisible(h)||boxes.Length==0){if(items.Remove(h))Publish();return;}if(items.Count>=128&&!items.ContainsKey(h))return;Rectangle[] old;if(items.TryGetValue(h,out old)&&old.Length==boxes.Length){bool same=true;for(int i=0;i<old.Length;i++)if(old[i]!=boxes[i]){same=false;break;}if(same)return;}items[h]=boxes;Publish();}}
 // [代码块] full.popup.inspect | 范围：第 51-71 行（本次词法括号核验）。
 // [功能/连接] 后台检查 → HWND 原生矩形或有限 UIA 子树 → Store；自绘且无语义的提示交 IdleFade。
 // [修改] 原生类增加 tooltip/IME 变体；仅 TextInputHost 或小工具弹窗查询，每窗至少250ms，最多单个查询。
 void Inspect(IntPtr h){
  if(disposed)return;Interlocked.Increment(ref Checks);if(!Win.IsWindowVisible(h)){Store(h,Empty);return;}
  string cls=Win.Class(h).ToLowerInvariant();Win.Rect r;if(!Win.GetWindowRect(h,out r))return;
  if(cls.Contains("tooltip")||cls.Contains("candidate")||cls.Contains("mscandui")||cls=="ime_candidate"||cls=="cicerouiwndframe"){
   if(r.R>r.L&&r.B>r.T)Store(h,new[]{Rectangle.FromLTRB(r.L,r.T,r.R,r.B)});return;
  }
  bool textInput=false;
  if(cls.Contains("corewindow")||cls.Contains("content")||cls.Contains("popup")||cls.Contains("input")){
   uint pid;GetWindowThreadProcessId(h,out pid);try{using(var p=Process.GetProcessById((int)pid)){textInput=String.Equals(p.ProcessName,"TextInputHost",StringComparison.OrdinalIgnoreCase);}}catch(ArgumentException){return;}
  }
  bool smallTool=(Win.Style(h,-20)&0x80)!=0&&(Win.Style(h,-16)&unchecked((int)0x80000000))!=0&&r.R-r.L<=1200&&r.B-r.T<=800;
  if(!textInput&&!smallTool)return;
  long now=Stopwatch.GetTimestamp();lock(gate){long before;if(queried.TryGetValue(h,out before)&&now-before<Stopwatch.Frequency/4)return;if(queried.Count>=256)queried.Clear();queried[h]=now;}
  if(Interlocked.CompareExchange(ref semanticBusy,1,0)!=0)return;
  ThreadPool.QueueUserWorkItem(delegate{try{
   if(disposed)return;Interlocked.Increment(ref SemanticQueries);var root=AutomationElement.FromHandle(h);
   var condition=new OrCondition(new PropertyCondition(AutomationElement.AutomationIdProperty,"IME_Candidate_Window"),new PropertyCondition(AutomationElement.AutomationIdProperty,"IME_Prediction_Window"),new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.ToolTip));
   var matches=root.FindAll(textInput?TreeScope.Element|TreeScope.Descendants:TreeScope.Element|TreeScope.Children,condition);
   var boxes=new List<Rectangle>();int n=0;foreach(AutomationElement e in matches){if(++n>64)break;if(e.Current.IsOffscreen)continue;var b=e.Current.BoundingRectangle;if(!b.IsEmpty&&b.Width>0&&b.Height>0&&b.Width<2000&&b.Height<1000)boxes.Add(Rectangle.FromLTRB((int)b.Left,(int)b.Top,(int)Math.Ceiling(b.Right),(int)Math.Ceiling(b.Bottom)));}Store(h,boxes.ToArray());
  }catch(ElementNotAvailableException){}catch(InvalidOperationException){}catch(COMException){}finally{Interlocked.Exchange(ref semanticBusy,0);}});
 }
 public void Dispose(){disposed=true;bool ok=true;for(int i=0;i<hooks.Length;i++)if(hooks[i]!=IntPtr.Zero){if(UnhookWinEvent(hooks[i]))hooks[i]=IntPtr.Zero;else ok=false;}lock(gate){pending.Clear();queried.Clear();items.Clear();Volatile.Write(ref rectangles,Empty);}if(!ok)throw new InvalidOperationException("Popup hooks did not detach.");}
}
