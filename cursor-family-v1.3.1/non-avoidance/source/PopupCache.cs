using System;using System.Drawing;
// Non-avoidance has no popup hooks, window inspection, UIA or clipping cuts.
internal sealed class PopupCache : IDisposable {
 internal long Events=0,Checks=0,SemanticQueries=0,Dropped=0;internal int PeakPending=0;
 static readonly Rectangle[] Empty=new Rectangle[0];
 internal Rectangle[] Rectangles {get{return Empty;}}
 internal int HookCount {get{return 0;}}
 internal static int PendingSemanticQueries {get{return 0;}}
 public void Dispose(){}
}
