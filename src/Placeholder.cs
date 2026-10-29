using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

// 1280x720 frames sent to the Pixie cameras instead of video. No text: apps often mirror the picture.
public static class Placeholder {
    public enum Lock {Closed,Open,Unknown}
    static readonly Dictionary<Lock,Bitmap> cache=new Dictionary<Lock,Bitmap>();
    // The user's privacy picture, already fitted to 1280x720. Bitmaps handed out are never disposed:
    // the video pipeline may still be drawing the previous one.
    public static string CustomPath=Path.Combine(Preferences.DataFolder,"privacy-image.png");
    static Bitmap custom; static bool customLoaded;

    // What apps reading a Pixie camera see whenever there is no video: the user's picture or the closed lock.
    public static Bitmap Privacy() {return Custom() ?? Get(Lock.Closed);}
    // The window's "result" box also shows why there is no video. mode: PIXY mode (-1 unknown, 2 privacy, otherwise starting).
    public static Bitmap Status(int mode) {
        if(mode==2) return Privacy();
        return Get(mode<0?Lock.Unknown:Lock.Open);
    }

    static Bitmap Get(Lock kind) {
        lock(cache) {
            Bitmap bitmap;
            if(!cache.TryGetValue(kind,out bitmap)) cache[kind]=bitmap=Render(kind);
            return bitmap;
        }
    }

    // The Home page orb at video size: glow, halo and ring, a gradient ball with a shine and a white symbol.
    // Kept left-right symmetric, so it looks the same in apps that mirror the picture.
    public static Bitmap Render(Lock kind) {
        var bitmap=new Bitmap(1280,720,PixelFormat.Format32bppArgb);
        Color light,mid,dark;
        if(kind==Lock.Closed) {light=Color.FromArgb(0x5B,0xEA,0x86);mid=Color.FromArgb(0x2D,0xBE,0x57);dark=Color.FromArgb(0x17,0x8A,0x3A);}
        else if(kind==Lock.Open) {light=Color.FromArgb(0xFF,0x7B,0x6F);mid=Color.FromArgb(0xE5,0x39,0x2E);dark=Color.FromArgb(0xA5,0x1F,0x17);}
        else {light=Color.FromArgb(0xB4,0xB7,0xBE);mid=Color.FromArgb(0x80,0x84,0x8C);dark=Color.FromArgb(0x56,0x5A,0x62);}
        const float cx=640,cy=360,r=160,k=r*2/164f; // k: the Home orb is 164 DIP wide
        using(var g=Graphics.FromImage(bitmap)) {
            g.SmoothingMode=SmoothingMode.AntiAlias;
            g.Clear(Color.FromArgb(20,22,27));
            // Glow around the ball.
            float gr=r+150;
            using(var path=new GraphicsPath()) {
                path.AddEllipse(cx-gr,cy-gr,gr*2,gr*2);
                using(var glow=new PathGradientBrush(path)) {
                    glow.CenterPoint=new PointF(cx,cy);
                    glow.InterpolationColors=new ColorBlend {
                        Colors=new[]{Color.FromArgb(0,mid),Color.FromArgb(22,mid),Color.FromArgb(80,mid),Color.FromArgb(150,mid)},
                        Positions=new[]{0f,.25f,.5f,1f}};
                    g.FillPath(glow,path);
                }
            }
            using(var halo=new Pen(Color.FromArgb(60,mid),1.5f*k)) g.DrawEllipse(halo,cx-94*k,cy-94*k,188*k,188*k);
            using(var ring=new Pen(Color.FromArgb(150,mid),2.5f*k)) g.DrawEllipse(ring,cx-90*k,cy-90*k,180*k,180*k);
            // The ball: a radial gradient lit from above, as on the Home page but centred horizontally.
            using(var ball=new GraphicsPath()) {
                ball.AddEllipse(cx-r,cy-r,r*2,r*2);
                float br=r*1.44f,by=cy-r*.28f;
                using(var path=new GraphicsPath()) {
                    path.AddEllipse(cx-br,by-br,br*2,br*2);
                    using(var fill=new PathGradientBrush(path)) {
                        fill.CenterPoint=new PointF(cx,cy-r*.4f);
                        fill.InterpolationColors=new ColorBlend {Colors=new[]{dark,dark,mid,light},Positions=new[]{0f,.3f,.6f,1f}};
                        g.FillPath(fill,ball);
                    }
                }
                using(var shine=new LinearGradientBrush(new PointF(0,cy-r-1),new PointF(0,cy-r+r*1.1f),Color.FromArgb(70,255,255,255),Color.FromArgb(0,255,255,255))) {
                    shine.WrapMode=WrapMode.TileFlipX;
                    var clip=g.Clip;g.SetClip(new RectangleF(0,0,1280,cy-r+r*1.1f));
                    g.FillPath(shine,ball);
                    g.Clip=clip;
                }
            }
            // The symbol, drawn on the 24-unit grid of the window's icons: lock, eye (camera starting) or question.
            float s=66*k/24;
            var state=g.Save();
            g.TranslateTransform(cx-12*s,cy-12*s);g.ScaleTransform(s,s);
            using(var pen=new Pen(Color.White,2.1f) {StartCap=LineCap.Round,EndCap=LineCap.Round,LineJoin=LineJoin.Round})
            using(var white=new SolidBrush(Color.White))
            using(var icon=new GraphicsPath()) {
                if(kind==Lock.Closed) {
                    icon.StartFigure();
                    icon.AddArc(4.5f,10.5f,4,4,180,90);icon.AddArc(15.5f,10.5f,4,4,270,90);
                    icon.AddArc(15.5f,16.5f,4,4,0,90);icon.AddArc(4.5f,16.5f,4,4,90,90);icon.CloseFigure();
                    icon.StartFigure();
                    icon.AddLine(8,10.5f,8,7.5f);icon.AddArc(8,3.5f,8,8,180,180);icon.AddLine(16,7.5f,16,10.5f);
                    g.DrawPath(pen,icon);
                    g.FillEllipse(white,10.4f,13.9f,3.2f,3.2f);
                } else if(kind==Lock.Open) {
                    icon.StartFigure();
                    icon.AddBezier(2,12,4.5f,7.2f,8,5,12,5);icon.AddBezier(12,5,16,5,19.5f,7.2f,22,12);
                    icon.AddBezier(22,12,19.5f,16.8f,16,19,12,19);icon.AddBezier(12,19,8,19,4.5f,16.8f,2,12);icon.CloseFigure();
                    g.DrawPath(pen,icon);
                    g.FillEllipse(white,9,9,6,6);
                } else {
                    icon.StartFigure();
                    icon.AddArc(9,6,6,6,180,246);
                    icon.AddBezier(13.2f,11.7f,12.4f,12.1f,12,12.7f,12,13.6f);icon.AddLine(12,13.6f,12,14.5f);
                    g.DrawPath(pen,icon);
                    g.FillEllipse(white,10.95f,16.6f,2.1f,2.1f);
                }
            }
            g.Restore(state);
        }
        return bitmap;
    }

    // Picture shown in privacy instead of the closed lock: the user's image if one is set.
    public static Bitmap Custom() {
        lock(cache) {
            if(!customLoaded) {
                customLoaded=true;
                try {if(File.Exists(CustomPath)) using(var file=new Bitmap(CustomPath)) custom=new Bitmap(file);}
                catch {custom=null;}
            }
            return custom;
        }
    }
    public static bool HasCustom {get {return Custom()!=null;}}

    // Fits the image into 1280x720 without cropping (black bars) and keeps a copy in the profile,
    // so the original file can be moved or deleted.
    public static void SetCustom(string source) {
        Bitmap fitted;
        using(var image=Image.FromFile(source)) fitted=Fit(image);
        Directory.CreateDirectory(Path.GetDirectoryName(CustomPath));
        fitted.Save(CustomPath,ImageFormat.Png);
        lock(cache) {custom=fitted;customLoaded=true;}
    }
    public static void ClearCustom() {
        if(File.Exists(CustomPath)) File.Delete(CustomPath);
        lock(cache) {custom=null;customLoaded=true;}
    }

    public static Bitmap Fit(Image image) {
        var bitmap=new Bitmap(1280,720,PixelFormat.Format32bppArgb);
        using(var g=Graphics.FromImage(bitmap)) {
            g.Clear(Color.Black);
            g.InterpolationMode=InterpolationMode.HighQualityBicubic;
            float scale=Math.Min(1280f/image.Width,720f/image.Height);
            float w=image.Width*scale,h=image.Height*scale;
            g.DrawImage(image,(1280-w)/2,(720-h)/2,w,h);
        }
        return bitmap;
    }
}

public enum CaptureAction {None,Start,Stop,OpenLens}

// When Pro captures from PIXY: only while an app reads a Pixie camera or the user asked for the preview.
public static class CaptureRules {
    // mode: PIXY mode; arrived: an app started reading in this tick; autoOpen: leave privacy for such an app.
    public static CaptureAction Decide(bool manualPreview,bool consumerRecent,bool arrived,int mode,bool autoOpen,bool running) {
        bool wanted=manualPreview || consumerRecent;
        if(!wanted) return running ? CaptureAction.Stop : CaptureAction.None;
        if(mode==2) {
            if(running) return CaptureAction.Stop; // privacy enabled elsewhere, e.g. on the camera
            return arrived && autoOpen ? CaptureAction.OpenLens : CaptureAction.None;
        }
        if(mode<0) return CaptureAction.None;
        return running ? CaptureAction.None : CaptureAction.Start;
    }
    // Lens left open without Pro's capture: opened by Windows Settings, an app that read PIXY directly, or the tray.
    // cameraApps: apps Windows reports as using any camera right now.
    public static bool IdleLens(bool autoPrivacy,int mode,bool running,bool manualPreview,bool consumerRecent,int cameraApps) {
        return autoPrivacy && mode>=0 && mode!=2 && !running && !manualPreview && !consumerRecent && cameraApps==0;
    }
    // Every USB request restarts the camera's own privacy timer, which counts only while no video streams (mode 3).
    // Then the lens is read less often than the timer, so the camera can close itself.
    public static int CameraPollSeconds(int mode,bool streaming,int privacyTimer) {
        return mode==3 && !streaming && privacyTimer>0 ? privacyTimer+5 : 1;
    }
    // Leaving privacy: the lens rises and the exposure settles for about two seconds; apps keep the placeholder meanwhile.
    public static readonly TimeSpan RevealDelay=TimeSpan.FromSeconds(2);
}
