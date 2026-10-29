using System;
using System.Drawing;
using System.Windows.Forms;
class LayoutCheck {
    // DrawToBitmap skips nested controls (group boxes), so draw them level by level.
    static void Draw(Graphics g,Control.ControlCollection controls,int x,int y) {
        foreach(Control c in controls) {
            if(!c.Visible) continue;
            using(var part=new Bitmap(c.Width,c.Height)) {c.DrawToBitmap(part,new Rectangle(Point.Empty,part.Size));g.DrawImageUnscaled(part,x+c.Left,y+c.Top);}
            Draw(g,c.Controls,x+c.Left,y+c.Top);
        }
    }
    static void SaveOverDesktop(EyeAlert alert,string file) {
        using(var image=alert.RenderImage()) using(var bitmap=new Bitmap(image.Width,image.Height)) {
            using(var g=Graphics.FromImage(bitmap)) {
                g.FillRectangle(new SolidBrush(Color.FromArgb(32,32,36)),0,0,image.Width/2,image.Height);
                g.FillRectangle(new SolidBrush(Color.FromArgb(245,245,245)),image.Width/2,0,image.Width/2,image.Height);
                g.DrawImage(image,0,0);
            }
            if(image.GetPixel(2,2).A!=0) throw new Exception("Alert corner is not transparent");
            bitmap.Save(file);
        }
    }
    [STAThread] static void Main() {
        Application.EnableVisualStyles();
        using(var window=new MainWindow(false,true)) using(var bitmap=new Bitmap(window.Width,window.Height)) {
            // Control.Visible is false until the form is shown, so show it off screen first.
            window.StartPosition=FormStartPosition.Manual;window.Location=new Point(-4000,-4000);window.ShowInTaskbar=false;window.Show();Application.DoEvents();
            window.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));
            using(var g=Graphics.FromImage(bitmap)) Draw(g,window.Controls,8,31);
            bitmap.Save("layout.png");
        }
        // The alert has no background: show it over dark and light halves to check readability.
        using(var alert=new EyeAlert()) SaveOverDesktop(alert,"alert.png");
        foreach(Placeholder.Lock kind in Enum.GetValues(typeof(Placeholder.Lock))) using(var frame=Placeholder.Render(kind)) frame.Save("placeholder-"+kind.ToString().ToLower()+".png");
        if(!MainWindow.ShouldAlert(2,3,false)||!MainWindow.ShouldAlert(2,1,false)||MainWindow.ShouldAlert(2,0,true)||MainWindow.ShouldAlert(2,-1,false)||MainWindow.ShouldAlert(1,1,false))throw new Exception("Alert transition test failed");
        Console.WriteLine("PASS offline layout render and external privacy transition rules");
    }
}
