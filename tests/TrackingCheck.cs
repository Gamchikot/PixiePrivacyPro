using System;
using System.Threading;
class TrackingCheck {
    static void Main() {
        using(var camera=new Pixy()) {
            int initial=camera.Query();
            Console.WriteLine("Initial: "+initial);
            try {
                Console.WriteLine("Leave privacy: "+camera.Set(0));
                Console.WriteLine("Tracking requested in standby: "+camera.Set(1));
                using(var video=new VideoPipeline()) {
                    video.SetAllowed(true);video.Start();
                    try {
                        Thread.Sleep(2500);
                        int active=camera.Query();Console.WriteLine("Mode with stream: "+active);
                        if(active!=1)throw new Exception("Standby tracking setting was not retained");
                        int off=camera.Set(0);Console.WriteLine("Tracking OFF: "+off);
                        if(off!=0)throw new Exception("Normal mode not confirmed");
                        int on=camera.Set(1);Console.WriteLine("Tracking ON: "+on);
                        if(on!=1)throw new Exception("Tracking mode not confirmed");
                    } finally {video.SetAllowed(false);video.Stop();}
                }
            } finally {Console.WriteLine("Restored: "+camera.Set(initial==3?0:initial));}
        }
    }
}
