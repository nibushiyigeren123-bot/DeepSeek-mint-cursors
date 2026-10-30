using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Automation;
internal sealed class PopupCache : IDisposable {
 delegate void Callback(IntPtr hook,uint evt,IntPtr hwnd,int obj,int child,uint thread,uint time);
 [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min,uint max,IntPtr module,Callback cb,uint process,uint thread,uint flags);
 [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h,out uint process);
 readonly Callback callback;readonly IntPtr[] hooks=new IntPtr[2];readonly object gate=new object();
 readonly HashSet<IntPtr> pending=new HashSet<IntPtr>();readonly Dictionary<IntPtr,Rectangle[]> items=new Dictionary<IntPtr,Rectangle[]>();
 Rectangle[] rectangles=new Rectangle[0];bool queued;volatile bool disposed;static int semanticBusy;
 internal long Events,Checks,SemanticQueries,Dropped;internal int PeakPending;
 internal Rectangle[] Rectangles {get{return Volatile.Read(ref rectangles);}}
 internal int HookCount {get{return disposed?0:2;}}
 internal static int PendingSemanticQueries {get{return Volatile.Read(ref semanticBusy);}}
 internal PopupCache(){callback=Event;hooks[0]=SetWinEventHook(0x8000,0x8003,IntPtr.Zero,callback,0,0,2);hooks[1]=SetWinEventHook(0x800B,0x800B,IntPtr.Zero,callback,0,0,2);if(hooks[0]==IntPtr.Zero||hooks[1]==IntPtr.Zero){Dispose();throw new InvalidOperationException("Popup event hooks failed.");}foreach(var h in Win.Windows())Enqueue(h);}
 void Event(IntPtr hook,uint evt,IntPtr hwnd,int obj,int child,uint thread,uint time){if(disposed||hwnd==IntPtr.Zero||obj!=0||child!=0)return;Interlocked.Increment(ref Events);if(evt==0x8001||evt==0x8003){lock(gate){if(items.Remove(hwnd))Publish();}return;}Enqueue(hwnd);}
 void Enqueue(IntPtr h){lock(gate){if(disposed)return;if(pending.Count>=256){Dropped++;return;}pending.Add(h);PeakPending=Math.Max(PeakPending,pending.Count);if(queued)return;queued=true;ThreadPool.QueueUserWorkItem(Drain);}}
 void Drain(object unused){try{while(!disposed){IntPtr[] batch;lock(gate){if(pending.Count==0){queued=false;return;}batch=new IntPtr[pending.Count];pending.CopyTo(batch);pending.Clear();}foreach(var h in batch){if(disposed)return;Inspect(h);}}}catch(Exception){/* An inaccessible external window must not stop tracking. */}finally{lock(gate){queued=false;if(!disposed&&pending.Count>0){queued=true;ThreadPool.QueueUserWorkItem(Drain);}}}}
 void Publish(){var boxes=new List<Rectangle>();foreach(var pair in items){foreach(var r in pair.Value){if(boxes.Count>=256)break;boxes.Add(r);}if(boxes.Count>=256)break;}Volatile.Write(ref rectangles,boxes.ToArray());}
 void Store(IntPtr h,Rectangle[] boxes){lock(gate){if(disposed)return;if(!Win.IsWindowVisible(h)||boxes.Length==0){if(items.Remove(h))Publish();return;}if(items.Count>=128&&!items.ContainsKey(h))return;Rectangle[] old;if(items.TryGetValue(h,out old)&&old.Length==boxes.Length){bool same=true;for(int i=0;i<old.Length;i++)if(old[i]!=boxes[i]){same=false;break;}if(same)return;}items[h]=boxes;Publish();}}
 void Inspect(IntPtr h){if(disposed)return;Interlocked.Increment(ref Checks);if(!Win.IsWindowVisible(h)){Store(h,new Rectangle[0]);return;}string cls=Win.Class(h).ToLowerInvariant();bool native=cls.Contains("tooltips_class32")||cls.Contains("candidate")||cls.Contains("mscandui");
  if(native){Win.Rect r;if(Win.GetWindowRect(h,out r))Store(h,new[]{Rectangle.FromLTRB(r.L,r.T,r.R,r.B)});return;}
  if(!(cls.Contains("corewindow")||cls.Contains("content")||cls.Contains("popup")))return;
  uint pid;GetWindowThreadProcessId(h,out pid);try{using(var p=Process.GetProcessById((int)pid)){if(!String.Equals(p.ProcessName,"TextInputHost",StringComparison.OrdinalIgnoreCase))return;}}catch(ArgumentException){return;}
  if(Interlocked.CompareExchange(ref semanticBusy,1,0)!=0)return;
  ThreadPool.QueueUserWorkItem(delegate{try{if(disposed)return;Interlocked.Increment(ref SemanticQueries);var a=AutomationElement.FromHandle(h);var condition=new OrCondition(new PropertyCondition(AutomationElement.AutomationIdProperty,"IME_Candidate_Window"),new PropertyCondition(AutomationElement.AutomationIdProperty,"IME_Prediction_Window"),new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.ToolTip));var matches=a.FindAll(TreeScope.Element|TreeScope.Descendants,condition);var boxes=new List<Rectangle>();int n=0;foreach(AutomationElement e in matches){if(++n>64)break;if(e.Current.IsOffscreen)continue;var b=e.Current.BoundingRectangle;if(!b.IsEmpty&&b.Width>0&&b.Height>0&&b.Width<2000&&b.Height<1000)boxes.Add(Rectangle.FromLTRB((int)b.Left,(int)b.Top,(int)Math.Ceiling(b.Right),(int)Math.Ceiling(b.Bottom)));}Store(h,boxes.ToArray());}catch(ElementNotAvailableException){}catch(InvalidOperationException){}catch(COMException){}finally{Interlocked.Exchange(ref semanticBusy,0);}});
 }
 public void Dispose(){disposed=true;bool ok=true;for(int i=0;i<2;i++)if(hooks[i]!=IntPtr.Zero){if(UnhookWinEvent(hooks[i]))hooks[i]=IntPtr.Zero;else ok=false;}lock(gate){pending.Clear();items.Clear();Volatile.Write(ref rectangles,new Rectangle[0]);}if(!ok)throw new InvalidOperationException("Popup hooks did not detach.");}
}
