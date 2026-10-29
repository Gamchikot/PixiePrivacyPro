using System;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Text;
using System.Speech.Synthesis;
using System.Threading;

// Optional sound for the eye alert: the user's own file (MP3/WAV/WMA, kept as a copy in the profile)
// or, without one, a synthesized low drone and a slow low "Я вижу тебя" from a Russian Windows voice.
public static class AlertSound {
    static byte[] drone;
    static SoundPlayer player;
    public static string Folder=Preferences.DataFolder;
    const string Alias="PixieEyeSound";
    [DllImport("winmm.dll",CharSet=CharSet.Unicode)] static extern int mciSendString(string command,StringBuilder result,int length,IntPtr callback);

    // Which sound plays: the synthesized drone with the voice, the bundled ominous voice recording, or the user's own file.
    public const int Drone=0,Voice=1,Custom=2;
    public static string VoiceFile=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"dark-voice.mp3");
    // Before there was a choice, an own file simply replaced the standard sound.
    public static int Kind {get {
        int kind=Preferences.GetInt(Preferences.AlertSoundKind,-1);
        if(kind<Drone||kind>Custom) return CustomFile!=null?Custom:Drone;
        return kind;
    }}
    public static string CustomFile {get {
        try {foreach(string file in Directory.GetFiles(Folder,"alert-sound.*")) if(!file.EndsWith(".new")) return file;} catch {}
        return null;
    }}
    public static void SetCustom(string source) {
        string extension=Path.GetExtension(source).ToLowerInvariant();
        if(extension!=".mp3" && extension!=".wav" && extension!=".wma") throw new Exception(L.T("Нужен файл MP3, WAV или WMA."));
        Directory.CreateDirectory(Folder);
        string target=Path.Combine(Folder,"alert-sound"+extension);
        File.Copy(source,target+".new",true);
        Close();
        ClearOthers(target);
        if(File.Exists(target)) File.Delete(target);
        File.Move(target+".new",target);
    }
    public static void ClearCustom() {Close();ClearOthers(null);}
    static void ClearOthers(string keep) {
        try {foreach(string file in Directory.GetFiles(Folder,"alert-sound.*")) if(file!=keep && !file.EndsWith(".new")) File.Delete(file);} catch(DirectoryNotFoundException) {}
    }
    static void Close() {mciSendString("close "+Alias,null,0,IntPtr.Zero);}

    // How long the chosen sound plays, so the eye can leave right after it. The drone with the voice is about 3 s.
    public static double Seconds() {
        int kind=Kind;
        string file=kind==Custom?CustomFile:kind==Voice&&File.Exists(VoiceFile)?VoiceFile:null;
        if(file!=null) {
            Close();
            var length=new StringBuilder(32);
            try {
                if(mciSendString("open \""+file+"\" type mpegvideo alias "+Alias,null,0,IntPtr.Zero)==0 &&
                   mciSendString("set "+Alias+" time format milliseconds",null,0,IntPtr.Zero)==0 &&
                   mciSendString("status "+Alias+" length",length,length.Capacity,IntPtr.Zero)==0) {
                    int ms;
                    if(int.TryParse(length.ToString().Trim(),out ms) && ms>0) return Math.Min(ms/1000.0,15);
                }
            } finally {Close();}
        }
        return 3;
    }
    // Stops a sound started by Play (the test button pressed a second time).
    public static void Stop() {
        Close();
        try {if(player!=null) player.Stop();} catch {}
    }
    public static void Play() {
        int kind=Kind;
        string file=kind==Custom?CustomFile:kind==Voice&&File.Exists(VoiceFile)?VoiceFile:null;
        if(file!=null) {
            // MCI plays MP3 without extra components; the alias is reopened for every alert.
            Close();
            if(mciSendString("open \""+file+"\" type mpegvideo alias "+Alias,null,0,IntPtr.Zero)==0 &&
               mciSendString("play "+Alias+" from 0",null,0,IntPtr.Zero)==0) return;
        }
        try {
            if(drone==null) drone=DroneWave();
            if(player!=null) player.Stop();
            player=new SoundPlayer(new MemoryStream(drone));
            player.Play();
        } catch {}
        // SAPI is slow to start; keep it off the UI thread.
        ThreadPool.QueueUserWorkItem(delegate {
            try {
                using(var voice=new SpeechSynthesizer()) {
                    // The voice speaks the interface language: a Russian voice for Russian, any English voice for English.
                    string culture=L.English?"en":"ru";bool found=false;
                    foreach(var installed in voice.GetInstalledVoices())
                        if(installed.Enabled && installed.VoiceInfo.Culture.TwoLetterISOLanguageName==culture) {voice.SelectVoice(installed.VoiceInfo.Name);found=true;break;}
                    if(!found) return;
                    Thread.Sleep(350);
                    voice.SetOutputToDefaultAudioDevice();
                    voice.SpeakSsml("<speak version=\"1.0\" xmlns=\"http://www.w3.org/2001/10/synthesis\" xml:lang=\""+voice.Voice.Culture.Name+"\">"+
                        "<prosody pitch=\"x-low\" rate=\"x-slow\" volume=\"loud\">"+EyeAlert.Headline+"</prosody></speak>");
                }
            } catch {}
        });
    }

    // 2.2 s, 22 kHz mono 16-bit: 55/82/110 Hz with a slow swell, a little rumble and soft fades.
    public static byte[] DroneWave() {
        const int rate=22050; int samples=(int)(rate*2.2);
        var random=new Random(7); double noise=0;
        using(var stream=new MemoryStream()) using(var w=new BinaryWriter(stream)) {
            w.Write(0x46464952);w.Write(36+samples*2);w.Write(0x45564157);
            w.Write(0x20746D66);w.Write(16);w.Write((short)1);w.Write((short)1);w.Write(rate);w.Write(rate*2);w.Write((short)2);w.Write((short)16);
            w.Write(0x61746164);w.Write(samples*2);
            for(int i=0;i<samples;i++) {
                double t=(double)i/rate;
                double envelope=Math.Min(1,t/0.35)*Math.Min(1,(2.2-t)/0.6);
                double swell=0.75+0.25*Math.Sin(2*Math.PI*0.9*t);
                noise=noise*0.995+(random.NextDouble()-0.5)*0.02;
                double v=0.5*Math.Sin(2*Math.PI*55*t)+0.3*Math.Sin(2*Math.PI*82.4*t+0.6)+0.2*Math.Sin(2*Math.PI*110*t+1.3)+noise*3;
                w.Write((short)Math.Max(-32767,Math.Min(32767,v*envelope*swell*14000)));
            }
            return stream.ToArray();
        }
    }
}
