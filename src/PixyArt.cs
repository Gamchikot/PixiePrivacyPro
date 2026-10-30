using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

// The PIXY head drawn as vectors for the Home orb and the control center tile.
// Closed: the head is bowed (as the real camera in privacy), lenses hidden, a green lock on its top.
// Open: the head faces you, the lenses in their silver rings glow red with a red LED under them.
// Waiting: the head faces you with dark lenses and the LED off. Unknown: a white silhouette.
public static class PixyArt {
    public const int Closed=2,Open=1,Waiting=3,Unknown=-1;
    // The head spans x 150..874, y 270..690 of the 1024 drawing.
    const double Left=150,Top=270,Width=724,Height=420;

    public static int State(int mode) {return mode==2?Closed:mode<0?Unknown:mode==3?Waiting:Open;}

    public static FrameworkElement Draw(int state,double width) {
        var canvas=new Canvas {Width=Width,Height=Height};
        if(state==Unknown) {
            var white=new SolidColorBrush(Color.FromArgb(235,255,255,255));
            Add(canvas,RoundRect(150,330,130,300,40),white);Add(canvas,RoundRect(744,330,130,300,40),white);
            Add(canvas,RoundRect(230,270,564,420,190),white);
        } else {
            var side=Vertical("#3A3E46","#16181C");
            Add(canvas,RoundRect(150,330,130,300,40),side);Add(canvas,RoundRect(744,330,130,300,40),side);
            if(state==Closed) Bowed(canvas); else Facing(canvas,state==Open);
        }
        return new Viewbox {Width=width,Height=width*Height/Width,Child=canvas,IsHitTestVisible=false};
    }

    static void Bowed(Canvas canvas) {
        Add(canvas,RoundRect(230,270,564,420,190),Vertical("#5C616C","#22242A"));
        canvas.Children.Add(new Path {Data=Geometry.Parse("M 312,402 A 212,150 0 0 1 712,402"),Stroke=new SolidColorBrush(Color.FromArgb(70,255,255,255)),StrokeThickness=6,RenderTransform=Shift()});
        foreach(double x in new[]{470,496,528,554}) Add(canvas,new EllipseGeometry(new Point(x,336),6,6),Brush("#1E2025"));
        Add(canvas,RoundRect(300,612,424,52,26),Vertical("#121316","#040406"));
        // The lock: shackle, body and keyhole, green as the orb.
        var green=Brush("#46D670");
        canvas.Children.Add(new Path {Data=Geometry.Parse("M 465.5,475 V 436 A 46.5,46.5 0 0 1 558.5,436 V 475"),Stroke=green,StrokeThickness=24,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,RenderTransform=Shift()});
        Add(canvas,RoundRect(437,462,150,120,24),green);
        var hole=Brush("#22242A");
        Add(canvas,new EllipseGeometry(new Point(512,505),13,13),hole);
        Add(canvas,RoundRect(505,505,14,38,6),hole);
    }

    static void Facing(Canvas canvas,bool live) {
        Add(canvas,RoundRect(230,270,564,420,190),Vertical("#4A4E57","#1C1E23"));
        Add(canvas,RoundRect(290,330,444,300,150),Vertical("#18191D","#060709"));
        canvas.Children.Add(new Path {Data=new RectangleGeometry(new Rect(290-Left,330-Top,444,300),150,150),Stroke=Brush("#50545C"),StrokeThickness=6});
        Add(canvas,RoundRect(330,345,364,70,35),new SolidColorBrush(Color.FromArgb(28,255,255,255)));
        foreach(double cx in new[]{410,614}) {
            if(live) Add(canvas,new EllipseGeometry(new Point(cx,450),88,88),Glow(Color.FromRgb(255,70,60)));
            canvas.Children.Add(new Path {Data=new EllipseGeometry(new Point(cx-Left,450-Top),62,62),Stroke=Brush("#CDD2DA"),StrokeThickness=22});
            Add(canvas,new EllipseGeometry(new Point(cx,450),40,40),live?Lens():Brush("#262B36"));
        }
        if(live) {
            Add(canvas,RoundRect(452,525,120,64,32),Glow(Color.FromRgb(255,70,60)));
            Add(canvas,RoundRect(478,545,68,24,12),Brush("#FF4A3D"));
        } else Add(canvas,RoundRect(478,545,68,24,12),Brush("#2A2D33"));
    }

    static Brush Lens() {
        var b=new RadialGradientBrush {GradientOrigin=new Point(.4,.35)};
        b.GradientStops.Add(new GradientStop(Color.FromRgb(255,150,140),0));
        b.GradientStops.Add(new GradientStop(Color.FromRgb(230,40,30),.45));
        b.GradientStops.Add(new GradientStop(Color.FromRgb(90,8,8),1));
        return b;
    }
    static Brush Glow(Color c) {
        var b=new RadialGradientBrush();
        b.GradientStops.Add(new GradientStop(Color.FromArgb(150,c.R,c.G,c.B),.45));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0,c.R,c.G,c.B),1));
        return b;
    }
    static Brush Brush(string hex) {return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));}
    static Brush Vertical(string top,string bottom) {
        return new LinearGradientBrush((Color)ColorConverter.ConvertFromString(top),(Color)ColorConverter.ConvertFromString(bottom),new Point(.5,0),new Point(.5,1));
    }
    static Transform Shift() {return new TranslateTransform(-Left,-Top);}
    static Geometry RoundRect(double x,double y,double w,double h,double r) {return new RectangleGeometry(new Rect(x,y,w,h),r,r);}
    static void Add(Canvas canvas,Geometry shape,Brush fill) {canvas.Children.Add(new Path {Data=shape,Fill=fill,RenderTransform=Shift()});}
}
