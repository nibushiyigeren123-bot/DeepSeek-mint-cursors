using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

// [代码块] full.settings | 范围：第 9-22 行（本次词法括号核验）。
// [功能/连接] Controller 启动读取 runtime/avoidance-settings.json；AvatarHost 使用不可变配置。
// [修改] v1.4.0 新增少量持久选项；损坏配置明确报错，禁止静默关闭默认避让。
internal sealed class AvoidanceSettings {
 internal readonly bool IdleFadeEnabled,PopupClippingEnabled;
 internal readonly int DwellMilliseconds,FadeOutMilliseconds,FadeInMilliseconds,MotionThreshold;
 internal AvoidanceSettings(bool idle,bool popup,int dwell,int fadeOut,int fadeIn,int threshold){
  if(dwell<200||dwell>10000||fadeOut<0||fadeOut>2000||fadeIn<0||fadeIn>1000||threshold<1||threshold>12)throw new InvalidDataException("Avoidance settings outside supported range.");
  IdleFadeEnabled=idle;PopupClippingEnabled=popup;DwellMilliseconds=dwell;FadeOutMilliseconds=fadeOut;FadeInMilliseconds=fadeIn;MotionThreshold=threshold;
 }
 internal static AvoidanceSettings Load(string root){
  string path=Path.Combine(root,"avoidance-settings.json");
  if(!File.Exists(path))return new AvoidanceSettings(true,true,800,180,80,3);
  var row=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(path));
  return new AvoidanceSettings(Convert.ToBoolean(row["idleFadeEnabled"]),Convert.ToBoolean(row["popupClippingEnabled"]),Convert.ToInt32(row["dwellMilliseconds"]),Convert.ToInt32(row["fadeOutMilliseconds"]),Convert.ToInt32(row["fadeInMilliseconds"]),Convert.ToInt32(row["motionThresholdLogicalPixels"]));
 }
}

// [代码块] full.idle-fade | 范围：第 27-44 行（本次词法括号核验）。
// [功能/连接] AvatarHost 每次位置采样调用 Step；仅返回人物透明度，不修改光标、坐标或热点。
// [修改] v1.4.0 用停留锚点过滤微抖；累积移动跨过阈值恢复，按键拖动保持显示，转向渐变连续。
internal sealed class IdleFade {
 readonly AvoidanceSettings settings;
 bool initialized;int anchorX,anchorY;double lastMotion,transitionStart,transitionFrom=255,target=255,opacity=255;
 internal IdleFade(AvoidanceSettings settings){this.settings=settings;}
 internal void Reset(){initialized=false;opacity=target=transitionFrom=255;}
 internal byte Step(double now,int x,int y,bool dragging,double dpiScale){
  if(!initialized){initialized=true;anchorX=x;anchorY=y;lastMotion=transitionStart=now;}
  int threshold=Math.Max(1,(int)Math.Round(settings.MotionThreshold*Math.Max(1,dpiScale)));
  long dx=(long)x-anchorX,dy=(long)y-anchorY;
  if(dragging||dx*dx+dy*dy>=(long)threshold*threshold){anchorX=x;anchorY=y;lastMotion=now;}
  double duration=target==0?settings.FadeOutMilliseconds:settings.FadeInMilliseconds;
  double t=duration==0?1:Math.Min(1,Math.Max(0,(now-transitionStart)/duration));
  opacity=transitionFrom+(target-transitionFrom)*t;
  double desired=settings.IdleFadeEnabled&&!dragging&&now-lastMotion>=settings.DwellMilliseconds?0:255;
  if(desired!=target){transitionFrom=opacity;transitionStart=now;target=desired;if((target==0?settings.FadeOutMilliseconds:settings.FadeInMilliseconds)==0)opacity=target;}
  return (byte)Math.Max(0,Math.Min(255,Math.Round(opacity)));
 }
}
