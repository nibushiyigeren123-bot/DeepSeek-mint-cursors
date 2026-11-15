using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
internal static class DisplayRefresh {
 // DEVMODEW's display union occupies 16 bytes; fields below preserve the native layout.
 [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
 struct Mode {
  [MarshalAs(UnmanagedType.ByValTStr, SizeConst=32)] public string device;
  public ushort spec, driver, size, extra;
  public uint fields;
  public int x, y;
  public uint orientation, fixedOutput;
  public short color, duplex, yResolution, ttOption, collate;
  [MarshalAs(UnmanagedType.ByValTStr, SizeConst=32)] public string form;
  public ushort logPixels;
  public uint bits, width, height, flags, frequency, icmMethod, icmIntent, media, dither, reserved1, reserved2, panWidth, panHeight;
 }
 [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern bool EnumDisplaySettings(string device, int mode, ref Mode value);
 [DllImport("user32.dll")] static extern bool GetCursorPos(out Point point);
 internal static int ForCursor() {
  Point point;
  Screen screen = GetCursorPos(out point) ? Screen.FromPoint(point) : Screen.PrimaryScreen;
  var mode = new Mode { size = (ushort)Marshal.SizeOf(typeof(Mode)) };
  if (screen != null && EnumDisplaySettings(screen.DeviceName, -1, ref mode) && mode.frequency >= 24 && mode.frequency <= 1000)
   return (int)mode.frequency;
  return 60;
 }
}
