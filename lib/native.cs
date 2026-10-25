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
 // Opt-in (tuning.json perMonitorDpi): sharp rendering on monitors with different scale settings.
 [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
 public static bool EnablePerMonitorDpi() {
  try { return SetThreadDpiAwarenessContext(new IntPtr(-4))!=IntPtr.Zero; } catch(Exception) { return false; }
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
// Beat tracking from whatever the PC is playing (WASAPI loopback). Audio is analysed in memory only:
// nothing is recorded or saved. Bass energy at 100 frames/s -> onset strength -> tempo by
// autocorrelation (70-180 BPM) -> beat timing by matching a comb of beats to recent onsets.
public static class CapriBeat {
 [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
 interface IMMDeviceEnumerator {
  [PreserveSig] int EnumAudioEndpoints(int dataFlow,int stateMask,out IntPtr devices);
  [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow,int role,out IMMDevice device);
 }
 [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
 interface IMMDevice {
  [PreserveSig] int Activate(ref Guid iid,int clsCtx,IntPtr activationParams,[MarshalAs(UnmanagedType.IUnknown)] out object iface);
 }
 [ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
 interface IAudioClient {
  [PreserveSig] int Initialize(int shareMode,int streamFlags,long bufferDuration,long periodicity,IntPtr format,IntPtr sessionGuid);
  [PreserveSig] int GetBufferSize(out uint frames);
  [PreserveSig] int GetStreamLatency(out long latency);
  [PreserveSig] int GetCurrentPadding(out uint padding);
  [PreserveSig] int IsFormatSupported(int shareMode,IntPtr format,out IntPtr closest);
  [PreserveSig] int GetMixFormat(out IntPtr format);
  [PreserveSig] int GetDevicePeriod(out long defaultPeriod,out long minimumPeriod);
  [PreserveSig] int Start();
  [PreserveSig] int Stop();
  [PreserveSig] int Reset();
  [PreserveSig] int SetEventHandle(IntPtr handle);
  [PreserveSig] int GetService(ref Guid iid,[MarshalAs(UnmanagedType.IUnknown)] out object service);
 }
 [ComImport, Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
 interface IAudioCaptureClient {
  [PreserveSig] int GetBuffer(out IntPtr data,out uint frames,out uint flags,out ulong devicePosition,out ulong qpcPosition);
  [PreserveSig] int ReleaseBuffer(uint frames);
  [PreserveSig] int GetNextPacketSize(out uint frames);
 }

 const int Fps=100, History=600;            // 6 s of onset history
 static readonly System.Diagnostics.Stopwatch clock=System.Diagnostics.Stopwatch.StartNew();
 static readonly object sync=new object();
 static System.Threading.Thread thread;
 static volatile bool stopRequested;
 static double[] onset=new double[History];
 static double[] energy=new double[History];
 static int count;                           // frames analysed so far
 static double lowpass, hopEnergy, prevLog;
 static int hopFill, hopSize=480, rate=48000;
 static double lastFrameTime;
 static int framesSinceEstimate;
 static double candidatePeriod;
 static int candidateVotes;

 public static volatile bool Running;
 public static string LastError="";
 public static double Bpm;                    // current tempo, 0 when unsure
 public static double Confidence;             // 0..1
 public static double Level;                  // recent loudness, 0..1-ish
 static double period;                        // seconds per beat
 static double lastBeat;                      // clock time of the most recent beat

 public static double Now() { return clock.Elapsed.TotalSeconds; }
 // Position inside the current beat (0..1), or -1 when there is no reliable beat.
 public static double Phase() {
  double p=period, b=lastBeat;
  if(!Running || p<=0 || Confidence<0.4 || Level<0.002) return -1;
  double x=(Now()-b)/p; x-=Math.Floor(x);
  return x;
 }
 // Whole beats since the last detected beat (used to alternate left/right on each beat).
 public static long BeatNumber() {
  double p=period; if(p<=0) return 0;
  return (long)Math.Floor((Now()-lastBeat)/p);
 }

 public static void Start() {
  if(thread!=null && thread.IsAlive) return;
  stopRequested=false; LastError="";
  thread=new System.Threading.Thread(Run); thread.IsBackground=true; thread.Name="CapriBeat"; thread.Start();
 }
 public static void Stop() { stopRequested=true; }

 public static void Reset(int sampleRate) {
  lock(sync) {
   rate=Math.Max(8000,sampleRate); hopSize=rate/Fps; hopFill=0; hopEnergy=0; lowpass=0; prevLog=0;
   onset=new double[History]; energy=new double[History]; count=0; framesSinceEstimate=0;
   Bpm=0; Confidence=0; period=0; candidatePeriod=0; candidateVotes=0;
  }
 }

 // Feeds mono samples (-1..1). Public so the analysis can be tested without a sound card.
 public static void Feed(float[] samples,int n,double endTime) {
  lock(sync) {
   double a=1.0-Math.Exp(-2*Math.PI*150.0/rate);       // ~150 Hz low-pass: kick drum and bass
   int framesInBuffer=(n+hopFill)/hopSize, frameIndex=0;
   for(int i=0;i<n;i++) {
    lowpass+=a*(samples[i]-lowpass);
    hopEnergy+=lowpass*lowpass;
    if(++hopFill>=hopSize) {
     double e=hopEnergy/hopSize; hopFill=0; hopEnergy=0;
     double lg=Math.Log(1e-7+e);
     double o=Math.Max(0,lg-prevLog); prevLog=0.7*prevLog+0.3*lg;
     onset[count%History]=o; energy[count%History]=e; count++;
     frameIndex++;
     lastFrameTime=endTime-(double)(framesInBuffer-frameIndex)/Fps;
     if(++framesSinceEstimate>=25) { framesSinceEstimate=0; Estimate(); }
    }
   }
  }
 }

 static double At(double[] ring,int age) { return ring[((count-1-age)%History+History)%History]; }

 static void Estimate() {
  int n=Math.Min(count,History);
  if(n<300) return;                                  // need 3 s
  double level=0; for(int i=0;i<100;i++) level+=At(energy,i); Level=Math.Sqrt(level/100);
  int minLag=Fps*60/180, maxLag=Fps*60/70;
  double best=0, sum=0, sum2=0; int bestLag=0; int lags=0;
  double[] ac=new double[maxLag+2];
  for(int lag=minLag-1;lag<=maxLag+1;lag++) {
   double s=0; for(int i=0;i+lag<n;i++) s+=At(onset,i)*At(onset,i+lag);
   ac[lag]=s;
  }
  for(int lag=minLag;lag<=maxLag;lag++) {
   double bpm=60.0*Fps/lag;
   double prior=Math.Exp(-0.5*Math.Pow(Math.Log(bpm/120.0,2)/0.9,2));    // gentle preference for ~120
   double v=ac[lag]*prior;
   sum+=ac[lag]; sum2+=ac[lag]*ac[lag]; lags++;
   if(v>best) { best=v; bestLag=lag; }
  }
  if(bestLag==0) { Confidence=0; return; }
  double mean=sum/lags, sd=Math.Sqrt(Math.Max(1e-12,sum2/lags-mean*mean));
  double conf=Math.Max(0,Math.Min(1,(ac[bestLag]-mean)/(sd*4)));
  // Sub-frame lag from the neighbours (parabolic peak).
  double y0=ac[bestLag-1], y1=ac[bestLag], y2=ac[bestLag+1], den=y0-2*y1+y2;
  double lagF=bestLag+(Math.Abs(den)>1e-12 ? 0.5*(y0-y2)/den : 0);
  double newPeriod=lagF/Fps;
  // Only switch tempo when two estimates in a row agree; otherwise smooth.
  if(period>0 && Math.Abs(newPeriod-period)/period<0.04) newPeriod=0.85*period+0.15*newPeriod;
  else if(Math.Abs(newPeriod-candidatePeriod)/Math.Max(1e-6,candidatePeriod)<0.04) { if(++candidateVotes<2 && period>0) { Confidence=conf*0.5; return; } }
  else { candidatePeriod=newPeriod; candidateVotes=1; if(period>0) { Confidence=conf*0.5; return; } }
  // Beat position: the offset whose comb of beats lines up with the strongest onsets.
  int L=(int)Math.Round(newPeriod*Fps); double bestPhase=-1; int phase=0;
  for(int ph=0;ph<L;ph++) {
   double s=0; int k=0;
   for(int age=ph;age<n && k<8;age+=L,k++) s+=At(onset,age)+0.5*(age>0?At(onset,age-1):0)+0.5*At(onset,age+1<n?age+1:age);
   if(s>bestPhase) { bestPhase=s; phase=ph; }
  }
  // Bass energy peaks ~20 ms after the kick starts; shift back so she moves on the hit.
  period=newPeriod; lastBeat=lastFrameTime-(double)phase/Fps-0.02;
  Bpm=60.0/period; Confidence=conf;
 }

 static void Run() {
  Running=true;
  while(!stopRequested) {
   try { Capture(); }
   catch(Exception e) { LastError=e.Message; }
   if(!stopRequested) System.Threading.Thread.Sleep(2000);    // device changed or failed: retry
  }
  Running=false; Bpm=0; Confidence=0;
 }

 static void Capture() {
  IMMDeviceEnumerator enumerator=(IMMDeviceEnumerator)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")));
  IMMDevice device; Check(enumerator.GetDefaultAudioEndpoint(0,1,out device),"no playback device");
  Guid clientId=typeof(IAudioClient).GUID; object o;
  Check(device.Activate(ref clientId,23,IntPtr.Zero,out o),"cannot open playback device");
  IAudioClient client=(IAudioClient)o;
  IntPtr format; Check(client.GetMixFormat(out format),"no mix format");
  int channels=Marshal.ReadInt16(format,2), sampleRate=Marshal.ReadInt32(format,4), bits=Marshal.ReadInt16(format,14);
  int tag=Marshal.ReadInt16(format,0)&0xFFFF;
  bool isFloat=tag==3;
  if(tag==0xFFFE) { byte[] g=new byte[16]; Marshal.Copy(new IntPtr(format.ToInt64()+24),g,0,16); isFloat=new Guid(g)==new Guid("00000003-0000-0010-8000-00aa00389b71"); }
  try { Check(client.Initialize(0,0x00020000,10000000,0,format,IntPtr.Zero),"cannot start loopback"); }
  finally { Marshal.FreeCoTaskMem(format); }
  Guid captureId=typeof(IAudioCaptureClient).GUID; object c;
  Check(client.GetService(ref captureId,out c),"no capture service");
  IAudioCaptureClient capture=(IAudioCaptureClient)c;
  Reset(sampleRate);
  float[] mono=new float[sampleRate];
  Check(client.Start(),"cannot start capture");
  double lastData=Now();
  try {
   while(!stopRequested) {
    System.Threading.Thread.Sleep(10);
    uint packet; Check(capture.GetNextPacketSize(out packet),"capture failed");
    if(packet==0) {
     // Nothing playing: feed silence so the timeline keeps moving and confidence drops.
     double gap=Now()-lastData;
     if(gap>0.05) { int z=Math.Min(mono.Length,(int)(gap*sampleRate)); Array.Clear(mono,0,z); Feed(mono,z,Now()); lastData=Now(); }
     continue;
    }
    while(packet>0) {
     IntPtr data; uint frames, flags; ulong p1, p2;
     Check(capture.GetBuffer(out data,out frames,out flags,out p1,out p2),"capture failed");
     int n=(int)Math.Min(frames,(uint)mono.Length);
     if((flags&2)!=0 || data==IntPtr.Zero) Array.Clear(mono,0,n);
     else if(isFloat && bits==32) {
      float[] raw=new float[n*channels]; Marshal.Copy(data,raw,0,raw.Length);
      for(int i=0;i<n;i++) { float m=0; for(int ch=0;ch<channels;ch++) m+=raw[i*channels+ch]; mono[i]=m/channels; }
     } else if(bits==16) {
      short[] raw=new short[n*channels]; Marshal.Copy(data,raw,0,raw.Length);
      for(int i=0;i<n;i++) { float m=0; for(int ch=0;ch<channels;ch++) m+=raw[i*channels+ch]/32768f; mono[i]=m/channels; }
     } else Array.Clear(mono,0,n);
     Check(capture.ReleaseBuffer(frames),"capture failed");
     Feed(mono,n,Now()); lastData=Now();
     Check(capture.GetNextPacketSize(out packet),"capture failed");
    }
   }
  } finally {
   client.Stop();
   Marshal.ReleaseComObject(capture); Marshal.ReleaseComObject(client); Marshal.ReleaseComObject(device); Marshal.ReleaseComObject(enumerator);
  }
 }
 static void Check(int hr,string what) { if(hr<0) throw new Exception(what+" (0x"+hr.ToString("X8")+")"); }
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
 // Loudness of a 16-bit PCM WAV, fps values per second, scaled so ordinary speech reaches about 1.
 // Drives the talking mouth. Falls quickly, so syllables read as separate mouth movements.
 public static double[] Envelope(byte[] wav, double fps) {
  if(wav==null || wav.Length<44 || fps<=0) return new double[0];
  int channels=1, rate=22050, bits=16;
  for(int pos=12;pos+8<=wav.Length;) {
   int length=BitConverter.ToInt32(wav,pos+4);
   if(length<0 || length>wav.Length-pos-8) length=wav.Length-pos-8;
   string kind=System.Text.Encoding.ASCII.GetString(wav,pos,4);
   if(kind=="fmt " && length>=16) { channels=Math.Max(1,(int)BitConverter.ToInt16(wav,pos+10)); rate=BitConverter.ToInt32(wav,pos+12); bits=BitConverter.ToInt16(wav,pos+22); }
   if(kind=="data") {
    if(bits!=16 || rate<=0) return new double[0];
    int frameBytes=2*channels, perWindow=Math.Max(1,(int)(rate/fps)), frames=length/frameBytes;
    int windows=frames/perWindow+1;
    double[] env=new double[windows];
    for(int w=0;w<windows;w++) {
     double sum=0; int count=0;
     for(int f=w*perWindow;f<Math.Min(frames,(w+1)*perWindow);f++) { double v=BitConverter.ToInt16(wav,pos+8+f*frameBytes)/32768.0; sum+=v*v; count++; }
     env[w]=count>0 ? Math.Sqrt(sum/count) : 0;
    }
    double[] sorted=(double[])env.Clone(); Array.Sort(sorted);
    double peak=sorted[Math.Max(0,(int)(sorted.Length*0.9)-1)];
    if(peak<=1e-6) return new double[windows];
    for(int k=0;k<windows;k++) { env[k]=Math.Min(1.0,env[k]/peak); if(k>0) env[k]=Math.Max(env[k],env[k-1]*0.55); }
    return env;
   }
   pos+=8+length+(length%2);
  }
  return new double[0];
 }
 // Mouth openness per frame (0 closed, 1 half, 2 wide) from an Envelope. Loudness is judged against
 // the surrounding ~0.3 s so each syllable opens and closes, and a wide mouth eases to half after
 // a few frames, so long loud stretches still look like talking.
 public static int[] MouthLevels(double[] env, double half, double wide) {
  if(env==null) return new int[0];
  int n=env.Length, win=4, hold=4, rest=2;
  int[] levels=new int[n];
  for(int i=0;i<n;i++) {
   double lo=double.MaxValue, hi=double.MinValue;
   for(int j=Math.Max(0,i-win);j<=Math.Min(n-1,i+win);j++) { lo=Math.Min(lo,env[j]); hi=Math.Max(hi,env[j]); }
   double rel=hi-lo>0.08 ? (env[i]-lo)/(hi-lo) : 0.5;
   double x=rel*Math.Min(1.0,env[i]*1.5);
   levels[i]=x>=wide ? 2 : (x>=half ? 1 : 0);
  }
  int run=0, cool=0;
  for(int i=0;i<n;i++) {
   if(levels[i]==2) {
    if(cool>0) { levels[i]=1; cool--; }
    else { run++; if(run>hold) { levels[i]=1; run=0; cool=rest-1; } }
   } else { run=0; cool=0; }
  }
  return levels;
 }
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
