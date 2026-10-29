using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// Warning without a background: the burning eye and "Я вижу тебя" straight over the desktop.
// The eye is one picture (eye.png, embedded) brought to life frame by frame: the flames ripple, the slit pupil
// breathes and the iris glows. A layered window with per-pixel alpha keeps the edges soft; it never takes focus or clicks.
public sealed class EyeAlert : Form {
    public static string Headline {get {return L.English?"I see you":"Я вижу тебя";}}
    // The eye fades in, waits a moment, the sound plays, after it the same pause and the eye fades out.
    // Without a sound the eye stays SilentHold seconds instead.
    const double FadeIn=.4,FadeOut=.7,Loop=2.4,Pause=.5,SilentHold=2.4;
    readonly double life,soundAt;
    Action sound;
    readonly Timer frames=new Timer {Interval=33};
    DateTime started;
    readonly int width,eyeHeight,textHeight;
    int[] textLayer;
    LayeredSurface surface;
    readonly double[] rowA,rowB,colA,colB;

    public EyeAlert():this(null,0) {}
    // sound starts after the fade-in and a pause; soundSeconds is how long it lasts.
    public EyeAlert(Action sound,double soundSeconds) {
        this.sound=sound;
        soundAt=FadeIn+Pause;
        life=soundAt+(sound!=null?Math.Max(1,soundSeconds):SilentHold)+Pause+FadeOut;
        LoadSource();
        FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=true;StartPosition=FormStartPosition.Manual;
        float scale=DpiScale();
        width=(int)(260*scale);eyeHeight=width*sourceHeight/sourceWidth;textHeight=(int)(44*scale);
        ClientSize=new Size(width,eyeHeight+textHeight);
        Rectangle screen=Screen.PrimaryScreen.WorkingArea;
        Location=new Point(screen.Left+(screen.Width-width)/2,screen.Top+(int)(12*scale));
        rowA=new double[eyeHeight];rowB=new double[eyeHeight];colA=new double[width];colB=new double[width];
        frames.Tick+=delegate {NextFrame();};
        Shown+=delegate {started=DateTime.UtcNow;NextFrame();frames.Start();};
    }
    protected override bool ShowWithoutActivation {get{return true;}}
    // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT | WS_EX_LAYERED
    protected override CreateParams CreateParams {get {var p=base.CreateParams;p.ExStyle|=0x08000000|0x00000080|0x20|0x80000;return p;}}
    [DllImport("user32.dll")] static extern int GetDpiForSystem();
    static float DpiScale() {try {return GetDpiForSystem()/96f;} catch(EntryPointNotFoundException) {return 1;}}

    // ---------- source picture ----------
    static int[] source;static int sourceWidth,sourceHeight;
    static Bitmap LoadEyeBitmap() {
        try {
            var stream=typeof(EyeAlert).Assembly.GetManifestResourceStream("eye.png");
            if(stream!=null) using(stream) using(var file=new Bitmap(stream)) return new Bitmap(file);
        } catch {}
        // Without the picture (test builds) a drawn eye keeps the alert working.
        var drawn=new Bitmap(900,628,PixelFormat.Format32bppArgb);
        using(var g=Graphics.FromImage(drawn)) {g.SmoothingMode=SmoothingMode.AntiAlias;DrawSauron(g,new RectangleF(110,212,680,276));}
        return drawn;
    }
    static void LoadSource() {
        if(source!=null) return;
        using(var bitmap=LoadEyeBitmap()) {
            sourceWidth=bitmap.Width;sourceHeight=bitmap.Height;
            source=Pixels(bitmap);
        }
    }
    // Premultiplied ARGB: what UpdateLayeredWindow expects, and it blends correctly when sampled.
    static int[] Pixels(Bitmap bitmap) {
        var data=bitmap.LockBits(new Rectangle(0,0,bitmap.Width,bitmap.Height),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
        var pixels=new int[bitmap.Width*bitmap.Height];
        try {Marshal.Copy(data.Scan0,pixels,0,pixels.Length);} finally {bitmap.UnlockBits(data);}
        for(int i=0;i<pixels.Length;i++) {
            uint p=(uint)pixels[i],a=p>>24;
            if(a==255) continue;
            uint r=(p>>16&255)*a/255,g=(p>>8&255)*a/255,b=(p&255)*a/255;
            pixels[i]=(int)(a<<24|r<<16|g<<8|b);
        }
        return pixels;
    }
    public static System.Windows.Media.Imaging.BitmapSource EyeImageSource() {
        using(var bitmap=LoadEyeBitmap()) return new WriteableBitmapHolder().Update(bitmap);
    }

    // ---------- headline ----------
    int[] TextLayer() {
        if(textLayer!=null) return textLayer;
        using(var bitmap=new Bitmap(width,textHeight,PixelFormat.Format32bppArgb)) {
            using(var g=Graphics.FromImage(bitmap)) {
                g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(Color.Transparent);
                float size=textHeight*.52f;
                using(var family=new FontFamily("Georgia")) using(var path=new GraphicsPath())
                using(var format=new StringFormat {Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center}) {
                    path.AddString(Headline,family,(int)FontStyle.Bold,size,new RectangleF(0,0,width,textHeight),format);
                    // Ember glow, a dark rim for light windows, then the gold-to-fire fill.
                    for(int i=6;i>=1;i--) using(var glow=new Pen(Color.FromArgb(22,255,90,10),i*size*.09f) {LineJoin=LineJoin.Round}) g.DrawPath(glow,path);
                    using(var rim=new Pen(Color.FromArgb(220,40,8,2),size*.1f) {LineJoin=LineJoin.Round}) g.DrawPath(rim,path);
                    var bounds=path.GetBounds();
                    using(var fill=new LinearGradientBrush(new PointF(0,bounds.Top),new PointF(0,bounds.Bottom),Color.FromArgb(255,240,180),Color.FromArgb(255,110,20))) g.FillPath(fill,path);
                }
            }
            textLayer=Pixels(bitmap);
        }
        return textLayer;
    }

    // ---------- animation ----------
    void NextFrame() {
        double elapsed=(DateTime.UtcNow-started).TotalSeconds;
        if(elapsed>=life) {frames.Stop();Close();return;}
        if(sound!=null && elapsed>=soundAt) {var play=sound;sound=null;play();}
        double appear=Math.Min(1,elapsed/FadeIn),ease=1-Math.Pow(1-appear,3);
        double zoom=.82+.18*ease+.03*Math.Sin(Math.PI*appear);
        double fade=Math.Min(ease,Math.Min(1,(life-elapsed)/FadeOut));
        if(surface==null) surface=new LayeredSurface(width,eyeHeight+textHeight);
        Render(surface,(elapsed%Loop)/Loop,zoom);
        surface.Show(this,(byte)(255*Math.Max(0,fade)));
    }
    unsafe void Render(LayeredSurface target,double t,double zoom) {
        int* dst=(int*)target.Bits;
        RenderEye(dst,t,zoom);
        var text=TextLayer();
        int offset=width*eyeHeight;
        Marshal.Copy(text,0,(IntPtr)(dst+offset),text.Length);
    }
    // t runs 0..1 over one loop; every motion uses whole periods of t, so the loop has no seam.
    unsafe void RenderEye(int* dst,double t,double zoom) {
        int w=width,h=eyeHeight,sw=sourceWidth,sh=sourceHeight;
        double step=sw/(double)w;
        double cx=sw*.502,cy=sh*.56,ax=sw*.445,ay=sh*.24,irx=sw*.17,iry=sh*.27;
        double p=2*Math.PI*t,amp=sw*.0075;
        for(int y=0;y<h;y++) {double s=y*step;rowA[y]=Math.Sin(s*.045+p);rowB[y]=Math.Sin(s*.07-2*p);}
        for(int x=0;x<w;x++) {double s=x*step;colA[x]=Math.Sin(s*.05-2*p);colB[x]=Math.Sin(s*.031+3*p);}
        double pupil=.17*Math.Sin(p),glow=.14*Math.Sin(2*p);
        fixed(int* src=source) {
            for(int y=0;y<h;y++) {
                int* row=dst+y*w;
                for(int x=0;x<w;x++) {
                    double sx=cx+(x*step-cx)/zoom,sy=cy+(y*step-cy)/zoom;
                    // Flame weight: 0 inside the almond of the eye, 1 out in the flames.
                    double nx=(sx-cx)/ax,ny=(sy-cy)/ay;
                    double d=(ny<0?-ny:ny)-(1-nx*nx);
                    double fw=d<=-.2?0:d>=.3?1:Smooth((d+.2)/.5);
                    double ix=(sx-cx)/irx,iy=(sy-cy)/iry,ir=ix*ix+iy*iy;
                    double iris=ir<1?(1-ir)*(1-ir):0;
                    if(fw>0) {
                        sx+=amp*fw*(.7*rowA[y]+.3*colB[x]);
                        sy+=amp*fw*(.6*colA[x]+.4*rowB[y])-amp*.35*fw*(1+colB[x]);
                    }
                    if(iris>0) sx=cx+(sx-cx)*(1-pupil*iris);
                    int c=Sample(src,sw,sh,sx,sy);
                    double k=1+glow*iris+.07*fw*rowA[y]*colA[x];
                    row[x]=k==1?c:Bright(c,k);
                }
            }
        }
    }
    static double Smooth(double v) {return v*v*(3-2*v);}
    static unsafe int Sample(int* src,int sw,int sh,double x,double y) {
        if(x<0||y<0||x>=sw-1||y>=sh-1) return 0;
        int x0=(int)x,y0=(int)y;double fx=x-x0,fy=y-y0;
        int* p=src+y0*sw+x0;
        uint a=(uint)p[0],b=(uint)p[1],c=(uint)p[sw],d=(uint)p[sw+1];
        if((a|b|c|d)==0) return 0;
        double w00=(1-fx)*(1-fy),w10=fx*(1-fy),w01=(1-fx)*fy,w11=fx*fy;
        uint A=(uint)((a>>24)*w00+(b>>24)*w10+(c>>24)*w01+(d>>24)*w11+.5);
        uint R=(uint)((a>>16&255)*w00+(b>>16&255)*w10+(c>>16&255)*w01+(d>>16&255)*w11+.5);
        uint G=(uint)((a>>8&255)*w00+(b>>8&255)*w10+(c>>8&255)*w01+(d>>8&255)*w11+.5);
        uint B=(uint)((a&255)*w00+(b&255)*w10+(c&255)*w01+(d&255)*w11+.5);
        return (int)(A<<24|R<<16|G<<8|B);
    }
    // Brightens a premultiplied pixel; colour channels never exceed alpha.
    static int Bright(int c,double k) {
        uint p=(uint)c,a=p>>24;
        uint r=(uint)Math.Min(a,(p>>16&255)*k),g=(uint)Math.Min(a,(p>>8&255)*k),b=(uint)Math.Min(a,(p&255)*k);
        return (int)(a<<24|r<<16|g<<8|b);
    }

    // One still frame (straight alpha) for tests and screenshots.
    public unsafe Bitmap RenderImage() {
        using(var frame=new LayeredSurface(width,eyeHeight+textHeight)) {
            Render(frame,.25,1);
            var bitmap=new Bitmap(width,eyeHeight+textHeight,PixelFormat.Format32bppArgb);
            var data=bitmap.LockBits(new Rectangle(0,0,bitmap.Width,bitmap.Height),ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);
            try {
                int* src=(int*)frame.Bits,dst=(int*)data.Scan0;
                for(int i=0;i<bitmap.Width*bitmap.Height;i++) {
                    uint p=(uint)src[i],a=p>>24;
                    if(a==0) {dst[i]=0;continue;}
                    uint r=Math.Min(255,(p>>16&255)*255/a),g=Math.Min(255,(p>>8&255)*255/a),b=Math.Min(255,(p&255)*255/a);
                    dst[i]=(int)(a<<24|r<<16|g<<8|b);
                }
            } finally {bitmap.UnlockBits(data);}
            return bitmap;
        }
    }

    // Simple drawn eye: the fallback when eye.png is missing.
    static GraphicsPath Almond(RectangleF r) {
        var path=new GraphicsPath();
        path.AddBezier(r.Left,r.Top+r.Height/2,r.Left+r.Width/3,r.Top,r.Right-r.Width/3,r.Top,r.Right,r.Top+r.Height/2);
        path.AddBezier(r.Right,r.Top+r.Height/2,r.Right-r.Width/3,r.Bottom,r.Left+r.Width/3,r.Bottom,r.Left,r.Top+r.Height/2);
        path.CloseFigure();
        return path;
    }
    public static void DrawSauron(Graphics g,RectangleF r) {
        for(int i=12;i>=1;i--) {
            float grow=i*2.5f;
            using(var glow=Almond(new RectangleF(r.X-grow*1.6f,r.Y-grow,r.Width+grow*3.2f,r.Height+grow*2)))
            using(var brush=new SolidBrush(Color.FromArgb(14,255,70,0))) g.FillPath(brush,glow);
        }
        using(var eye=Almond(r)) {
            using(var fire=new PathGradientBrush(eye)) {
                fire.CenterPoint=new PointF(r.X+r.Width/2,r.Y+r.Height/2);
                fire.CenterColor=Color.FromArgb(255,255,236,140);
                fire.SurroundColors=new[]{Color.FromArgb(255,190,25,0)};
                fire.FocusScales=new PointF(.25f,.2f);
                g.FillPath(fire,eye);
            }
            using(var rim=new Pen(Color.FromArgb(255,120,10,0),4)) g.DrawPath(rim,eye);
        }
        float slitWidth=r.Width*.075f,top=r.Y+r.Height*.06f,bottom=r.Bottom-r.Height*.06f,cx=r.X+r.Width/2;
        using(var slit=new GraphicsPath()) {
            slit.AddBezier(cx,top,cx+slitWidth,top+r.Height*.3f,cx+slitWidth,bottom-r.Height*.3f,cx,bottom);
            slit.AddBezier(cx,bottom,cx-slitWidth,bottom-r.Height*.3f,cx-slitWidth,top+r.Height*.3f,cx,top);
            slit.CloseFigure();
            using(var brush=new SolidBrush(Color.FromArgb(255,15,0,0))) g.FillPath(brush,slit);
        }
    }
    protected override void Dispose(bool disposing) {
        if(disposing) {frames.Dispose();if(surface!=null) surface.Dispose();}
        base.Dispose(disposing);
    }
}

// A 32-bit top-down DIB the alert draws into directly, pushed to the layered window each frame.
sealed class LayeredSurface : IDisposable {
    [StructLayout(LayoutKind.Sequential)] struct Size32 {public int cx,cy;}
    [StructLayout(LayoutKind.Sequential)] struct Point32 {public int x,y;}
    [StructLayout(LayoutKind.Sequential,Pack=1)] struct Blend {public byte op,flags,alpha,format;}
    [StructLayout(LayoutKind.Sequential)] struct BitmapInfoHeader {public int size,width,height;public short planes,bitCount;public int compression,sizeImage,xPels,yPels,clrUsed,clrImportant;}
    [DllImport("user32.dll",SetLastError=true)] static extern bool UpdateLayeredWindow(IntPtr hwnd,IntPtr dst,ref Point32 pos,ref Size32 size,IntPtr src,ref Point32 srcPos,int key,ref Blend blend,int flags);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd,IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc,IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr dc,ref BitmapInfoHeader info,int usage,out IntPtr bits,IntPtr section,int offset);
    readonly IntPtr memory,bitmap,old;
    public readonly IntPtr Bits;
    readonly int width,height;
    public LayeredSurface(int width,int height) {
        this.width=width;this.height=height;
        var header=new BitmapInfoHeader {size=40,width=width,height=-height,planes=1,bitCount=32};
        IntPtr screen=GetDC(IntPtr.Zero);
        try {
            memory=CreateCompatibleDC(screen);
            bitmap=CreateDIBSection(screen,ref header,0,out Bits,IntPtr.Zero,0);
            old=SelectObject(memory,bitmap);
        } finally {ReleaseDC(IntPtr.Zero,screen);}
    }
    public void Show(Form form,byte alpha) {
        IntPtr screen=GetDC(IntPtr.Zero);
        try {
            var size=new Size32 {cx=width,cy=height};
            var pos=new Point32 {x=form.Left,y=form.Top};var origin=new Point32();
            var blend=new Blend {op=0,flags=0,alpha=alpha,format=1};
            UpdateLayeredWindow(form.Handle,screen,ref pos,ref size,memory,ref origin,0,ref blend,2);
        } finally {ReleaseDC(IntPtr.Zero,screen);}
    }
    public void Dispose() {SelectObject(memory,old);DeleteObject(bitmap);DeleteDC(memory);}
}
