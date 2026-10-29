using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

// One HID connection shared by every caller in the process: a fresh handle drops its first request,
// and the lock keeps a query from reading another caller's reply. Dispose releases the lock, not the handle.
public sealed class Pixy : IDisposable {
    const string Dll = "hidapi.dll";
    const ushort Vendor=0x328f, Product=0x00c0;
    [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory | DllImportSearchPath.System32)]
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] static extern IntPtr hid_open(ushort vid, ushort pid, IntPtr serial);
    [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory | DllImportSearchPath.System32)]
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] static extern int hid_write(IntPtr d, byte[] b, UIntPtr n);
    [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory | DllImportSearchPath.System32)]
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] static extern int hid_read_timeout(IntPtr d, byte[] b, UIntPtr n, int ms);
    [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory | DllImportSearchPath.System32)]
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] static extern void hid_close(IntPtr d);
    [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory | DllImportSearchPath.System32)]
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] static extern IntPtr hid_error(IntPtr d);
    [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory | DllImportSearchPath.System32)]
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] static extern IntPtr hid_enumerate(ushort vid, ushort pid);
    [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory | DllImportSearchPath.System32)]
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] static extern void hid_free_enumeration(IntPtr list);

    static readonly object sync=new object();
    static IntPtr shared;
    bool released;

    public Pixy() {
        Monitor.Enter(sync);
        try { Connect(); } catch { Monitor.Exit(sync); throw; }
    }
    static void Connect() {
        if(shared!=IntPtr.Zero) return;
        shared=hid_open(Vendor,Product,IntPtr.Zero);
        if(shared==IntPtr.Zero) throw new Exception(EmeetStudioRunning()
            ? L.T("Камерой управляет EMEET Studio. Закройте EMEET Studio (и в трее тоже), чтобы Pixie Privacy Pro могла управлять камерой.")
            : L.T("Не удалось открыть EMEET PIXY. Проверьте USB-подключение."));
    }
    static void Drop() { if(shared!=IntPtr.Zero) { hid_close(shared); shared=IntPtr.Zero; } }
    // Listing devices does not talk to the camera, so it neither wakes it nor resets its privacy timer.
    public static bool Present() {
        IntPtr list=hid_enumerate(Vendor,Product);
        if(list==IntPtr.Zero) { lock(sync) Drop(); return false; }
        hid_free_enumeration(list);
        return true;
    }
    // EMEET Studio opens the HID interface exclusively.
    public static bool EmeetStudioRunning() {
        try { foreach(var p in System.Diagnostics.Process.GetProcesses()) using(p) if(p.ProcessName.IndexOf("emeet",StringComparison.OrdinalIgnoreCase)>=0) return true; } catch {}
        return false;
    }

    void Send(byte[] data) {
        byte[] packet = new byte[32];
        Array.Copy(data, packet, data.Length);
        if (hid_write(shared, packet, (UIntPtr)packet.Length) == packet.Length) return;
        // The handle dies with an unplug; a replugged camera needs a new one.
        string error=Marshal.PtrToStringUni(hid_error(shared));
        Drop(); Connect();
        if (hid_write(shared, packet, (UIntPtr)packet.Length) != packet.Length)
            throw new Exception(L.T("Ошибка USB: ") + (Marshal.PtrToStringUni(hid_error(shared)) ?? error));
    }
    void Drain() { byte[] b=new byte[64]; for(int i=0;i<20;i++) if(hid_read_timeout(shared,b,(UIntPtr)b.Length,0)<=0) break; }
    // Sends a request and returns the reply with the same group and command (the group comes back with high bits set).
    // A SET is acknowledged with 0x20 at byte 8; an unanswered request returns null.
    byte[] Exchange(params byte[] request) {
        byte[] b = new byte[64];
        for (int attempt=0; attempt<3; attempt++) {
            Drain();
            Send(request);
            for (int i=0; i<4; i++) {
                int n=hid_read_timeout(shared,b,(UIntPtr)b.Length,200);
                if (n<0) { Drop(); throw new Exception(L.T("Не удалось прочитать ответ камеры.")); }
                if (n>=10 && b[0]==9 && (b[1]&0x1f)==(request[1]&0x1f) && b[2]==request[2] && b[3]==request[3]) return (byte[])b.Clone();
            }
        }
        return null;
    }
    public int Query() {
        // 03 is standby with no video stream; tracking mode is not exposed then.
        byte[] b=Exchange(9,1,1,1);
        if (b!=null && b[8]<=3) return b[8];
        throw new Exception(L.T("Камера не ответила на запрос состояния по USB. Проверьте подключение и повторите обновление."));
    }
    public int Set(int mode) {
        if (mode<0 || mode>2) throw new ArgumentOutOfRangeException("mode");
        for (int attempt=0; attempt<2; attempt++) {
            Exchange(9,1,1,0,0,1,0,1,(byte)mode);
            // The lens takes about a second to move; the mode reads back once it settles.
            int actual=-1;
            for (int wait=0; wait<10; wait++) {
                Thread.Sleep(150);
                actual=Query();
                // Standby confirms leaving privacy, but cannot confirm tracking on/off.
                if (actual==mode || (mode!=2 && actual==3)) return actual;
            }
        }
        throw new Exception(L.T("Камера ответила, но не подтвердила переключение. Повторите через пару секунд."));
    }

    // Seconds without use before the camera closes the lens by itself (0 = never). Stored in the camera.
    public int PrivacyTimer() {
        byte[] b=Exchange(9,2,1,1);
        if (b==null) throw new Exception(L.T("Камера не сообщила таймер приватности."));
        return BitConverter.ToInt32(b,8);
    }
    public int SetPrivacyTimer(int seconds) {
        byte[] s=BitConverter.GetBytes(Math.Max(0,seconds));
        Exchange(9,2,1,0,0,4,0,4,s[0],s[1],s[2],s[3]);
        return PrivacyTimer();
    }
    // Microphone processing, stored in the camera: 1 noise cancelling, 2 live, 3 original.
    public int AudioMode() {
        byte[] b=Exchange(9,5,0,4);
        if (b==null) throw new Exception(L.T("Камера не сообщила режим микрофона."));
        return b[8];
    }
    public int SetAudioMode(int mode) {
        if (mode<1 || mode>3) throw new ArgumentOutOfRangeException("mode");
        Exchange(9,5,0,3,0,1,0,1,(byte)mode);
        return AudioMode();
    }
    public bool Gestures() {
        byte[] b=Exchange(9,4,2,1,0,1,0,1,2);
        if (b==null) throw new Exception(L.T("Камера не сообщила состояние жестов."));
        return b[9]!=0;
    }
    public bool SetGestures(bool on) {
        Exchange(9,4,2,0,0,2,0,2,2,(byte)(on?1:0));
        return Gestures();
    }
    // Image features applied by the camera to every app: 1 and 2 are the two flips, 4 auto-rotate.
    public bool Feature(int id) {
        byte[] b=Exchange(9,4,0,7,0,1,0,1,(byte)id);
        if (b==null) throw new Exception(L.T("Камера не сообщила настройку изображения."));
        return b[9]!=0;
    }
    public bool SetFeature(int id,bool on) {
        Exchange(9,4,0,8,0,2,0,2,(byte)id,(byte)(on?1:0));
        return Feature(id);
    }
    // Reports the camera sends without a request (for diagnostics).
    public byte[] Listen(int ms) {
        byte[] b=new byte[64];
        int n=hid_read_timeout(shared,b,(UIntPtr)b.Length,ms);
        if (n<0) { Drop(); throw new Exception(L.T("Не удалось прочитать ответ камеры.")); }
        return n>0 ? (byte[])b.Clone() : null;
    }

    public void Dispose() { if(!released) { released=true; Monitor.Exit(sync); } }
}
