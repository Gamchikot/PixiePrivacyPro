using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// Renders every page of the WPF window, the tray control center, the eye alert and the tray icons to PNG
// in both themes, with a fake camera state. No camera needed. UiShots.exe [output folder]
class UiShots {
    static void SetField(object target,string name,object value) {target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(target,value);}
    static object GetField(object target,string name) {return target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(target);}
    static void Call(object target,string name) {target.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.DeclaredOnly,null,Type.EmptyTypes,null).Invoke(target,null);}
    static string folder;

    [STAThread] static void Main(string[] args) {
        // UiShots.exe [folder] [--en]: --en renders the English interface.
        L.English=Array.IndexOf(args,"--en")>=0;
        folder=args.Length>0 && args[0]!="--en"?args[0]:"shots";
        Directory.CreateDirectory(folder);
        var app=new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};
        app.Startup+=async delegate {
            try {await Run();Console.WriteLine("PASS UI shots in "+Path.GetFullPath(folder));}
            catch(Exception e) {Console.WriteLine(e);Environment.ExitCode=1;}
            app.Shutdown();
        };
        app.Run();
    }
    static async Task Run() {
        using(var main=new MainWindow(false,true)) {
            SetField(main,"privacyTimer",60);SetField(main,"audioMode",1);SetField(main,"gestures",1);SetField(main,"hwMirror",0);SetField(main,"hwFlip",0);
            ((System.Windows.Forms.Label)GetField(main,"microphone")).Text=L.T("Микрофон PIXY отключён полностью")+" · "+L.T("защита работает");
            var window=new ModernWindow(main) {Left=-6000,Top=0,ShowActivated=false,WindowStartupLocation=WindowStartupLocation.Manual};
            window.Show();
            foreach(bool dark in new[]{true,false}) {
                Ui.ApplyTheme(window.Resources,dark);
                string theme=dark?"dark":"light";
                foreach(int mode in new[]{2,0}) {
                    SetField(main,"mode",mode);
                    window.Navigate("journal");window.Navigate("home");
                    await Shot(window,"home-"+(mode==2?"closed":"open")+"-"+theme,dark);
                    if(mode!=0) continue;
                    // The preview lives on the camera page.
                    window.Navigate("camera");
                    using(var frame=TestFrame()) window.ShowFrame(frame,null);
                    await Shot(window,"camera-live-"+theme,dark);
                    window.ShowFrame(null,Placeholder.Status(2));
                }
                SetField(main,"mode",2);
                foreach(string page in new[]{"privacy","camera","mic","alert","look","journal"}) {window.Navigate(page);await Shot(window,page+"-"+theme,dark);}
                var center=new ControlCenter(main) {Left=-6000,Top=0,ShowActivated=false};
                Ui.ApplyTheme(center.Resources,dark);
                center.Show();await Task.Delay(300);
                Save((FrameworkElement)center.Content,"center-"+theme,dark?Color.FromRgb(40,52,90):Color.FromRgb(170,190,230));
                // The same with the lens open: the privacy tile stays lit and shows the red eye.
                SetField(main,"mode",0);Call(center,"Render");
                Save((FrameworkElement)center.Content,"center-open-"+theme,dark?Color.FromRgb(40,52,90):Color.FromRgb(170,190,230));
                SetField(main,"mode",2);
                center.Close();
            }
            window.Shutdown();
        }
        using(var alert=new EyeAlert()) using(var image=alert.RenderImage()) using(var both=new System.Drawing.Bitmap(image.Width,image.Height)) {
            using(var g=System.Drawing.Graphics.FromImage(both)) {
                g.FillRectangle(new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(32,34,40)),0,0,image.Width/2,image.Height);
                g.FillRectangle(new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(240,240,240)),image.Width/2,0,image.Width/2,image.Height);
                g.DrawImage(image,0,0);
            }
            if(image.GetPixel(2,2).A!=0) throw new Exception("Alert corner is not transparent");
            both.Save(Path.Combine(folder,"alert.png"));
        }
        using(var strip=new System.Drawing.Bitmap(4*72,72)) {
            using(var g=System.Drawing.Graphics.FromImage(strip)) {
                g.Clear(System.Drawing.Color.FromArgb(28,28,28));
                for(int i=0;i<3;i++) using(var drop=TrayIcons.Drop(i,64)) g.DrawImage(drop,4+i*72,4);
            }
            strip.Save(Path.Combine(folder,"tray-icons.png"));
        }
    }
    static System.Drawing.Bitmap TestFrame() {
        var frame=new System.Drawing.Bitmap(1280,720);
        using(var g=System.Drawing.Graphics.FromImage(frame)) {
            using(var bg=new System.Drawing.Drawing2D.LinearGradientBrush(new System.Drawing.Point(0,0),new System.Drawing.Point(0,720),System.Drawing.Color.FromArgb(70,84,110),System.Drawing.Color.FromArgb(24,28,38))) g.FillRectangle(bg,0,0,1280,720);
            g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using(var person=new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(190,200,215))) {g.FillEllipse(person,560,170,160,180);g.FillEllipse(person,430,380,420,420);}
        }
        return frame;
    }
    static async Task Shot(ModernWindow window,string name,bool dark) {
        Call(window,"Render");
        await Task.Delay(600);
        Call(window,"Render");
        Save((FrameworkElement)window.Content,name,dark?Color.FromRgb(20,22,27):Color.FromRgb(244,245,247));
    }
    static void Save(FrameworkElement element,string name,Color background) {
        element.UpdateLayout();
        int w=(int)Math.Ceiling(element.ActualWidth),h=(int)Math.Ceiling(element.ActualHeight);
        var visual=new DrawingVisual();
        using(var dc=visual.RenderOpen()) {
            dc.DrawRectangle(new SolidColorBrush(background),null,new Rect(0,0,w,h));
            dc.DrawRectangle(new VisualBrush(element),null,new Rect(0,0,w,h));
        }
        var bitmap=new RenderTargetBitmap(w,h,96,96,PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using(var file=File.Create(Path.Combine(folder,name+".png"))) encoder.Save(file);
    }
}
