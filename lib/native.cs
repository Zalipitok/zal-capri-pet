using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public static class CapriCursor {
 [StructLayout(LayoutKind.Sequential)] public struct Point {public int X;public int Y;}
 [DllImport("user32.dll",SetLastError=true)] public static extern bool GetCursorPos(out Point p);
 [DllImport("user32.dll",SetLastError=true)] public static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
 [StructLayout(LayoutKind.Sequential)] public struct Rect {public int Left,Top,Right,Bottom;}
 [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h,int n);
 [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr h,int n,int v);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h,out Rect r);
 [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int cx,int cy,uint flags);
 public static void MakeOverlay(IntPtr h) {SetWindowLong(h,-20,GetWindowLong(h,-20)|0x20|0x08000000|0x80);}
 public static void FollowCursor(IntPtr h) {
  Point p;Rect r;if(GetCursorPos(out p)&&GetWindowRect(h,out r))
   SetWindowPos(h,new IntPtr(-1),p.X-(r.Right-r.Left)/2,p.Y-(r.Bottom-r.Top)/2,0,0,0x11);
 }
 public static string LastResult="Not tested";
 public static Point ReadPosition() {Point p;if(!GetCursorPos(out p))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());return p;}
 public static bool Nudge(int dx,int dy) {
  Point p;if(!GetCursorPos(out p)){LastResult="GetCursorPos failed: "+Marshal.GetLastWin32Error();return false;}
  int l=GetSystemMetrics(76),t=GetSystemMetrics(77);
  int r=l+GetSystemMetrics(78)-1,b=t+GetSystemMetrics(79)-1;
  int x=Math.Max(l,Math.Min(r,p.X+dx)),y=Math.Max(t,Math.Min(b,p.Y+dy));
  if(!SetCursorPos(x,y)){LastResult="SetCursorPos failed: "+Marshal.GetLastWin32Error();return false;}
  Point after;if(!GetCursorPos(out after)){LastResult="Moved; position verification failed";return true;}
  bool moved=after.X!=p.X || after.Y!=p.Y;
  LastResult=moved ? "Moved "+(after.X-p.X)+", "+(after.Y-p.Y)+" pixels" : "Windows accepted the request but the pointer stayed in place (possibly clipped by another app).";
  return moved;
 }
}
// Monitors, windows, fullscreen detection, system idle time and app volume.
public static class CapriDesktop {
 [StructLayout(LayoutKind.Sequential)] public struct RECT {public int Left,Top,Right,Bottom;}
 [StructLayout(LayoutKind.Sequential)] public struct POINT {public int X,Y;}
 [StructLayout(LayoutKind.Sequential)] public struct MONITORINFO {public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags;}
 [StructLayout(LayoutKind.Sequential)] struct LASTINPUTINFO {public uint cbSize; public uint dwTime;}
 delegate bool MonitorEnumProc(IntPtr monitor,IntPtr hdc,ref RECT rect,IntPtr data);
 [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(POINT pt,uint flags);
 [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr h,uint flags);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern bool GetMonitorInfo(IntPtr m,ref MONITORINFO mi);
 [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr hdc,IntPtr clip,MonitorEnumProc proc,IntPtr data);
 [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h,out RECT r);
 [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
 [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
 [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr h);
 [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h,int n);
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr h,StringBuilder s,int n);
 [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO info);
 [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr h);
 [DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out int state);
 [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h,int attr,out RECT r,int size);
 [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h,int attr,out int v,int size);
 [DllImport("winmm.dll")] static extern int waveOutSetVolume(IntPtr h,uint v);
 static readonly uint ownPid=(uint)System.Diagnostics.Process.GetCurrentProcess().Id;
 static int[] Work(IntPtr m,bool full) {
  MONITORINFO mi=new MONITORINFO(); mi.cbSize=Marshal.SizeOf(typeof(MONITORINFO));
  if(m==IntPtr.Zero || !GetMonitorInfo(m,ref mi)) return null;
  RECT r=full ? mi.rcMonitor : mi.rcWork;
  return new int[]{r.Left,r.Top,r.Right,r.Bottom};
 }
 public static int[] WorkAreaAt(int x,int y) {POINT p; p.X=x; p.Y=y; return Work(MonitorFromPoint(p,2),false);}
 public static int[] AllWorkAreas() {
  List<int> list=new List<int>();
  MonitorEnumProc callback=delegate(IntPtr m,IntPtr hdc,ref RECT rect,IntPtr data) {int[] a=Work(m,false); if(a!=null) list.AddRange(a); return true;};
  EnumDisplayMonitors(IntPtr.Zero,IntPtr.Zero,callback,IntPtr.Zero);
  GC.KeepAlive(callback);
  return list.ToArray();
 }
 public static double IdleSeconds() {
  LASTINPUTINFO info=new LASTINPUTINFO(); info.cbSize=(uint)Marshal.SizeOf(typeof(LASTINPUTINFO));
  if(!GetLastInputInfo(ref info)) return 0;
  unchecked { uint now=(uint)Environment.TickCount; return (now-info.dwTime)/1000.0; }
 }
 public static bool SetAppVolume(double volume) {
  uint level=(uint)(Math.Max(0,Math.Min(1,volume))*0xFFFF);
  try { return waveOutSetVolume(IntPtr.Zero,(level<<16)|level)==0; } catch(Exception) { return false; }
 }
 static string ClassOf(IntPtr h) {StringBuilder s=new StringBuilder(256); GetClassName(h,s,s.Capacity); return s.ToString();}
 static bool IsShell(string c) {return c=="Progman"||c=="WorkerW"||c=="Shell_TrayWnd"||c=="Shell_SecondaryTrayWnd"||c=="NotifyIconOverflowWindow"||c=="Windows.UI.Core.CoreWindow"||c=="TopLevelWindowForOverflowXamlIsland";}
 static bool IsOwn(IntPtr h) {uint pid; GetWindowThreadProcessId(h,out pid); return pid==ownPid;}
 static bool Cloaked(IntPtr h) {int c=0; try { return DwmGetWindowAttribute(h,14,out c,4)==0 && c!=0; } catch(Exception) { return false; }}
 static bool Bounds(IntPtr h,out RECT r) {
  try { if(DwmGetWindowAttribute(h,9,out r,Marshal.SizeOf(typeof(RECT)))==0 && r.Right>r.Left) return true; } catch(Exception) { }
  return GetWindowRect(h,out r);
 }
 static bool Usable(IntPtr h) {
  if(h==IntPtr.Zero || !IsWindow(h) || !IsWindowVisible(h) || IsIconic(h) || Cloaked(h) || IsOwn(h)) return false;
  if(IsShell(ClassOf(h))) return false;
  return (GetWindowLong(h,-20)&0x80)==0; // skip tool windows
 }
 // Foreground app window as {hwnd,left,top,right,bottom} in pixels, or null.
 public static long[] ForegroundWindow() {
  IntPtr h=GetForegroundWindow(); RECT r;
  if(!Usable(h) || !Bounds(h,out r)) return null;
  return new long[]{h.ToInt64(),r.Left,r.Top,r.Right,r.Bottom};
 }
 // Foreground app as {process name, window title}, or null. Used only by the opt-in app awareness.
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr h,StringBuilder s,int n);
 public static string[] ForegroundApp() {
  IntPtr h=GetForegroundWindow(); if(h==IntPtr.Zero || IsOwn(h)) return null;
  uint pid; GetWindowThreadProcessId(h,out pid);
  string name="";
  try { using(System.Diagnostics.Process p=System.Diagnostics.Process.GetProcessById((int)pid)) name=p.ProcessName; } catch(Exception) { return null; }
  StringBuilder title=new StringBuilder(512); GetWindowText(h,title,title.Capacity);
  return new string[]{name,title.ToString()};
 }
 public static int[] WindowBounds(long handle) {
  IntPtr h=new IntPtr(handle); RECT r;
  if(!Usable(h) || !Bounds(h,out r)) return null;
  return new int[]{r.Left,r.Top,r.Right,r.Bottom};
 }
 // True when a fullscreen app covers the monitor Capri is on (or D3D exclusive/presentation mode is active).
 public static bool FullscreenBlocks(IntPtr self) {
  try { int q; if(SHQueryUserNotificationState(out q)==0 && (q==3 || q==4)) return true; } catch(Exception) { }
  IntPtr fg=GetForegroundWindow();
  if(fg==IntPtr.Zero || IsOwn(fg) || !IsWindowVisible(fg) || IsIconic(fg) || IsShell(ClassOf(fg))) return false;
  RECT r; if(!GetWindowRect(fg,out r)) return false;
  IntPtr mon=MonitorFromWindow(fg,2); int[] m=Work(mon,true); if(m==null) return false;
  if(r.Left>m[0] || r.Top>m[1] || r.Right<m[2] || r.Bottom<m[3]) return false;
  // A maximized, resizable window overhangs the monitor too; that is not fullscreen.
  if(IsZoomed(fg) && (GetWindowLong(fg,-16)&0x00040000)!=0) return false;
  return self==IntPtr.Zero || mon==MonitorFromWindow(self,2);
 }
}
public sealed class CapriVoicePlayback {
 private readonly System.Media.SoundPlayer player;
 private readonly System.IO.MemoryStream stream;
 private volatile bool completed;
 public bool IsCompleted { get { return completed; } }
 public string Error { get; private set; }
 public CapriVoicePlayback(byte[] wav) {
  stream=new System.IO.MemoryStream(wav,false);
  player=new System.Media.SoundPlayer(stream); player.Load();
 }
 public void Start() {
  System.Threading.ThreadPool.QueueUserWorkItem(delegate(object unused) {
   try { player.PlaySync(); } catch(Exception e) { Error=e.Message; }
   finally { player.Dispose(); stream.Dispose(); completed=true; }
  });
 }
 public void Stop() { if(!completed) { try { player.Stop(); } catch(ObjectDisposedException) {} } }
}
// Fallback only: used when Windows refuses per-app volume, so gain is baked into the clip.
public static class CapriPcmAudio {
 public static byte[] WithVolume(byte[] source, double volume) {
  byte[] data=(byte[])source.Clone();
  if(data.Length<44) throw new ArgumentException("Invalid WAV file");
  bool pcm16=false;
  for(int pos=12;pos+8<=data.Length;) {
   int length=BitConverter.ToInt32(data,pos+4);
   if(length<0 || length>data.Length-pos-8) throw new ArgumentException("Truncated WAV chunk");
   string kind=System.Text.Encoding.ASCII.GetString(data,pos,4);
   if(kind=="fmt " && length>=16) pcm16=BitConverter.ToInt16(data,pos+8)==1 && BitConverter.ToInt16(data,pos+22)==16;
   if(kind=="data") {
    if(!pcm16) throw new ArgumentException("Expected 16-bit PCM WAV");
    double gain=Math.Max(0,Math.Min(1,volume));
    for(int i=pos+8;i+1<pos+8+length;i+=2) {
     short sample=(short)(BitConverter.ToInt16(data,i)*gain);
     data[i]=(byte)(sample&255); data[i+1]=(byte)((sample>>8)&255);
    }
    return data;
   }
   pos+=8+length+(length%2);
  }
  throw new ArgumentException("WAV data chunk missing");
 }
}
// Removes detached seam fragments. Results are cached in sprite-cache, so this runs once per sheet.
public static class CapriSpritePixels {
 public static void Clean(byte[] pixels, int width, int height) {
  int count=width*height;
  bool[] seen=new bool[count];
  int[] queue=new int[count];
  for(int i=0;i<count;i++) if(pixels[i*4+3]<8) pixels[i*4+3]=0;
  for(int start=0;start<count;start++) {
   if(seen[start] || pixels[start*4+3]==0) continue;
   int head=0,tail=1;queue[0]=start;seen[start]=true;
   bool border=false;
   while(head<tail) {
    int i=queue[head++], x=i%width, y=i/width;
    if(x==0 || y==0 || x==width-1 || y==height-1) border=true;
    for(int dy=-1;dy<=1;dy++) for(int dx=-1;dx<=1;dx++) {
     int nx=x+dx,ny=y+dy;
     if(nx<0 || nx>=width || ny<0 || ny>=height) continue;
     int n=ny*width+nx;
     if(!seen[n] && pixels[n*4+3]>0) {seen[n]=true;queue[tail++]=n;}
    }
   }
   // Preserve larger silhouettes and intentional interior floating details.
   if(tail<12 || (border && tail<2000))
    for(int j=0;j<tail;j++) pixels[queue[j]*4+3]=0;
  }
  for(int i=0;i<count;i++) if(pixels[i*4+3]==0) {
   pixels[i*4]=0;pixels[i*4+1]=0;pixels[i*4+2]=0;
  }
 }
}
