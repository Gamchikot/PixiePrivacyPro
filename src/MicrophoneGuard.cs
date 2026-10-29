using System;
using System.Collections.Generic;
using System.Management;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Threading;
using Microsoft.Win32;

[ComImport,Guid("f8679f50-850a-41cf-9c72-430f290290c8"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioPolicy {
    [PreserveSig] int GetMixFormat(); [PreserveSig] int GetDeviceFormat();
    [PreserveSig] int ResetDeviceFormat(); [PreserveSig] int SetDeviceFormat();
    [PreserveSig] int GetProcessingPeriod(); [PreserveSig] int SetProcessingPeriod();
    [PreserveSig] int GetShareMode(); [PreserveSig] int SetShareMode();
    [PreserveSig] int GetPropertyValue(); [PreserveSig] int SetPropertyValue();
    [PreserveSig] int SetDefaultEndpoint();
    [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string id,int visible);
}
[ComImport,Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioEnumerator {
    [PreserveSig] int EnumAudioEndpoints(int flow,uint mask,out IAudioCollection devices);
    [PreserveSig] int GetDefaultAudioEndpoint(int flow,int role,out IAudioDevice device);
    [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id,out IAudioDevice device);
}
[ComImport,Guid("0BD7A1BE-7A1A-44DB-8397-C0A3B9A11D46"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioCollection {
    [PreserveSig] int GetCount(out uint count);[PreserveSig] int Item(uint n,out IAudioDevice device);
}
[ComImport,Guid("D666063F-1587-4E43-81F1-B948E807363F"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioDevice {
    [PreserveSig] int Activate();[PreserveSig] int OpenPropertyStore();
    [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);[PreserveSig] int GetState(out uint state);
}

public static class MicrophoneGuard {
    public const string ServiceName="PixiePrivacyProMicGuard";
    // Machine-wide choice read by the service: 1 = the user allowed the PIXY microphone in Pixie Pro, missing or 0 = disabled.
    // The service creates the key and lets local users set it, since Pro runs without elevation.
    const string PolicyKey=@"SOFTWARE\PixiePrivacyPro\Microphone";
    public static bool Allowed {get {
        try {using(var key=Registry.LocalMachine.OpenSubKey(PolicyKey)) return key!=null && Convert.ToInt32(key.GetValue("Allowed",0))==1;}
        catch {return false;}
    }}
    public static void SetAllowed(bool allowed) {
        using(var key=Registry.LocalMachine.OpenSubKey(PolicyKey,true)) {
            if(key==null) throw new Exception(L.T("служба защиты микрофона ещё не подготовила настройку; перезапустите компьютер или переустановите Pixie Privacy Pro"));
            key.SetValue("Allowed",allowed?1:0,RegistryValueKind.DWord);
        }
    }
    public static void PreparePolicy() {
        using(var key=Registry.LocalMachine.CreateSubKey(PolicyKey)) {
            var acl=key.GetAccessControl();
            acl.AddAccessRule(new System.Security.AccessControl.RegistryAccessRule(new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.BuiltinUsersSid,null),
                System.Security.AccessControl.RegistryRights.ReadKey|System.Security.AccessControl.RegistryRights.SetValue,System.Security.AccessControl.AccessControlType.Allow));
            key.SetAccessControl(acl);
        }
    }
    static void SetVisibility(string name,int visible) {
        var policy=(IAudioPolicy)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")));
        try {Marshal.ThrowExceptionForHR(policy.SetEndpointVisibility("{0.0.1.00000000}."+name,visible));}
        finally {Marshal.ReleaseComObject(policy);}
    }
    // The camera's USB audio interface. Disabled as a device (Device Manager code 22) the microphone is gone from Windows
    // entirely; the endpoint switch below covers the time before the device is disabled or after it is replugged.
    static List<KeyValuePair<string,int>> AudioInterfaces() {
        var list=new List<KeyValuePair<string,int>>();
        using(var search=new ManagementObjectSearcher(@"SELECT DeviceID,ConfigManagerErrorCode FROM Win32_PnPEntity WHERE DeviceID LIKE 'USB\\VID_328F&PID_00C0&MI_02\\%'"))
            foreach(ManagementObject device in search.Get()) using(device)
                list.Add(new KeyValuePair<string,int>(Convert.ToString(device["DeviceID"]),Convert.ToInt32(device["ConfigManagerErrorCode"])));
        return list;
    }
    static void SetDevice(string id,bool enable) {
        // The id goes into a command line run as LocalSystem: accept only the PIXY audio interface with a plain instance id.
        if(!System.Text.RegularExpressions.Regex.IsMatch(id,@"^USB\\VID_328F&PID_00C0&MI_02\\[A-Za-z0-9&_.\-]+$",System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            throw new Exception("Unexpected device id: "+id);
        var info=new System.Diagnostics.ProcessStartInfo(System.IO.Path.Combine(Environment.SystemDirectory,"pnputil.exe"),(enable?"/enable-device \"":"/disable-device \"")+id+"\"") {UseShellExecute=false,CreateNoWindow=true};
        using(var process=System.Diagnostics.Process.Start(info)) {
            if(!process.WaitForExit(30000)) {try {process.Kill();} catch {} throw new Exception(L.T("pnputil не ответил"));}
            // 3010: done, takes full effect after a restart.
            if(process.ExitCode!=0 && process.ExitCode!=3010) throw new Exception(L.T("pnputil вернул код ")+process.ExitCode+L.T(" при ")+(enable?L.T("включении"):L.T("отключении"))+L.T(" микрофона PIXY"));
        }
    }
    // A device change can leave Windows waiting for a restart; pnputil then refuses the next one, so retry rarely.
    static DateTime deviceRetry=DateTime.MinValue;
    static bool deviceRetryFor;
    public static string Inspect(bool enforce) {
        bool allowed=Allowed;
        int found=0,disabled=0,absent=0,changed=0;
        // Endpoints first: switching one works at once, while a device change may wait for a restart.
        using(var root=Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Capture")) {
            if(root!=null) foreach(string name in root.GetSubKeyNames()) using(var device=root.OpenSubKey(name)) using(var props=device.OpenSubKey("Properties")) {
                if(props==null)continue;
                string hardware=Convert.ToString(props.GetValue("{b3f8fa53-0004-438e-9003-51a46e139bfc},2"));
                if(hardware.IndexOf(@"USB\VID_328F&PID_00C0&MI_02\",StringComparison.OrdinalIgnoreCase)<0)continue;
                found++;
                uint state=0;
                var enumerator=(IAudioEnumerator)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")));
                try {IAudioDevice endpoint;Marshal.ThrowExceptionForHR(enumerator.GetDevice("{0.0.1.00000000}."+name,out endpoint));try {Marshal.ThrowExceptionForHR(endpoint.GetState(out state));}finally{Marshal.ReleaseComObject(endpoint);}}
                finally {Marshal.ReleaseComObject(enumerator);}
                if((state&2)!=0) {
                    if(enforce && allowed) {SetVisibility(name,1);changed++;} else disabled++;
                    continue;
                }
                // NOTPRESENT/UNPLUGGED endpoints cannot record; they are disabled on the next pass once they become active.
                if((state&(4|8))!=0) {absent++;continue;}
                if(enforce && !allowed) {SetVisibility(name,0);changed++;}
            }
        }
        bool working=found>disabled+absent;
        int devices=0,devicesOff=0;
        string deviceError=null;
        foreach(var device in AudioInterfaces()) {
            devices++;
            bool off=device.Value==22;
            if(off) devicesOff++;
            if(!enforce || allowed!=off) continue;
            // The device is disabled only after its endpoint is off, so a refused change never leaves the microphone on.
            if(!allowed && (changed>0||working)) continue;
            // One attempt per ten minutes for each direction, since Windows may be waiting for a restart.
            if(deviceRetryFor==allowed && DateTime.UtcNow<deviceRetry) continue;
            deviceRetryFor=allowed;deviceRetry=DateTime.UtcNow.AddMinutes(10);
            try {SetDevice(device.Key,allowed);changed++;}
            catch(Exception e) {deviceError=e.Message;}
        }
        if(changed>0) return Inspect(false);
        if(deviceError!=null) throw new Exception(deviceError);
        if(allowed) return working ? L.T("Микрофон PIXY включён в настройках Pixie Pro") :
            devicesOff>0||disabled>0 ? L.T("Микрофон PIXY разрешён, служба включает его (может понадобиться перезагрузка)") :
            found==0 ? L.T("Микрофон PIXY не подключён") : L.T("Микрофон PIXY недоступен в Windows (устройство не работает)");
        if(working) return L.T("ВНИМАНИЕ: микрофон PIXY включён");
        if(devices>0 && devicesOff==devices) return L.T("Микрофон PIXY отключён полностью");
        if(found==0) return L.T("Микрофон PIXY не подключён");
        return disabled>0 ? L.T("Микрофон PIXY отключён в Windows") : L.T("Микрофон PIXY недоступен в Windows (устройство не работает)");
    }
}

public sealed class GuardService : ServiceBase {
    System.Threading.Timer timer;
    int busy;
    // IPolicyConfig is undocumented: a Windows update can break it, so repeat errors are logged once an hour.
    string lastError; DateTime lastLogged;
    public GuardService() { ServiceName=MicrophoneGuard.ServiceName; AutoLog=true; }
    protected override void OnStart(string[] args) {
        try {MicrophoneGuard.PreparePolicy();} catch(Exception e) {Log(L.T("Не удалось подготовить настройку микрофона: ")+e.Message,System.Diagnostics.EventLogEntryType.Warning);}
        timer=new System.Threading.Timer(delegate {
            if(Interlocked.Exchange(ref busy,1)!=0) return;
            try {
                MicrophoneGuard.Inspect(true);
                if(lastError!=null) { Log(L.T("Защита микрофона снова работает."),System.Diagnostics.EventLogEntryType.Information); lastError=null; }
            }
            catch(Exception e) {
                if(e.Message!=lastError || DateTime.UtcNow-lastLogged>TimeSpan.FromHours(1)) {
                    Log(e.Message,System.Diagnostics.EventLogEntryType.Error);
                    lastError=e.Message; lastLogged=DateTime.UtcNow;
                }
            }
            finally {
                try { GuardCamera(); } catch {}
                Interlocked.Exchange(ref busy,0);
            }
        },null,0,3000);
    }
    // PIXY powers up with the lens open (boot, wake, replug). The service runs before anyone logs in,
    // so it closes the lens each time the camera appears; Pixie Pro opens it again on request.
    bool cameraPresent;
    // Presence is checked without talking to the camera: any request would restart its own privacy timer.
    void GuardCamera() {
        if(!Pixy.Present()) { cameraPresent=false; return; }
        if(cameraPresent) return;
        using(var camera=new Pixy()) {
            int mode=camera.Query();
            if(mode!=2) {
                camera.Set(2);
                Log(L.T("Камера PIXY включилась с открытым объективом (режим ")+mode+L.T("), включена приватность."),System.Diagnostics.EventLogEntryType.Information);
            }
            cameraPresent=true;
        }
    }
    void Log(string text,System.Diagnostics.EventLogEntryType type) { try { EventLog.WriteEntry(text,type); } catch {} }
    protected override void OnStop() { if(timer!=null) timer.Dispose(); }
    public static void Main(string[] args) {
        if(args.Length>0 && args[0]=="--once") { Console.WriteLine(MicrophoneGuard.Inspect(true)); return; }
        ServiceBase.Run(new GuardService());
    }
}
