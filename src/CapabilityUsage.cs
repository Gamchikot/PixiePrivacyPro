using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

// Windows keeps per-app camera/microphone use for its privacy indicator under ConsentStore.
// LastUsedTimeStop stays 0 while an app still holds the device. The record covers any camera,
// not only PIXY, and only devices opened through the Windows camera stack.
public static class CapabilityUsage {
    public const string StorePath=@"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore";
    public const string Camera="webcam",Microphone="microphone";

    public static List<string> Active(string capability,string ownPath) {
        using(var root=Registry.CurrentUser.OpenSubKey(StorePath+"\\"+capability)) return Active(root,ownPath);
    }

    public static List<string> Active(RegistryKey root,string ownPath) {
        var apps=new List<string>();
        if(root==null) return apps;
        foreach(string name in root.GetSubKeyNames()) {
            if(name=="NonPackaged") {
                using(var desktop=root.OpenSubKey(name)) {
                    if(desktop==null) continue;
                    foreach(string exe in desktop.GetSubKeyNames()) using(var app=desktop.OpenSubKey(exe)) {
                        if(!InUse(app)) continue;
                        // By file name: a copy from another folder, or an entry left by a killed instance, is still us.
                        if(ownPath!=null && string.Equals(System.IO.Path.GetFileName(exe.Replace('#','\\')),System.IO.Path.GetFileName(ownPath),StringComparison.OrdinalIgnoreCase)) continue;
                        Add(apps,DisplayName(exe));
                    }
                }
            } else using(var app=root.OpenSubKey(name)) if(InUse(app)) Add(apps,DisplayName(name));
        }
        apps.Sort(StringComparer.OrdinalIgnoreCase);
        return apps;
    }

    static void Add(List<string> apps,string name) {if(!apps.Contains(name)) apps.Add(name);}

    static bool InUse(RegistryKey app) {
        if(app==null) return false;
        long start=Convert.ToInt64(app.GetValue("LastUsedTimeStart",0L));
        long stop=Convert.ToInt64(app.GetValue("LastUsedTimeStop",0L));
        return start>0 && (stop==0 || stop<start);
    }

    // "C:#Program Files#App#app.exe" -> "app.exe"; "Vendor.App_8wekyb3d8bbwe" -> "Vendor.App".
    public static string DisplayName(string key) {
        if(key.IndexOf('#')>=0) return Path.GetFileName(key.Replace('#','\\'));
        int publisher=key.LastIndexOf('_');
        return publisher>0 ? key.Substring(0,publisher) : key;
    }

    public static string Describe(List<string> apps) {return apps.Count==0 ? L.T("нет") : string.Join(", ",apps.ToArray());}
}
