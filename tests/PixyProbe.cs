using System;
using System.Threading;

// Hardware probe for the vendor settings: PixyProbe get | mode [m] | timer [s] | audio [m] | gesture [0|1]
// | feature id [0|1] | listen seconds | idle seconds (no USB traffic, then reads the mode).
class PixyProbe {
    static void Main(string[] a) {
        string cmd=a.Length>0?a[0]:"get";
        if(cmd=="idle") {
            Thread.Sleep(int.Parse(a[1])*1000);
            using(var c=new Pixy()) Console.WriteLine("mode after idle: "+c.Query());
            return;
        }
        using(var c=new Pixy()) {
            switch(cmd) {
                case "get":
                    Console.WriteLine("mode="+c.Query());
                    Try("timer",()=>c.PrivacyTimer().ToString());
                    Try("audio",()=>c.AudioMode().ToString());
                    Try("gesture",()=>c.Gestures().ToString());
                    foreach(int id in new[]{1,2,4}) Try("feature"+id,()=>c.Feature(id).ToString());
                    break;
                case "mode": Console.WriteLine(a.Length>1?c.Set(int.Parse(a[1])):c.Query()); break;
                case "timer": Console.WriteLine(a.Length>1?c.SetPrivacyTimer(int.Parse(a[1])):c.PrivacyTimer()); break;
                case "audio": Console.WriteLine(a.Length>1?c.SetAudioMode(int.Parse(a[1])):c.AudioMode()); break;
                case "gesture": Console.WriteLine(a.Length>1?c.SetGestures(a[1]=="1"):c.Gestures()); break;
                case "feature": Console.WriteLine(a.Length>2?c.SetFeature(int.Parse(a[1]),a[2]=="1"):c.Feature(int.Parse(a[1]))); break;
                case "listen":
                    var end=DateTime.UtcNow.AddSeconds(int.Parse(a[1]));
                    while(DateTime.UtcNow<end) {var r=c.Listen(500); if(r!=null) Console.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff")+" "+BitConverter.ToString(r,0,16));}
                    break;
            }
        }
    }
    static void Try(string name,Func<string> f) { try {Console.WriteLine(name+"="+f());} catch(Exception e) {Console.WriteLine(name+": "+e.Message);} }
}
