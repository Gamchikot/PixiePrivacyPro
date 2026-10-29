using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Threading;
using AForge.Video;
using AForge.Video.DirectShow;

public sealed class CropSettings {
    public const string Circle="Круг";
    public string Shape="16:9";
    public float X=0,Y=0,W=1,H=1;
    // Mirror flips what apps receive left to right. The window no longer offers it (the camera's own mirror does that).
    public bool Mirror;
    public bool IsCircle {get {return Shape==Circle;}}
    public float Ratio {get {return Shape=="1:1" || IsCircle ? 1f : Shape=="4:3" ? 4f/3f : 16f/9f;}}
    public void Fit(int width,int height,float fraction) {
        float cx=X+W/2,cy=Y+H/2;
        float ratio=Ratio;
        float pixelW=Math.Min(width,height*ratio)*fraction;
        W=pixelW/width; H=pixelW/ratio/height;
        Move(cx-W/2,cy-H/2);
    }
    public void Move(float x,float y) { X=Math.Max(0,Math.Min(1-W,x)); Y=Math.Max(0,Math.Min(1-H,y)); }
    public CropSettings Copy() { return (CropSettings)MemberwiseClone(); }
    public RectangleF Rectangle(int width,int height) { return new RectangleF(X*width,Y*height,W*width,H*height); }
    public static Bitmap Render(Bitmap input,CropSettings crop) {
        Bitmap output=new Bitmap(1280,720,PixelFormat.Format32bppArgb);
        RenderInto(output,input,crop);
        return output;
    }
    public static void RenderInto(Bitmap output,Bitmap input,CropSettings crop) {
        RectangleF src=crop.Rectangle(input.Width,input.Height);
        float scale=Math.Min(1280/src.Width,720/src.Height);
        RectangleF dest=new RectangleF((1280-src.Width*scale)/2,(720-src.Height*scale)/2,src.Width*scale,src.Height*scale);
        using(var g=Graphics.FromImage(output)) {
            g.Clear(Color.Black);
            g.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
            // dest is centred, so mirroring around the frame centre keeps it in place.
            if(crop.Mirror) {g.TranslateTransform(1280,0);g.ScaleTransform(-1,1);}
            if(crop.IsCircle) {
                using(var circle=new System.Drawing.Drawing2D.GraphicsPath()) {
                    circle.AddEllipse(dest);
                    g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    using(var brush=new TextureBrush(input,src)) {
                        brush.TranslateTransform(dest.X,dest.Y);
                        brush.ScaleTransform(dest.Width/src.Width,dest.Height/src.Height);
                        g.FillPath(brush,circle);
                    }
                }
            } else g.DrawImage(input,dest,src,GraphicsUnit.Pixel);
        }
    }
    public static Bitmap Black() {
        var bitmap=new Bitmap(1280,720,PixelFormat.Format32bppArgb);
        using(var g=Graphics.FromImage(bitmap)) g.Clear(Color.Black);
        return bitmap;
    }
}

// Sender for the published Unity Capture shared-memory protocol (schellingb/UnityCapture).
// Filter DLL is unmodified, MIT licensed; this managed sender is independent.
public sealed class VirtualOutput : IDisposable {
    MemoryMappedFile map;
    MemoryMappedViewAccessor view;
    Mutex mutex;
    EventWaitHandle sent,want=new EventWaitHandle(false,EventResetMode.AutoReset,"UnityCapture_Want");
    byte[] pixels=new byte[1280*720*4];
    // The filter signals Want each time a receiving app asks for a frame; the MF source stamps each delivered frame.
    long lastWant;
    static readonly long ConsumerWindow=TimeSpan.FromSeconds(3).Ticks;
    public bool ConsumerActive {get {
        if(DateTime.UtcNow.Ticks-Interlocked.Read(ref lastWant)<ConsumerWindow) return true;
        try { return mf.ConsumerActive; } catch(Exception) { return false; }
    }}
    public bool MfConsumerActive {get {try { return mf.ConsumerActive; } catch(Exception) { return false; }}}
    void MarkConsumer() {Interlocked.Exchange(ref lastWant,DateTime.UtcNow.Ticks);}
    readonly MfFrameWriter mf=new MfFrameWriter();
    public bool Send(Bitmap image) {
        // The Media Foundation camera gets the same frame.
        try { mf.Send(image); } catch(Exception) {}
        if(view==null) {
            try {
                mutex=Mutex.OpenExisting("UnityCapture_Mutx");
                sent=EventWaitHandle.OpenExisting("UnityCapture_Sent");
                map=MemoryMappedFile.OpenExisting("UnityCapture_Data",MemoryMappedFileRights.ReadWrite);
                view=map.CreateViewAccessor(0,0,MemoryMappedFileAccess.ReadWrite);
            } catch(WaitHandleCannotBeOpenedException) { CloseHandles(); return false; }
              catch(FileNotFoundException) { CloseHandles(); return false; }
        }
        var data=image.LockBits(new Rectangle(0,0,1280,720),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
        // One pass: flip rows bottom-up and swap GDI BGRA to filter RGBA.
        try { unsafe { fixed(byte* target=pixels) for(int y=0;y<720;y++) {
            uint* from=(uint*)((byte*)data.Scan0+y*data.Stride);
            uint* to=(uint*)(target+(719-y)*1280*4);
            for(int x=0;x<1280;x++) { uint p=from[x]; to[x]=(p&0xFF00FF00u)|((p&0xFFu)<<16)|((p>>16)&0xFFu); }
        } } }
        finally { image.UnlockBits(data); }
        bool locked=false;
        try {
            try { locked=mutex.WaitOne(50); } catch(AbandonedMutexException) { locked=true; }
            if(!locked) return false;
            if(view.ReadInt32(0)<pixels.Length) throw new Exception(L.T("Недостаточный размер буфера виртуальной камеры."));
            view.Write(4,1280); view.Write(8,720); view.Write(12,1280);
            view.Write(16,0); view.Write(20,1); view.Write(24,0); view.Write(28,500);
            view.WriteArray(32,pixels,0,pixels.Length);
        } finally { if(locked) mutex.ReleaseMutex(); }
        sent.Set(); if(want.WaitOne(0)) MarkConsumer(); return true;
    }
    void CloseHandles() {
        if(view!=null) view.Dispose(); if(map!=null) map.Dispose(); if(mutex!=null) mutex.Dispose();
        if(sent!=null) sent.Dispose();
        view=null;map=null;mutex=null;sent=null;
    }
    public void Dispose() { CloseHandles(); want.Dispose(); mf.Dispose(); }
}

public sealed class VideoPipeline : IDisposable {
    readonly object gate=new object();
    VideoCaptureDevice capture;
    Bitmap preview,output;
    // The window preview gets a half-size copy of each camera frame, stamped so the window copies only new ones.
    public const int PreviewWidth=640,PreviewHeight=360;
    long previewStamp;bool previewValid;
    public Size SourceSize {get;private set;}
    CropSettings crop=new CropSettings();
    VirtualOutput sender=new VirtualOutput();
    volatile bool allowed,stopping;
    // The preview copy is only needed while the preview window is on screen.
    public volatile bool PreviewVisible=true;
    public string Error {get;private set;}
    public bool Running {get {return capture!=null && capture.IsRunning;}}
    // True while some app reads the Pixie camera (DirectShow or Media Foundation), even when we send a placeholder.
    public bool ConsumerActive {get {return sender.ConsumerActive;}}
    public bool MfConsumerActive {get {return sender.MfConsumerActive;}}
    // Shown instead of camera frames in privacy, before the first frame and when unknown; null means black.
    Bitmap idle;
    void ShowIdle() {
        previewValid=false;
        if(output==null) output=CropSettings.Black();
        using(var g=Graphics.FromImage(output)) {if(idle==null) g.Clear(Color.Black); else g.DrawImage(idle,0,0,1280,720);}
        sender.Send(output);
    }
    public void SetIdleFrame(Bitmap frame) { lock(gate) {
        if(idle==frame) return;
        idle=frame;
        if(!allowed || !Running) ShowIdle();
    }}
    public void SetAllowed(bool value) { lock(gate) {
        allowed=value;
        if(!value) ShowIdle();
    }}
    public void SetCrop(CropSettings value) {lock(gate) crop=value.Copy();}
    public void Start() {
        if(Running) return;
        stopping=false; Error=null;
        string moniker=null;
        foreach(FilterInfo f in new FilterInfoCollection(FilterCategory.VideoInputDevice))
            if(f.Name=="EMEET PIXY" && f.MonikerString.IndexOf("vid_328f",StringComparison.OrdinalIgnoreCase)>=0) {moniker=f.MonikerString;break;}
        if(moniker==null) throw new Exception(L.T("Не найдена физическая камера EMEET PIXY."));
        capture=new VideoCaptureDevice(moniker);
        // The fastest 1280x720 mode up to 30 fps: the list may also offer slow ones first.
        foreach(var cap in capture.VideoCapabilities)
            if(cap.FrameSize.Width==1280 && cap.FrameSize.Height==720 && cap.AverageFrameRate<=30
               && (capture.VideoResolution==null || cap.AverageFrameRate>capture.VideoResolution.AverageFrameRate)) capture.VideoResolution=cap;
        capture.NewFrame+=OnFrame;
        capture.VideoSourceError+=delegate(object s,VideoSourceErrorEventArgs e) { Error=e.Description; };
        capture.Start();
    }
    void OnFrame(object senderObj,NewFrameEventArgs e) {
        if(stopping) return;
        lock(gate) {
            if(stopping) return;
            try {
                if(!allowed) {ShowIdle();return;}
                if(output==null) output=CropSettings.Black();
                CropSettings.RenderInto(output,e.Frame,crop);
                lastFrame=DateTime.UtcNow.Ticks;
                SourceSize=e.Frame.Size;
                if(PreviewVisible) {
                    if(preview==null) preview=new Bitmap(PreviewWidth,PreviewHeight,PixelFormat.Format32bppArgb);
                    using(var g=Graphics.FromImage(preview)) {
                        g.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.Bilinear;
                        g.DrawImage(e.Frame,0,0,PreviewWidth,PreviewHeight);
                    }
                    previewStamp++;previewValid=true;
                }
                sender.Send(output);
            } catch(Exception ex) {Error=ex.Message;}
        }
    }
    public Bitmap Snapshot(bool result) {lock(gate) {Bitmap b=result?output:previewValid?preview:null;return b==null?null:(Bitmap)b.Clone();}}
    // Copies the newest preview frame (BGRA, PreviewWidth x PreviewHeight) into a buffer when it is newer than seen.
    public bool CopyPreview(IntPtr target,int stride,ref long seen) {lock(gate) {
        if(!previewValid || previewStamp==seen) return false;
        var data=preview.LockBits(new Rectangle(0,0,PreviewWidth,PreviewHeight),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
        try { unsafe { for(int y=0;y<PreviewHeight;y++)
            Buffer.MemoryCopy((byte*)data.Scan0+y*data.Stride,(byte*)target+y*stride,stride,PreviewWidth*4); } }
        finally { preview.UnlockBits(data); }
        seen=previewStamp;
        return true;
    }}
    // Keeps the virtual cameras fed while no camera frames arrive (privacy, start-up, stalled capture).
    long lastFrame;
    public void PulseClosed() {lock(gate) {
        if(!LiveLocked() && output!=null) sender.Send(output);
    }}
    // True while camera frames reach the apps (not the privacy picture).
    public bool Live {get {lock(gate) return LiveLocked();}}
    bool LiveLocked() {return allowed && Running && DateTime.UtcNow.Ticks-lastFrame<TimeSpan.FromMilliseconds(500).Ticks;}
    public void Stop() {
        stopping=true;
        if(capture!=null) {capture.SignalToStop();capture.WaitForStop();capture.NewFrame-=OnFrame;capture=null;}
        lock(gate) ShowIdle();
    }
    public void Dispose() {Stop(); lock(gate) {if(output!=null) output.Dispose();sender.Dispose();}}
}
