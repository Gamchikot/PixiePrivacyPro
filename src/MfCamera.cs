using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Threading;

// Windows 11 Media Foundation virtual camera (native\PixieVCam.cpp). Pro registers a session camera
// that exists while Pro runs; the Frame Server loads the media source, which reads our frames.
public static class MfCamera {
    public const string Name="Pixie Privacy Pro Camera (MF)";
    const string Dll="PixieVirtualCamera.dll";
    [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory | DllImportSearchPath.System32)]
    [DllImport(Dll,CharSet=CharSet.Unicode)] static extern int PixieVCam_Create(string friendlyName);
    [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory | DllImportSearchPath.System32)]
    [DllImport(Dll)] static extern int PixieVCam_Remove();
    const int ProcNotFound=unchecked((int)0x8007007F);

    // Environment.OSVersion reports Windows 8 to apps without a compatibility manifest.
    public static bool Supported {get {
        try {
            using(var key=Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion")) {
                int build;
                return key!=null && int.TryParse(key.GetValue("CurrentBuildNumber") as string,out build) && build>=22000;
            }
        } catch {return false;}
    }}

    // Returns null on success, otherwise a message for the user.
    public static string Create() {
        if(!Supported) return L.T("нужна Windows 11");
        try {
            int hr=PixieVCam_Create(Name);
            if(hr>=0) return null;
            if(hr==ProcNotFound) return L.T("Windows не поддерживает виртуальные камеры Media Foundation");
            return L.T("ошибка 0x")+hr.ToString("X8");
        } catch(DllNotFoundException) {return L.T("не найден ")+Dll;}
    }
    public static void Remove() {try {PixieVCam_Remove();} catch(DllNotFoundException) {}}
}

// Writes frames into "Global\PixiePrivacyProCamera", created by the media source when an app opens the camera.
// Layout (shared with PixieVCam.cpp): 64-byte header, then 1280x720 BGRA top-down.
public sealed class MfFrameWriter : IDisposable {
    const string MapName=@"Global\PixiePrivacyProCamera";
    const int Magic=0x43565850,HeaderSize=64,Width=1280,Height=720;
    const long MapSize=HeaderSize+Width*Height*4L;
    [DllImport("kernel32.dll")] static extern ulong GetTickCount64();
    MemoryMappedFile map;
    MemoryMappedViewAccessor view;
    ulong lastAttempt;

    unsafe byte* Acquire() {
        if(view==null) {
            ulong now=GetTickCount64();
            if(lastAttempt!=0 && now-lastAttempt<1000) return null;
            lastAttempt=now;
            try {
                map=MemoryMappedFile.OpenExisting(MapName,MemoryMappedFileRights.ReadWrite);
                view=map.CreateViewAccessor(0,MapSize,MemoryMappedFileAccess.ReadWrite);
            } catch(FileNotFoundException) {Close();return null;}
              catch(UnauthorizedAccessException) {Close();return null;}
              catch(IOException) {Close();return null;}
        }
        byte* pointer=null;
        view.SafeMemoryMappedViewHandle.AcquirePointer(ref pointer);
        return pointer+view.PointerOffset;
    }

    // True while an app has received a frame from the camera within the last 3 seconds.
    public unsafe bool ConsumerActive {get {
        if(view==null) return false;
        byte* p=Acquire();
        try {return p!=null && *(int*)p==Magic && GetTickCount64()-(ulong)*(long*)(p+32)<3000;}
        finally {if(p!=null) view.SafeMemoryMappedViewHandle.ReleasePointer();}
    }}

    public unsafe void Send(Bitmap image) {
        byte* p=Acquire();
        if(p==null) return;
        try {
            if(*(int*)p!=Magic) return;
            var data=image.LockBits(new Rectangle(0,0,Width,Height),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
            try {
                // Sequence is odd while copying so the reader can skip a half-written frame.
                int* sequence=(int*)(p+16);
                Interlocked.Increment(ref *sequence);
                Thread.MemoryBarrier();
                byte* target=p+HeaderSize;
                for(int y=0;y<Height;y++) Buffer.MemoryCopy((byte*)data.Scan0+y*data.Stride,target+y*Width*4,Width*4,Width*4);
                Thread.MemoryBarrier();
                *(long*)(p+24)=(long)GetTickCount64();
                Interlocked.Increment(ref *sequence);
            } finally {image.UnlockBits(data);}
        } finally {view.SafeMemoryMappedViewHandle.ReleasePointer();}
    }

    void Close() {
        if(view!=null) view.Dispose(); if(map!=null) map.Dispose();
        view=null; map=null;
    }
    public void Dispose() {Close();}
}
