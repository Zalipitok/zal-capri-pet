using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Runtime.InteropServices;
public static class CapriFx {
 public static void UpdateArcs(Polyline[] arcs,double cw,double ch,double time,double strength) {
  for(int i=0;i<arcs.Length;i++) {
   PointCollection points=new PointCollection(5);
   for(int j=0;j<5;j++) {
    double angle=i*Math.PI/3+j*0.13+time*0.3;
    double radius=0.19+j*0.055+0.02*Math.Sin(time*9+i*3+j*2);
    points.Add(new Point(cw*(0.5+Math.Cos(angle)*radius),ch*(0.5+Math.Sin(angle)*radius)));
   }
   points.Freeze();
   arcs[i].Points=points; arcs[i].Visibility=Visibility.Visible; arcs[i].Opacity=strength;
  }
 }
 public static void UpdateBars(Rectangle[] bars,double ww,double hh,double elapsed,bool travelling,double direction) {
  for(int i=0;i<bars.Length;i++) {
   Rectangle bar=bars[i];
   bar.Width=ww*(0.12+0.025*(i%5));
   Canvas.SetLeft(bar,(ww-bar.Width)*(0.5+0.45*Math.Sin(i*2.3+elapsed*2)));
   Canvas.SetTop(bar,hh*(0.12+0.075*i)+Math.Sin(elapsed*3+i)*5);
   if(travelling) {
    bar.Width=ww*(0.08+0.035*(i%4));
    double tail=direction>0 ? 0.08 : 0.7;
    Canvas.SetLeft(bar,ww*(tail+0.08*Math.Sin(elapsed*9+i)));
    bar.Opacity=0.4+0.6*Math.Abs(Math.Sin(elapsed*8+i*1.7));
   } else bar.Opacity=1;
  }
 }
}
// Global summon hotkey. The window hook only counts presses; the main loop picks them up.
public static class CapriHotkey {
 [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h,int id,uint modifiers,uint key);
 [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h,int id);
 const int Id=0xCA1;
 static int pressed;
 static HwndSource source;
 public static bool Register(IntPtr hwnd,uint modifiers,uint key) {
  if(source==null) { source=HwndSource.FromHwnd(hwnd); if(source==null) return false; source.AddHook(Hook); }
  UnregisterHotKey(hwnd,Id);
  return RegisterHotKey(hwnd,Id,modifiers|0x4000,key); // 0x4000 = no auto-repeat
 }
 public static void Unregister(IntPtr hwnd) { UnregisterHotKey(hwnd,Id); }
 static IntPtr Hook(IntPtr hwnd,int msg,IntPtr wParam,IntPtr lParam,ref bool handled) {
  if(msg==0x0312 && wParam.ToInt32()==Id) { pressed++; handled=true; }
  return IntPtr.Zero;
 }
 public static bool TakePressed() { if(pressed>0) { pressed=0; return true; } return false; }
}
