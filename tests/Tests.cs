using System;
using System.Drawing;
using System.Threading;
using AForge.Video.DirectShow;
class Tests {
    static void Require(bool condition,string text){if(!condition)throw new Exception(text);}
    static void Main() {
        var crop=new CropSettings {Shape="1:1"};crop.Fit(1280,720,.5f);crop.Move(9,-9);
        Require(Math.Abs(crop.W*1280-crop.H*720)<1,"Square ratio");Require(crop.X+crop.W<=1 && crop.Y==0,"Clamp");
        using(var input=new Bitmap(1280,720)) {using(var g=Graphics.FromImage(input)){g.Clear(Color.Red);}using(var result=CropSettings.Render(input,crop)){Require(result.GetPixel(0,360).R==0,"Letterbox outside crop");Require(result.GetPixel(640,360).R>240,"Crop content");}}
        Console.WriteLine("PASS crop aspect, boundary clamp and black borders");
        string path=null;foreach(FilterInfo f in new FilterInfoCollection(FilterCategory.VideoInputDevice)){Console.WriteLine("Camera: "+f.Name);if(f.Name=="Pixie Privacy Pro Camera")path=f.MonikerString;}
        Require(path!=null,"Virtual camera registration");
        using(var sender=new VirtualOutput()) using(var frame=new Bitmap(1280,720)) {
            using(var g=Graphics.FromImage(frame)){g.Clear(Color.Red);g.FillRectangle(Brushes.Blue,0,360,1280,360);}
            var receiver=new VideoCaptureDevice(path);foreach(var cap in receiver.VideoCapabilities)if(cap.FrameSize.Width==1280&&cap.FrameSize.Height==720){receiver.VideoResolution=cap;break;}
            int count=0; bool got=false;receiver.NewFrame+=delegate(object s,AForge.Video.NewFrameEventArgs e){Color top=e.Frame.GetPixel(e.Frame.Width/2,e.Frame.Height/4);Color bottom=e.Frame.GetPixel(e.Frame.Width/2,e.Frame.Height*3/4);if(count++%30==0)Console.WriteLine(e.Frame.Width+"x"+e.Frame.Height+" top "+top+" bottom "+bottom);if(top.R>220&&top.B<30&&bottom.B>220&&bottom.R<30)got=true;};
            receiver.Start();try {for(int i=0;i<120&&!got;i++){sender.Send(frame);Thread.Sleep(50);}Require(got,"Virtual stream pixel/color/orientation verification");
                // The filter may deliver the first frames before it signals Want; keep sending for a moment.
                for(int i=0;i<40&&!sender.ConsumerActive;i++){sender.Send(frame);Thread.Sleep(50);}
                Require(sender.ConsumerActive,"Receiver not detected through the Want event");}finally{receiver.SignalToStop();receiver.WaitForStop();}
        }
        Console.WriteLine("PASS virtual camera receives correctly oriented RGBA frames");
        using(var camera=new Pixy()) {
            int initial=camera.Query();
            try {if(initial==2)camera.Set(0);using(var video=new VideoPipeline()){video.SetAllowed(true);video.Start();Bitmap sample=null;for(int i=0;i<100&&sample==null;i++){Thread.Sleep(100);sample=video.Snapshot(false);}Require(sample!=null,"Physical capture: "+video.Error);Console.WriteLine("PASS PIXY preview "+sample.Width+"x"+sample.Height);sample.Dispose();video.SetAllowed(false);using(var black=video.Snapshot(true)){Require(black.GetPixel(640,360).ToArgb()==Color.Black.ToArgb(),"Privacy black frame");}video.Stop();}}
            finally {if(initial==2)camera.Set(2);}
        }
        Console.WriteLine(MicrophoneGuard.Inspect(false));
    }
}

