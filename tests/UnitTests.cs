using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using Microsoft.Win32;

// Offline checks: no camera, virtual camera or service needed. Run by build.ps1 -Test.
class UnitTests {
    static int failed;
    static void Check(bool condition,string name) {
        Console.WriteLine((condition?"PASS ":"FAIL ")+name);
        if(!condition) failed++;
    }
    static int Main() {
        Crop();
        Alerts();
        Capture();
        Readers();
        Usage();
        Journal();
        Console.WriteLine(failed==0?"All offline tests passed":failed+" offline test(s) failed");
        return failed==0?0:1;
    }

    static void Crop() {
        var crop=new CropSettings {Shape="1:1"};crop.Fit(1280,720,.5f);crop.Move(9,-9);
        Check(Math.Abs(crop.W*1280-crop.H*720)<1,"square crop keeps 1:1 in pixels");
        Check(crop.X+crop.W<=1 && crop.Y==0,"crop is clamped to the frame");
        using(var input=new Bitmap(1280,720)) {
            using(var g=Graphics.FromImage(input)) g.Clear(Color.Red);
            using(var result=CropSettings.Render(input,crop)) {
                Check(result.GetPixel(0,360).R==0,"letterbox outside the crop is black");
                Check(result.GetPixel(640,360).R>240,"crop content is drawn");
            }
            using(var reused=CropSettings.Black()) {
                CropSettings.RenderInto(reused,input,new CropSettings());
                Check(reused.GetPixel(0,0).R>240,"full-frame render fills a reused bitmap");
                CropSettings.RenderInto(reused,input,crop);
                Check(reused.GetPixel(0,360).R==0,"reused bitmap is cleared between frames");
            }
        }
        using(var black=CropSettings.Black()) Check(black.GetPixel(640,360).ToArgb()==Color.Black.ToArgb(),"black frame");
        using(var input=new Bitmap(1280,720)) {
            using(var g=Graphics.FromImage(input)) {g.Clear(Color.Red);g.FillRectangle(Brushes.Blue,0,0,640,720);}
            var circle=new CropSettings {Shape=CropSettings.Circle};circle.Fit(1280,720,1f);
            Check(Math.Abs(circle.W*1280-circle.H*720)<1,"circle frame is square");
            using(var result=CropSettings.Render(input,circle)) {
                Check(result.GetPixel(640,360).ToArgb()!=Color.Black.ToArgb() && result.GetPixel(640,40).ToArgb()!=Color.Black.ToArgb(),"circle keeps the centre and edge midpoints");
                Check(result.GetPixel(300,30).ToArgb()==Color.Black.ToArgb(),"circle blacks out the corners of its square");
            }
            var mirror=new CropSettings {Mirror=true};
            using(var plain=CropSettings.Render(input,new CropSettings())) using(var flipped=CropSettings.Render(input,mirror)) {
                Check(plain.GetPixel(100,360).B>200 && flipped.GetPixel(100,360).R>200 && flipped.GetPixel(1180,360).B>200,"mirror flips left and right");
            }
        }
    }

    static void Readers() {
        var trusted=new List<string>{"obs64.exe","TikTok LIVE Studio.exe"};
        Check(PixieReaders.AnyTrusted(new List<string>(),new string[0]),"empty trusted list allows any app");
        Check(PixieReaders.AnyTrusted(trusted,new[]{"tiktok live studio.exe"}),"trusted app matches without case");
        Check(!PixieReaders.AnyTrusted(trusted,new[]{"zoom.exe"}) && !PixieReaders.AnyTrusted(trusted,new string[0]),"other or unknown readers are not trusted");
        var merged=PixieReaders.Merge(new List<string>{"obs64.exe"},true,new List<string>{"OBS64.exe","Zoom.exe"});
        Check(merged.Count==2 && merged[1]=="Zoom.exe","MF readers come from Windows camera usage, without duplicates");
        Check(PixieReaders.Merge(new List<string>{"obs64.exe"},false,new List<string>{"Zoom.exe"}).Count==1,"camera usage is ignored while the MF camera is not read");
        Check(MainWindow.Duration(TimeSpan.FromSeconds(20))=="меньше минуты" && MainWindow.Duration(TimeSpan.FromMinutes(12.5))=="12 мин" && MainWindow.Duration(TimeSpan.FromMinutes(75))=="1 ч 15 мин","open time is described");
        string savedFolder=AlertSound.Folder;
        AlertSound.Folder=Path.Combine(Path.GetTempPath(),"PixieSoundTest-"+Guid.NewGuid().ToString("N"));
        string source=Path.Combine(Path.GetTempPath(),"PixieSoundSource-"+Guid.NewGuid().ToString("N")+".mp3");
        try {
            File.WriteAllBytes(source,new byte[]{1,2,3});
            Check(AlertSound.CustomFile==null,"standard alert sound by default");
            AlertSound.SetCustom(source);
            Check(AlertSound.CustomFile!=null && AlertSound.CustomFile.EndsWith("alert-sound.mp3") && File.ReadAllBytes(AlertSound.CustomFile).Length==3,"own alert sound is copied to the profile");
            File.WriteAllBytes(Path.ChangeExtension(source,".wav"),new byte[]{4});
            AlertSound.SetCustom(Path.ChangeExtension(source,".wav"));
            Check(Directory.GetFiles(AlertSound.Folder).Length==1 && AlertSound.CustomFile.EndsWith(".wav"),"a new own sound replaces the old one");
            bool refused=false;try {AlertSound.SetCustom(Path.ChangeExtension(source,".txt"));} catch(Exception) {refused=true;}
            Check(refused,"non-audio file is refused");
            AlertSound.ClearCustom();
            Check(AlertSound.CustomFile==null,"standard alert sound is restored");
        } finally {
            File.Delete(source);File.Delete(Path.ChangeExtension(source,".wav"));
            if(Directory.Exists(AlertSound.Folder)) Directory.Delete(AlertSound.Folder,true);
            AlertSound.Folder=savedFolder;
        }
        Check(HotKeySetting.Name(HotKeySetting.DefaultModifiers,HotKeySetting.DefaultKey)=="Win+Alt+Пробел" && HotKeySetting.Name(HotKeySetting.Control|HotKeySetting.Shift,System.Windows.Forms.Keys.D5)=="Ctrl+Shift+5","hotkey names");
        Check(Array.IndexOf(HotKeySetting.KeyChoices,System.Windows.Forms.Keys.Space)==0 && Array.IndexOf(HotKeySetting.KeyChoices,System.Windows.Forms.Keys.F12)>0,"hotkey keys offered");
        byte[] drone=AlertSound.DroneWave();
        Check(drone.Length>40000 && BitConverter.ToInt32(drone,0)==0x46464952 && BitConverter.ToInt32(drone,40)==drone.Length-44,"alert sound is a valid WAV");
    }

    static void Alerts() {
        Check(MainWindow.ShouldAlert(2,3,false) && MainWindow.ShouldAlert(2,1,false),"external exit from privacy alerts");
        Check(!MainWindow.ShouldAlert(2,0,true),"own action does not alert");
        Check(!MainWindow.ShouldAlert(2,-1,false) && !MainWindow.ShouldAlert(1,1,false),"unknown or unchanged state does not alert");
        Check(MainWindow.ShouldAlertAtStartup(0,false) && MainWindow.ShouldAlertAtStartup(3,false),"open camera at startup alerts");
        Check(!MainWindow.ShouldAlertAtStartup(2,false) && !MainWindow.ShouldAlertAtStartup(-1,false),"privacy or unknown at startup does not alert");
        Check(CaptureRules.CameraPollSeconds(3,false,60)==65 && CaptureRules.CameraPollSeconds(3,false,10)==15,"idle open lens is polled less often than the camera timer");
        Check(CaptureRules.CameraPollSeconds(2,false,60)==1 && CaptureRules.CameraPollSeconds(-1,false,60)==1 && CaptureRules.CameraPollSeconds(0,false,60)==1,"privacy, unknown and streaming modes are polled every second");
        Check(CaptureRules.CameraPollSeconds(3,true,60)==1 && CaptureRules.CameraPollSeconds(3,false,0)==1,"own capture or no camera timer keeps the fast poll");
        Check(MainWindow.TimerName(0)=="никогда" && MainWindow.TimerName(10)=="через 10 с" && MainWindow.TimerName(300)=="через 5 мин" && MainWindow.TimerName(90)=="через 90 с","camera timer names");
        Check(MainWindow.CloseAtStartup(0,false,true,false,0) && MainWindow.CloseAtStartup(3,false,true,false,0),"unused open lens at startup closes");
        Check(!MainWindow.CloseAtStartup(0,false,true,true,0) && !MainWindow.CloseAtStartup(0,false,true,false,1),"lens in use at startup stays open");
        Check(!MainWindow.CloseAtStartup(0,false,false,false,0) && !MainWindow.CloseAtStartup(2,false,true,false,0) && !MainWindow.CloseAtStartup(-1,false,true,false,0),"startup close needs auto privacy and an open lens");
    }

    static void Capture() {
        // Decide(manualPreview, consumerRecent, arrived, mode, autoOpen, running)
        Check(CaptureRules.Decide(false,true,true,0,false,false)==CaptureAction.Start,"app reading an open camera starts the capture");
        Check(CaptureRules.Decide(false,true,false,3,false,false)==CaptureAction.Start,"standby camera is started for a reading app");
        Check(CaptureRules.Decide(false,true,true,2,false,false)==CaptureAction.None,"safe mode: app in privacy gets the placeholder only");
        Check(CaptureRules.Decide(false,true,true,2,true,false)==CaptureAction.OpenLens,"auto mode: arriving app opens the lens");
        Check(CaptureRules.Decide(false,true,false,2,true,false)==CaptureAction.None,"auto mode: privacy chosen while the app reads is kept");
        Check(CaptureRules.Decide(false,true,false,2,false,true)==CaptureAction.Stop,"privacy enabled elsewhere stops the capture");
        Check(CaptureRules.Decide(false,false,false,0,false,true)==CaptureAction.Stop,"no reader and no preview stops the capture");
        Check(CaptureRules.Decide(true,false,false,0,false,false)==CaptureAction.Start,"preview button starts the capture");
        Check(CaptureRules.Decide(false,true,false,0,false,true)==CaptureAction.None,"running capture for a reader is kept");
        Check(CaptureRules.Decide(false,true,true,-1,true,false)==CaptureAction.None,"unknown state never opens the lens");
        // IdleLens(autoPrivacy, mode, running, manualPreview, consumerRecent, cameraApps)
        Check(CaptureRules.IdleLens(true,3,false,false,false,0) && CaptureRules.IdleLens(true,0,false,false,false,0),"lens left open by Windows Settings is closed");
        Check(!CaptureRules.IdleLens(true,0,false,false,false,1),"lens used by another app directly stays open");
        Check(!CaptureRules.IdleLens(true,0,true,false,false,0) && !CaptureRules.IdleLens(true,0,false,true,false,0) && !CaptureRules.IdleLens(true,0,false,false,true,0),"capture, preview or a Pixie reader keeps the lens open");
        Check(!CaptureRules.IdleLens(false,0,false,false,false,0),"setting off keeps the lens open");
        Check(!CaptureRules.IdleLens(true,2,false,false,false,0) && !CaptureRules.IdleLens(true,-1,false,false,false,0),"privacy or unknown state is left alone");
        string saved=Placeholder.CustomPath;
        Placeholder.CustomPath=Path.Combine(Path.GetTempPath(),"PixiePlaceholderTest-"+Guid.NewGuid().ToString("N")+".png");
        try {
            Placeholder.ClearCustom();
            using(var a=Placeholder.Render(Placeholder.Lock.Closed)) {
                Check(a.Width==1280 && a.Height==720,"placeholder is 1280x720");
                Check(a.GetPixel(640,260).ToArgb()!=a.GetPixel(20,20).ToArgb(),"placeholder draws the lock icon");
                Check(Mirror(a),"closed lock looks the same when mirrored");
            }
            Check(Placeholder.Status(2)!=Placeholder.Status(0) && Placeholder.Status(0)!=Placeholder.Status(-1) && Placeholder.Status(0)==Placeholder.Status(1),"window shows closed, open and unknown locks, cached");
            Check(Placeholder.Privacy()==Placeholder.Status(2),"apps get only the closed lock");
            Check(!Placeholder.HasCustom,"lock is the default privacy picture");
            string source=Path.Combine(Path.GetTempPath(),"PixiePlaceholderSource-"+Guid.NewGuid().ToString("N")+".png");
            using(var tall=new Bitmap(100,400)) {using(var g=Graphics.FromImage(tall)) g.Clear(Color.Red);tall.Save(source);}
            try {
                Placeholder.SetCustom(source);
                var custom=Placeholder.Privacy();
                Check(custom.Width==1280 && custom.GetPixel(640,360).R>240 && custom.GetPixel(20,360).ToArgb()==Color.Black.ToArgb(),"custom picture is fitted without cropping");
                Check(Placeholder.Status(2)==custom && Placeholder.Status(0)!=custom,"custom picture replaces only the closed lock");
                Check(File.Exists(Placeholder.CustomPath),"custom picture is kept in the profile");
                Placeholder.ClearCustom();
                Check(!Placeholder.HasCustom && !File.Exists(Placeholder.CustomPath) && Placeholder.Privacy()!=custom,"lock is restored");
            } finally {File.Delete(source);}
        } finally {Placeholder.ClearCustom();Placeholder.CustomPath=saved;}
    }
    static bool Mirror(Bitmap a) {
        // GDI+ samples at whole pixel coordinates, so the icon drawn around x=640 mirrors x to 1280-x.
        for(int y=0;y<720;y+=8) for(int x=1;x<640;x+=8) if(Math.Abs(a.GetPixel(x,y).G-a.GetPixel(1280-x,y).G)>40) return false;
        return true;
    }

    static void Usage() {
        string path=@"Software\PixiePrivacyPro\UnitTestConsentStore";
        Registry.CurrentUser.DeleteSubKeyTree(path,false);
        try {
            using(var root=Registry.CurrentUser.CreateSubKey(path)) {
                SetTimes(root,@"Vendor.Active_8wekyb3d8bbwe",100,0);
                SetTimes(root,@"Vendor.Closed_8wekyb3d8bbwe",100,200);
                SetTimes(root,@"NonPackaged\C:#Apps#zoom.exe",300,0);
                SetTimes(root,@"NonPackaged\C:#Apps#old.exe",300,400);
                SetTimes(root,@"NonPackaged\C:#Apps#reopened.exe",500,400);
                SetTimes(root,@"NonPackaged\C:#Program Files#Pixie Privacy Pro#PixiePrivacyPro.exe",100,0);
                root.CreateSubKey("Vendor.NeverUsed_x").Dispose();
                List<string> apps=CapabilityUsage.Active(root,@"C:\Program Files\Pixie Privacy Pro\PixiePrivacyPro.exe");
                Check(string.Join("|",apps.ToArray())=="reopened.exe|Vendor.Active|zoom.exe","active apps found, closed and own app excluded: "+CapabilityUsage.Describe(apps));
            }
            Check(CapabilityUsage.Active((RegistryKey)null,null).Count==0,"missing consent store gives no apps");
            Check(CapabilityUsage.Describe(new List<string>())=="нет","empty list is described");
        } finally {Registry.CurrentUser.DeleteSubKeyTree(path,false);}
    }
    static void SetTimes(RegistryKey root,string name,long start,long stop) {
        using(var key=root.CreateSubKey(name)) {
            key.SetValue("LastUsedTimeStart",start,RegistryValueKind.QWord);
            key.SetValue("LastUsedTimeStop",stop,RegistryValueKind.QWord);
        }
    }

    static void Journal() {
        string folder=Path.Combine(Path.GetTempPath(),"PixieJournalTest-"+Guid.NewGuid().ToString("N"));
        EventJournal.FilePath=Path.Combine(folder,"events.log");
        try {
            EventJournal.Write("первое событие");
            Check(File.ReadAllText(EventJournal.FilePath).Contains("первое событие"),"journal writes UTF-8 lines");
            File.AppendAllText(EventJournal.FilePath,new string('x',1024*1024+10));
            EventJournal.Write("после ротации");
            Check(File.Exists(EventJournal.FilePath+".1") && new FileInfo(EventJournal.FilePath).Length<1000,"journal rotates after 1 MB");
            EventJournal.Enabled=false;EventJournal.Write("скрыто");EventJournal.Enabled=true;
            Check(!File.ReadAllText(EventJournal.FilePath).Contains("скрыто"),"disabled journal writes nothing");
        } finally {Directory.Delete(folder,true);}
    }
}
