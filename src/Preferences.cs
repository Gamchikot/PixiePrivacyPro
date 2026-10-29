using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using System.Xml.Serialization;
using Microsoft.Win32;

// Per-user settings: HKCU\Software\PixiePrivacyPro flags, autostart and the crop frame file.
public static class Preferences {
    public const string Key=@"Software\PixiePrivacyPro";
    const string RunKey=@"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunValue="PixiePrivacyPro";
    public const string Tracking="Tracking",AutoPrivacy="AutoPrivacy",AutoOpen="AutoOpen",MfCamera="MfCamera",LockPrivacy="LockPrivacy",AlertSound="AlertSound",AlertPicture="AlertPicture",Language="Language",HotKey="HotKey",CameraTimerChosen="CameraTimerChosen";
    public const string HotKeyModifiers="HotKeyModifiers",HotKeyKey="HotKeyKey",AlertSoundKind="AlertSoundKind";
    // Theme: 0 as in Windows, 1 light, 2 dark. TrackingTrayIcon: the second tray icon, off by default.
    public const string Theme="Theme",TrackingTrayIcon="TrackingTrayIcon";
    const string TrustedValue="TrustedApps";
    public static readonly string DataFolder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PixiePrivacyPro");
    static string CropFile {get {return Path.Combine(DataFolder,"crop.xml");}}

    public static bool GetFlag(string name,bool fallback) {
        try {
            using(var key=Registry.CurrentUser.OpenSubKey(Key)) {
                object value=key==null ? null : key.GetValue(name);
                return value==null ? fallback : Convert.ToInt32(value)!=0;
            }
        } catch {return fallback;}
    }

    public static void SetFlag(string name,bool value) {
        using(var key=Registry.CurrentUser.CreateSubKey(Key)) key.SetValue(name,value?1:0);
    }
    public static int GetInt(string name,int fallback) {
        try {
            using(var key=Registry.CurrentUser.OpenSubKey(Key)) {
                object value=key==null ? null : key.GetValue(name);
                return value==null ? fallback : Convert.ToInt32(value);
            }
        } catch {return fallback;}
    }
    public static void SetInt(string name,int value) {
        using(var key=Registry.CurrentUser.CreateSubKey(Key)) key.SetValue(name,value);
    }

    // Apps allowed to open the lens in the "leave privacy" mode; empty means any app.
    public static List<string> TrustedApps() {
        var apps=new List<string>();
        try {
            using(var key=Registry.CurrentUser.OpenSubKey(Key)) {
                string value=key==null ? null : key.GetValue(TrustedValue) as string;
                if(value!=null) foreach(string app in value.Split('|')) if(app.Trim().Length>0 && !apps.Contains(app.Trim())) apps.Add(app.Trim());
            }
        } catch {}
        return apps;
    }
    public static void SetTrustedApps(List<string> apps) {
        using(var key=Registry.CurrentUser.CreateSubKey(Key)) key.SetValue(TrustedValue,string.Join("|",apps.ToArray()));
    }

    public static string StartupCommand {get {return "\""+Application.ExecutablePath+"\" --tray";}}

    public static bool Autostart {
        get {
            using(var key=Registry.CurrentUser.OpenSubKey(RunKey))
                return key!=null && string.Equals(key.GetValue(RunValue) as string,StartupCommand,StringComparison.OrdinalIgnoreCase);
        }
        set {
            using(var key=Registry.CurrentUser.CreateSubKey(RunKey)) {
                if(value) key.SetValue(RunValue,StartupCommand);
                else key.DeleteValue(RunValue,false);
            }
        }
    }

    // Called by the uninstaller: the per-machine MSI cannot remove the per-user Run value itself.
    public static void RemoveAutostart() {
        using(var key=Registry.CurrentUser.OpenSubKey(RunKey,true)) if(key!=null) key.DeleteValue(RunValue,false);
    }

    public static CropSettings LoadCrop() {
        try {
            CropSettings crop;
            using(var stream=File.OpenRead(CropFile)) crop=(CropSettings)new XmlSerializer(typeof(CropSettings)).Deserialize(stream);
            if(float.IsNaN(crop.W)||float.IsNaN(crop.H)||crop.W<=0||crop.H<=0||crop.W>1||crop.H>1||float.IsNaN(crop.X)||float.IsNaN(crop.Y)) return new CropSettings();
            crop.Move(crop.X,crop.Y);
            return crop;
        } catch {return new CropSettings();}
    }

    public static void SaveCrop(CropSettings crop) {
        Directory.CreateDirectory(DataFolder);
        using(var stream=File.Create(CropFile)) new XmlSerializer(typeof(CropSettings)).Serialize(stream,crop);
    }
}
