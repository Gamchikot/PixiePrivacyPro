using System;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
class TrackingUiCheck {
    static object Field(MainWindow w,string name){return typeof(MainWindow).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(w);}
    static Task Call(MainWindow w,string name,params object[] args){return (Task)typeof(MainWindow).GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(w,args);}
    static void Require(bool value,string message){if(!value)throw new Exception(message);}
    [STAThread] static void Main() {
        Application.EnableVisualStyles();
        object saved=null;
        using(var key=Registry.CurrentUser.OpenSubKey(@"Software\PixiePrivacyPro"))if(key!=null)saved=key.GetValue("Tracking");
        int initial;using(var camera=new Pixy())initial=camera.Query();
        int exit=0;
        using(var window=new MainWindow(false,true)) {
            var handle=window.Handle;
            var timer=new Timer {Interval=50};
            timer.Tick+=async delegate {
                timer.Stop();
                try {
                    await Call(window,"UpdateCamera",false);
                    Require((int)Field(window,"mode")==2,"Test expects initial privacy");
                    var button=(Button)Field(window,"tracking");
                    Require(!button.Enabled,"Tracking must be disabled in privacy");
                    await Call(window,"ToggleTracking");
                    using(var camera=new Pixy())Require(camera.Query()==2,"Tracking click opened privacy");
                    Console.WriteLine("PASS privacy blocks tracking");
                    await Call(window,"UpdateCamera",true);
                    await Call(window,"TogglePreview");
                    await Task.Delay(2000);
                    await Call(window,"UpdateCamera",false);
                    if((int)Field(window,"mode")==0)await Call(window,"ToggleTracking");
                    Require((int)Field(window,"mode")==1,"Tracking not confirmed ON");
                    int changes=0;button.EnabledChanged+=delegate {changes++;};
                    await Call(window,"UpdateCamera",false);
                    await Call(window,"UpdateCamera",false);
                    Require(changes==0,"Polling flickers tracking button");
                    Console.WriteLine("PASS tracking ON and steady controls during polling");
                    await Call(window,"UpdateCamera",true);
                    Require((int)Field(window,"mode")==2,"Privacy not enabled");
                    Require((bool)Field(window,"trackingWanted"),"Preference lost in privacy");
                    // Leaving privacy must bring the video back without pressing the preview button again.
                    await Call(window,"UpdateCamera",true);
                    await Task.Delay(2000);
                    await Call(window,"UpdateCamera",false);
                    Require(((VideoPipeline)Field(window,"video")).Running,"Capture not restarted after privacy");
                    Require((int)Field(window,"mode")==1,"Tracking not restored after privacy");
                    await Call(window,"ToggleTracking");
                    Require((int)Field(window,"mode")==0,"Tracking not confirmed OFF");
                    using(var key=Registry.CurrentUser.OpenSubKey(@"Software\PixiePrivacyPro"))
                        Require(Convert.ToInt32(key.GetValue("Tracking"))==0,"Preference not saved");
                    Console.WriteLine("PASS privacy restoration, tracking OFF and persistence");
                } catch(Exception e) {Console.WriteLine(e);exit=1;}
                finally {Application.ExitThread();}
            };
            timer.Start();Application.Run();timer.Dispose();
        }
        using(var camera=new Pixy())Console.WriteLine("Restored camera: "+camera.Set(initial==3?0:initial));
        using(var key=Registry.CurrentUser.CreateSubKey(@"Software\PixiePrivacyPro")) {
            if(saved==null)key.DeleteValue("Tracking",false);else key.SetValue("Tracking",saved);
        }
        Environment.ExitCode=exit;
    }
}
