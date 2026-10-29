using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

// Tray icons in the "drop" style: a round glossy drop with a white symbol — green lock (privacy), red eye (open),
// grey question mark (no answer); the tracking icon is a blue drop with a target. Drawn for the real tray size.
public static class TrayIcons {
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
    static readonly Color[] Green={Color.FromArgb(93,240,138),Color.FromArgb(30,158,69)};
    static readonly Color[] Red={Color.FromArgb(255,138,128),Color.FromArgb(212,46,37)};
    static readonly Color[] Grey={Color.FromArgb(178,181,188),Color.FromArgb(112,116,124)};
    static readonly Color[] Blue={Color.FromArgb(110,180,255),Color.FromArgb(10,110,230)};

    // SM_CXSMICON: 16 px at 100 %, 20 at 125 %, 24 at 150 % for a DPI-aware process.
    static int Size {get {int s=GetSystemMetrics(49);return s<16?16:s;}}

    // 0 = privacy (green lock), 1 = open (red eye), 2 = unknown (grey question mark).
    public static Icon Privacy(int state) {return ToIcon(Drop(state,Size));}
    public static Icon Tracking(Color color) {
        var colors=color==Color.Gray?Grey:color==Color.Goldenrod?new[]{Color.FromArgb(255,214,102),Color.FromArgb(214,150,10)}:Blue;
        return ToIcon(Draw(Size,colors,DrawTarget));
    }
    // The same drop for the settings page.
    public static System.Windows.Media.Imaging.BitmapSource PreviewSource(int state) {
        using(var bitmap=Drop(state,44)) return new WriteableBitmapHolder().Update(bitmap);
    }
    public static Bitmap Drop(int state,int size) {
        return state==0?Draw(size,Green,DrawLock):state==1?Draw(size,Red,DrawEye):Draw(size,Grey,DrawQuestion);
    }

    // Drawn 4x larger and scaled down: GDI+ antialiasing at 16 px alone looks rough.
    static Bitmap Draw(int size,Color[] colors,Action<Graphics,float> symbol) {
        int big=size*4;
        using(var large=new Bitmap(big,big)) {
            using(var g=Graphics.FromImage(large)) {
                g.SmoothingMode=SmoothingMode.AntiAlias;g.PixelOffsetMode=PixelOffsetMode.HighQuality;
                var r=new RectangleF(big*.04f,big*.04f,big*.92f,big*.92f);
                using(var path=new GraphicsPath()) {
                    path.AddEllipse(r);
                    using(var fill=new PathGradientBrush(path) {CenterPoint=new PointF(big*.36f,big*.3f),CenterColor=colors[0],SurroundColors=new[]{colors[1]}}) g.FillPath(fill,path);
                }
                symbol(g,big);
            }
            var small=new Bitmap(size,size);
            using(var g=Graphics.FromImage(small)) {
                g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.PixelOffsetMode=PixelOffsetMode.HighQuality;
                g.DrawImage(large,new Rectangle(0,0,size,size));
            }
            return small;
        }
    }
    static void DrawLock(Graphics g,float s) {
        using(var white=new SolidBrush(Color.White)) using(var pen=new Pen(Color.White,s*.085f)) {
            g.DrawArc(pen,s*.355f,s*.2f,s*.29f,s*.34f,180,180);
            g.DrawLine(pen,s*.355f,s*.37f,s*.355f,s*.46f);g.DrawLine(pen,s*.645f,s*.37f,s*.645f,s*.46f);
            using(var body=Rounded(new RectangleF(s*.29f,s*.44f,s*.42f,s*.33f),s*.06f)) g.FillPath(white,body);
            using(var hole=new SolidBrush(Color.FromArgb(30,158,69))) g.FillEllipse(hole,s*.455f,s*.55f,s*.09f,s*.09f);
        }
    }
    static void DrawEye(Graphics g,float s) {
        using(var white=new SolidBrush(Color.White)) using(var path=new GraphicsPath()) {
            path.AddBezier(s*.18f,s*.5f,s*.32f,s*.3f,s*.68f,s*.3f,s*.82f,s*.5f);
            path.AddBezier(s*.82f,s*.5f,s*.68f,s*.7f,s*.32f,s*.7f,s*.18f,s*.5f);
            g.FillPath(white,path);
            using(var iris=new SolidBrush(Color.FromArgb(212,46,37))) g.FillEllipse(iris,s*.4f,s*.4f,s*.2f,s*.2f);
        }
    }
    static void DrawQuestion(Graphics g,float s) {
        using(var font=new Font("Segoe UI",s*.42f,FontStyle.Bold,GraphicsUnit.Pixel)) using(var white=new SolidBrush(Color.White))
        using(var format=new StringFormat {Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center}) g.DrawString("?",font,white,new RectangleF(0,s*.02f,s,s),format);
    }
    static void DrawTarget(Graphics g,float s) {
        using(var pen=new Pen(Color.White,s*.075f)) using(var white=new SolidBrush(Color.White)) {
            g.DrawEllipse(pen,s*.29f,s*.29f,s*.42f,s*.42f);
            g.DrawLine(pen,s*.5f,s*.17f,s*.5f,s*.3f);g.DrawLine(pen,s*.5f,s*.7f,s*.5f,s*.83f);
            g.DrawLine(pen,s*.17f,s*.5f,s*.3f,s*.5f);g.DrawLine(pen,s*.7f,s*.5f,s*.83f,s*.5f);
            g.FillEllipse(white,s*.44f,s*.44f,s*.12f,s*.12f);
        }
    }
    static GraphicsPath Rounded(RectangleF r,float radius) {
        var path=new GraphicsPath();float d=radius*2;
        path.AddArc(r.X,r.Y,d,d,180,90);path.AddArc(r.Right-d,r.Y,d,d,270,90);
        path.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);path.AddArc(r.X,r.Bottom-d,d,d,90,90);
        path.CloseFigure();
        return path;
    }
    static Icon ToIcon(Bitmap bitmap) {
        using(bitmap) {
            IntPtr handle=bitmap.GetHicon();
            try {using(var icon=Icon.FromHandle(handle)) return (Icon)icon.Clone();}
            finally {DestroyIcon(handle);}
        }
    }
}
