using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

internal sealed class CharacterFrames {
 internal sealed class Role {internal byte[][] Frames;internal double Milliseconds;internal int X,Y,SymbolX,SymbolY;internal int BoundsLeft,BoundsTop,BoundsRight,BoundsBottom,PhysicalWidth,PhysicalHeight,PhysicalHotX,PhysicalHotY,LayoutOffsetX,LayoutOffsetY;}
 internal readonly Role[] Roles=new Role[17];
 internal readonly string Id,CharacterId,Edition;
 internal readonly int Width,Height,LogicalSize,ExpectedNativeSize;
 internal readonly long DecodedFrameBytes;
 internal readonly int UniqueFrames;
 internal readonly IntPtr[] Handles;
 // [代码块] prototype.frames.load | 范围：第 17-67 行（括号核验）。
 // [连接] Controller.Keep → manifest；原生热点给NativeCursor.ApplyProfile，整幅热点给AvatarHost。
 // [修改] 2026-10-31 plan v2：要求所有role的原生逻辑画布相同，并验证完整符号热点范围。
 internal CharacterFrames(string root,string id){
  Id=id;Handles=NativeCursor.Handles();
  string assets=Path.GetFullPath(Path.Combine(root,"assets"))+Path.DirectorySeparatorChar;
  var rows=(object[])new JavaScriptSerializer().DeserializeObject(File.ReadAllText(Path.Combine(assets,id,"manifest.json")));
  if(rows.Length!=17)throw new InvalidDataException("Exactly 17 roles required.");
  var first=(Dictionary<string,object>)rows[0];
  var canvas=(object[])first["body_canvas"];
  Width=Convert.ToInt32(canvas[0]);Height=Convert.ToInt32(canvas[1]);
  ExpectedNativeSize=Convert.ToInt32(first["expectedNativeSize"]);
  if(Array.IndexOf(new[]{32,64,96,128,256},ExpectedNativeSize)<0)throw new InvalidDataException("Unsupported native logical canvas.");
  if(Width<1||Height<1||Width>512||Height>512)throw new InvalidDataException("Invalid character surface.");
  CharacterId=(string)first["character"];Edition=(string)first["edition"];LogicalSize=Convert.ToInt32(first["logical_size"]);
  long loadedBytes=0;var decoded=new Dictionary<string,byte[]>();
  foreach(Dictionary<string,object> row in rows){
   int index=Array.IndexOf(NativeCursor.Names,(string)row["role"]);
   if(index<0||Roles[index]!=null)throw new InvalidDataException("Unknown or duplicate role.");
   var dimensions=(object[])row["body_canvas"];
   if(Convert.ToInt32(dimensions[0])!=Width||Convert.ToInt32(dimensions[1])!=Height||
      (string)row["character"]!=CharacterId||(string)row["edition"]!=Edition||Convert.ToInt32(row["logical_size"])!=LogicalSize)
      throw new InvalidDataException("Inconsistent profile metadata.");
   var nativeCanvas=(object[])row["native_canvas"];var nativeHot=(object[])row["symbol_hotspot"];
   if(Convert.ToInt32(row["expectedNativeSize"])!=ExpectedNativeSize||Convert.ToInt32(nativeCanvas[0])!=ExpectedNativeSize||Convert.ToInt32(nativeCanvas[1])!=ExpectedNativeSize)throw new InvalidDataException("Mixed native profile canvases.");
   int count=Convert.ToInt32(row["frames"]);var hot=(object[])row["hotspot"];
   var role=new Role{Frames=new byte[count][],Milliseconds=Convert.ToDouble(row["frame_ms"]),X=Convert.ToInt32(hot[0]),Y=Convert.ToInt32(hot[1]),SymbolX=Convert.ToInt32(nativeHot[0]),SymbolY=Convert.ToInt32(nativeHot[1])};
   // [修改] plan v3：全部phase可见边界并集用于固定DPI间距，不改变人物/点击热点。
   var bounds=(object[])row["native_visible_bounds"];
   if(bounds.Length!=4)throw new InvalidDataException("Missing native visible bounds.");
   role.BoundsLeft=Convert.ToInt32(bounds[0]);role.BoundsTop=Convert.ToInt32(bounds[1]);role.BoundsRight=Convert.ToInt32(bounds[2]);role.BoundsBottom=Convert.ToInt32(bounds[3]);
   if(role.BoundsLeft<1||role.BoundsTop<1||role.BoundsRight<=role.BoundsLeft||role.BoundsBottom<=role.BoundsTop||role.BoundsRight>=ExpectedNativeSize||role.BoundsBottom>=ExpectedNativeSize)throw new InvalidDataException("Invalid native visible bounds.");
   if(role.SymbolX<0||role.SymbolY<0||role.SymbolX>=ExpectedNativeSize||role.SymbolY>=ExpectedNativeSize)throw new InvalidDataException("Native hotspot outside canvas.");
   if(count<1||count>64||role.Milliseconds<10||Math.Abs(role.X)>1024||Math.Abs(role.Y)>1024)throw new InvalidDataException("Invalid role geometry or animation.");
   // Body coordinates are independent of the native symbol hotspot. A body
   // to the right of the endpoint legitimately has a negative virtual hotspot.
   string folder=Path.GetFullPath(Path.Combine(assets,(string)row["body_directory"]));
   if(!folder.StartsWith(assets,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Body directory escaped assets.");
   var hashes=(object[])row["body_hashes"];if(hashes.Length!=count)throw new InvalidDataException("Frame hash count mismatch.");
   for(int n=0;n<count;n++){
    string hash=(string)hashes[n];byte[] bytes;
    if(hash==null||hash.Length!=64)throw new InvalidDataException("Invalid frame identity.");
    if(!decoded.TryGetValue(hash,out bytes)){
     loadedBytes+=(long)Width*Height*4;
     if(loadedBytes>64L*1024*1024)throw new InvalidDataException("Profile exceeds frame memory budget.");
     bytes=Avatar.LoadFrame(Path.Combine(folder,"body-"+n.ToString("00")+".png"),Width,Height);decoded.Add(hash,bytes);
    }
    role.Frames[n]=bytes;
   }
   Roles[index]=role;
  }
  foreach(var r in Roles)if(r==null)throw new InvalidDataException("Missing cursor role.");
  DecodedFrameBytes=loadedBytes;UniqueFrames=decoded.Count;
 }
}
