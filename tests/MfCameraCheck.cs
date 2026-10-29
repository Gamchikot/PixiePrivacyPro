using System;
using System.Drawing;
using System.Threading;
using AForge.Video.DirectShow;

// End-to-end check of the Media Foundation camera: registration, frames through the Frame Server,
// black output once Pro stops writing, removal. Needs PixieVirtualCamera.dll registered (installed MSI).
class MfCameraCheck {
    static int Main() {
        string error=MfCamera.Create();
        Console.WriteLine("Create: "+(error??"OK"));
        if(error!=null) return 1;
        int exit=0;
        try {
            string path=null;
            for(int i=0;i<20 && path==null;i++) {
                foreach(FilterInfo f in new FilterInfoCollection(FilterCategory.VideoInputDevice)) if(f.Name.StartsWith(MfCamera.Name)) path=f.MonikerString;
                if(path==null) Thread.Sleep(250);
            }
            foreach(FilterInfo f in new FilterInfoCollection(FilterCategory.VideoInputDevice)) Console.WriteLine("Camera: "+f.Name);
            if(path==null) {Console.WriteLine("FAIL camera is not listed");return 1;}
            using(var writer=new MfFrameWriter()) using(var frame=new Bitmap(1280,720)) {
                using(var g=Graphics.FromImage(frame)) {g.Clear(Color.Red);g.FillRectangle(Brushes.Blue,0,360,1280,360);}
                var receiver=new VideoCaptureDevice(path);
                foreach(var cap in receiver.VideoCapabilities) {
                    Console.WriteLine("Capability: "+cap.FrameSize.Width+"x"+cap.FrameSize.Height+" @"+cap.AverageFrameRate);
                    if(cap.FrameSize.Width==1280 && cap.FrameSize.Height==720) receiver.VideoResolution=cap;
                }
                int frames=0; bool colored=false,black=false; bool sending=true;
                receiver.NewFrame+=delegate(object s,AForge.Video.NewFrameEventArgs e) {
                    Color top=e.Frame.GetPixel(e.Frame.Width/2,e.Frame.Height/4),bottom=e.Frame.GetPixel(e.Frame.Width/2,e.Frame.Height*3/4);
                    if(frames++%30==0) Console.WriteLine(e.Frame.Width+"x"+e.Frame.Height+" top "+top+" bottom "+bottom);
                    if(top.R>200 && top.B<50 && bottom.B>200 && bottom.R<50) colored=true;
                    if(!sending && top.R<20 && top.G<20 && top.B<20 && bottom.B<20) black=true;
                };
                receiver.VideoSourceError+=delegate(object s,AForge.Video.VideoSourceErrorEventArgs e) {Console.WriteLine("Source error: "+e.Description);};
                receiver.Start();
                try {
                    for(int i=0;i<200 && !colored;i++) {writer.Send(frame);Thread.Sleep(33);}
                    Console.WriteLine((colored?"PASS":"FAIL")+" colored frame through the Frame Server ("+frames+" frames)");
                    Console.WriteLine((writer.ConsumerActive?"PASS":"FAIL")+" consumer detected by the writer");
                    sending=false;
                    for(int i=0;i<60 && !black;i++) Thread.Sleep(50);
                    Console.WriteLine((black?"PASS":"FAIL")+" black frame when Pro stops writing");
                    if(!colored || !black || !writer.ConsumerActive) exit=1;
                } finally {receiver.SignalToStop();receiver.WaitForStop();}
            }
        } finally {
            MfCamera.Remove();
        }
        bool listed=false;
        Thread.Sleep(1000);
        foreach(FilterInfo f in new FilterInfoCollection(FilterCategory.VideoInputDevice)) if(f.Name.StartsWith(MfCamera.Name)) listed=true;
        Console.WriteLine((listed?"FAIL":"PASS")+" camera removed");
        return listed?1:exit;
    }
}
