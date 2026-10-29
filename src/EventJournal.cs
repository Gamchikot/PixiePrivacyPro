using System;
using System.IO;
using System.Text;

// Plain-text log of privacy transitions and camera/microphone use, kept next to crop.xml.
public static class EventJournal {
    public static string FilePath=Path.Combine(Preferences.DataFolder,"events.log");
    public static bool Enabled=true;
    const long Limit=1024*1024;
    static readonly object gate=new object();

    public static void Write(string text) {
        if(!Enabled) return;
        lock(gate) {
            try {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                var info=new FileInfo(FilePath);
                if(info.Exists && info.Length>Limit) {
                    string previous=FilePath+".1";
                    if(File.Exists(previous)) File.Delete(previous);
                    File.Move(FilePath,previous);
                }
                File.AppendAllText(FilePath,DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")+"  "+text+Environment.NewLine,Encoding.UTF8);
            } catch {}
        }
    }
}
