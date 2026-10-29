using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

// Which apps read the Pixie cameras. DirectShow readers load the Unity Capture filter into their own process;
// Media Foundation readers go through the Frame Server, so only Windows' camera usage record (ConsentStore) names them.
public static class PixieReaders {
    const uint SnapModule=0x8,SnapModule32=0x10;
    static readonly IntPtr Invalid=new IntPtr(-1);
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
    struct ModuleEntry {
        public uint dwSize,th32ModuleID,th32ProcessID,GlblcntUsage,ProccntUsage;
        public IntPtr modBaseAddr; public uint modBaseSize; public IntPtr hModule;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)] public string szModule;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)] public string szExePath;
    }
    [DllImport("kernel32.dll",SetLastError=true)] static extern IntPtr CreateToolhelp32Snapshot(uint flags,uint pid);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern bool Module32FirstW(IntPtr snapshot,ref ModuleEntry entry);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern bool Module32NextW(IntPtr snapshot,ref ModuleEntry entry);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    [StructLayout(LayoutKind.Sequential)]
    struct ThreadEntry {public uint dwSize,cntUsage,th32ThreadID,th32OwnerProcessID; public int tpBasePri,tpDeltaPri; public uint dwFlags;}
    [DllImport("kernel32.dll")] static extern bool Thread32First(IntPtr snapshot,ref ThreadEntry entry);
    [DllImport("kernel32.dll")] static extern bool Thread32Next(IntPtr snapshot,ref ThreadEntry entry);
    [DllImport("kernel32.dll")] static extern IntPtr OpenThread(uint access,bool inherit,uint id);
    [DllImport("ntdll.dll")] static extern int NtQueryInformationThread(IntPtr thread,int infoClass,out IntPtr info,int length,IntPtr returned);
    // Win32StartAddress needs THREAD_QUERY_INFORMATION; the limited right is refused.
    const uint SnapThread=0x4,ThreadQuery=0x40;
    const int ThreadQuerySetWin32StartAddress=9;

    // Processes of this user session that stream from UnityCaptureFilter64/32.dll, as "app.exe". Apps that only
    // list cameras load the filter too, but only a running graph has a streaming thread started inside the filter.
    public static List<string> DirectShow() {
        var apps=new List<string>();
        int self=Process.GetCurrentProcess().Id,session=Process.GetCurrentProcess().SessionId;
        Dictionary<uint,List<uint>> threads=null;
        foreach(var process in Process.GetProcesses()) using(process) {
            try {
                if(process.Id==self || process.Id<=4 || process.SessionId!=session) continue;
                long start,end;
                if(!FilterRange((uint)process.Id,out start,out end)) continue;
                if(threads==null) threads=Threads();
                List<uint> owned;
                if(threads.TryGetValue((uint)process.Id,out owned) && StartsInside(owned,start,end) && !apps.Contains(process.ProcessName+".exe")) apps.Add(process.ProcessName+".exe");
            } catch {}
        }
        apps.Sort(StringComparer.OrdinalIgnoreCase);
        return apps;
    }
    static bool FilterRange(uint pid,out long start,out long end) {
        start=end=0;
        IntPtr snapshot=CreateToolhelp32Snapshot(SnapModule|SnapModule32,pid);
        if(snapshot==Invalid) return false;
        try {
            var entry=new ModuleEntry {dwSize=(uint)Marshal.SizeOf(typeof(ModuleEntry))};
            for(bool ok=Module32FirstW(snapshot,ref entry);ok;ok=Module32NextW(snapshot,ref entry))
                if(entry.szModule!=null && entry.szModule.StartsWith("UnityCaptureFilter",StringComparison.OrdinalIgnoreCase)) {
                    start=entry.modBaseAddr.ToInt64();end=start+entry.modBaseSize;return true;
                }
            return false;
        } finally {CloseHandle(snapshot);}
    }
    static Dictionary<uint,List<uint>> Threads() {
        var threads=new Dictionary<uint,List<uint>>();
        IntPtr snapshot=CreateToolhelp32Snapshot(SnapThread,0);
        if(snapshot==Invalid) return threads;
        try {
            var entry=new ThreadEntry {dwSize=(uint)Marshal.SizeOf(typeof(ThreadEntry))};
            for(bool ok=Thread32First(snapshot,ref entry);ok;ok=Thread32Next(snapshot,ref entry)) {
                List<uint> list;
                if(!threads.TryGetValue(entry.th32OwnerProcessID,out list)) threads[entry.th32OwnerProcessID]=list=new List<uint>();
                list.Add(entry.th32ThreadID);
            }
        } finally {CloseHandle(snapshot);}
        return threads;
    }
    static bool StartsInside(List<uint> threads,long start,long end) {
        foreach(uint id in threads) {
            IntPtr thread=OpenThread(ThreadQuery,false,id);
            if(thread==IntPtr.Zero) continue;
            try {
                IntPtr address;
                if(NtQueryInformationThread(thread,ThreadQuerySetWin32StartAddress,out address,IntPtr.Size,IntPtr.Zero)==0) {
                    long value=address.ToInt64();
                    if(value>=start && value<end) return true;
                }
            } finally {CloseHandle(thread);}
        }
        return false;
    }

    // DirectShow readers plus, while the MF camera is read, the apps Windows reports as using a camera.
    public static List<string> Merge(List<string> directShow,bool mfRead,List<string> cameraApps) {
        var apps=new List<string>(directShow);
        if(mfRead) foreach(string app in cameraApps) if(!Contains(apps,app)) apps.Add(app);
        apps.Sort(StringComparer.OrdinalIgnoreCase);
        return apps;
    }
    public static bool Contains(List<string> apps,string app) {
        foreach(string item in apps) if(string.Equals(item,app,StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
    // Auto mode: an empty list trusts every app.
    public static bool AnyTrusted(List<string> trusted,IEnumerable<string> readers) {
        if(trusted.Count==0) return true;
        foreach(string app in readers) if(Contains(trusted,app)) return true;
        return false;
    }
}
