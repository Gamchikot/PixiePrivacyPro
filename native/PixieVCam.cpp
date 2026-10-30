// Pixie Privacy Pro virtual camera for Media Foundation (Windows 11, MFCreateVirtualCamera).
// The same DLL is the COM media source that the Windows Camera Frame Server loads and the helper
// PixiePrivacyPro.exe calls to register a session camera. Frames come from Pro through the shared
// memory "Global\PixiePrivacyProCamera" (BGRA, top-down); stale or missing frames become black.
#define WIN32_LEAN_AND_MEAN
#define _WIN32_WINNT 0x0A00
#define WINVER 0x0A00
#include <windows.h>
#include <initguid.h>
#include <cguid.h>
#include <mfapi.h>
#include <mfidl.h>
#include <mferror.h>
#include <sddl.h>
#include <shlobj.h>
#include <new>
#include <cstdio>
#include <cstdarg>
#include <cstring>

// ---- GUIDs missing from the mingw headers (values from the Windows SDK headers) ----
// {6D08C5CA-F5DE-4D43-BA53-B603B78E6DCD}
DEFINE_GUID(CLSID_PixieVirtualCamera, 0x6d08c5ca, 0xf5de, 0x4d43, 0xba, 0x53, 0xb6, 0x03, 0xb7, 0x8e, 0x6d, 0xcd);
static const wchar_t* const ClsidString = L"{6D08C5CA-F5DE-4D43-BA53-B603B78E6DCD}";
DEFINE_GUID(IID_IMFVirtualCameraPixie, 0x1C08A864, 0xEF6C, 0x4C75, 0xAF, 0x59, 0x5F, 0x2D, 0x68, 0xDA, 0x95, 0x63);
DEFINE_GUID(IID_IMFMediaStream2Pixie, 0xC5BC37D6, 0x75C7, 0x46A1, 0xA1, 0x32, 0x81, 0xB5, 0xF7, 0x23, 0xC2, 0x0F);
DEFINE_GUID(IID_IKsControlPixie, 0x28F54685, 0x06FD, 0x11D2, 0xB2, 0x7A, 0x00, 0xA0, 0xC9, 0x22, 0x31, 0x96);
DEFINE_GUID(MF_DEVICESTREAM_STREAM_CATEGORY_PIXIE, 0x2939e7b8, 0xa62e, 0x4579, 0xb6, 0x74, 0xd4, 0x07, 0x3d, 0xfa, 0xbb, 0xba);
DEFINE_GUID(MF_DEVICESTREAM_STREAM_ID_PIXIE, 0x11bd5120, 0xd124, 0x446b, 0x88, 0xe6, 0x17, 0x06, 0x02, 0x57, 0xff, 0xf9);
DEFINE_GUID(MF_DEVICESTREAM_FRAMESERVER_SHARED_PIXIE, 0x1CB378E9, 0xB279, 0x41D4, 0xAF, 0x97, 0x34, 0xA2, 0x43, 0xE6, 0x83, 0x20);
DEFINE_GUID(MF_DEVICESTREAM_ATTRIBUTE_FRAMESOURCE_TYPES_PIXIE, 0x17145fd1, 0x1b2b, 0x423c, 0x80, 0x01, 0x2b, 0x68, 0x33, 0xed, 0x35, 0x88);
DEFINE_GUID(PINNAME_VIDEO_CAPTURE_PIXIE, 0xfb6c4281, 0x0353, 0x11d1, 0x90, 0x5f, 0x00, 0x00, 0xc0, 0xcc, 0x16, 0xba);
static const UINT32 FrameSourceTypesColor = 0x0001;
enum { StreamStopped = 0, StreamPaused = 1, StreamRunning = 2 }; // MF_STREAM_STATE

struct IMFMediaStream2Pixie : public IMFMediaStream {
    virtual HRESULT STDMETHODCALLTYPE SetStreamState(int value) = 0;
    virtual HRESULT STDMETHODCALLTYPE GetStreamState(int* value) = 0;
};
struct IKsControlPixie : public IUnknown {
    virtual HRESULT STDMETHODCALLTYPE KsProperty(void* property, ULONG propertyLength, void* data, ULONG dataLength, ULONG* returned) = 0;
    virtual HRESULT STDMETHODCALLTYPE KsMethod(void* method, ULONG methodLength, void* data, ULONG dataLength, ULONG* returned) = 0;
    virtual HRESULT STDMETHODCALLTYPE KsEvent(void* event, ULONG eventLength, void* data, ULONG dataLength, ULONG* returned) = 0;
};
struct IMFVirtualCameraPixie : public IMFAttributes {
    virtual HRESULT STDMETHODCALLTYPE AddDeviceSourceInfo(LPCWSTR info) = 0;
    virtual HRESULT STDMETHODCALLTYPE AddProperty(const void* key, ULONG type, const BYTE* data, ULONG size) = 0;
    virtual HRESULT STDMETHODCALLTYPE AddRegistryEntry(LPCWSTR name, LPCWSTR subkey, DWORD type, const BYTE* data, ULONG size) = 0;
    virtual HRESULT STDMETHODCALLTYPE Start(IMFAsyncCallback* callback) = 0;
    virtual HRESULT STDMETHODCALLTYPE Stop() = 0;
    virtual HRESULT STDMETHODCALLTYPE Remove() = 0;
    virtual HRESULT STDMETHODCALLTYPE GetMediaSource(IMFMediaSource** source) = 0;
    virtual HRESULT STDMETHODCALLTYPE SendCameraProperty(REFGUID set, ULONG id, ULONG flags, void* payload, ULONG payloadLength, void* data, ULONG dataLength, ULONG* written) = 0;
    virtual HRESULT STDMETHODCALLTYPE CreateSyncEvent(REFGUID set, ULONG id, ULONG flags, HANDLE handle, IUnknown** sync) = 0;
    virtual HRESULT STDMETHODCALLTYPE CreateSyncSemaphore(REFGUID set, ULONG id, ULONG flags, HANDLE handle, LONG adjustment, IUnknown** sync) = 0;
    virtual HRESULT STDMETHODCALLTYPE Shutdown() = 0;
};
typedef HRESULT (WINAPI* CreateVirtualCameraFn)(int type, int lifetime, int access, LPCWSTR friendlyName, LPCWSTR sourceId,
    const GUID* categories, ULONG categoryCount, IMFVirtualCameraPixie** camera);

// ---- Small helpers ----
static HMODULE g_module;
static volatile LONG g_objects, g_locks;
static const UINT32 Width = 1280, Height = 720;
static const LONGLONG FrameDuration = 333333; // 30 fps in 100 ns units

template<class T> class Ptr {
    T* p = nullptr;
public:
    Ptr() {}
    Ptr(const Ptr& o) : p(o.p) { if (p) p->AddRef(); }
    ~Ptr() { Reset(); }
    Ptr& operator=(const Ptr& o) { if (o.p) o.p->AddRef(); Reset(); p = o.p; return *this; }
    void Reset() { if (p) { T* t = p; p = nullptr; t->Release(); } }
    void Attach(T* t) { Reset(); p = t; }
    T* Get() const { return p; }
    T** Out() { Reset(); return &p; }
    T* operator->() const { return p; }
    explicit operator bool() const { return p != nullptr; }
};

static void Log(const char* format, ...) {
    char text[600];
    SYSTEMTIME now; GetLocalTime(&now);
    int used = snprintf(text, sizeof(text), "%04u-%02u-%02u %02u:%02u:%02u.%03u [%lu] ", now.wYear, now.wMonth, now.wDay,
        now.wHour, now.wMinute, now.wSecond, now.wMilliseconds, GetCurrentProcessId());
    va_list args; va_start(args, format);
    vsnprintf(text + used, sizeof(text) - used - 2, format, args);
    va_end(args);
    strcat(text, "\r\n");
    OutputDebugStringA(text);
    // The temp folder of the hosting account (LocalService for the Frame Server): no other user can redirect it.
    wchar_t path[MAX_PATH + 32];
    DWORD length = GetTempPathW(MAX_PATH, path);
    if (length == 0 || length > MAX_PATH) return;
    wcscat(path, L"PixieVirtualCamera.log");
    HANDLE file = CreateFileW(path, FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, OPEN_ALWAYS, 0, nullptr);
    if (file == INVALID_HANDLE_VALUE) return;
    LARGE_INTEGER size;
    if (GetFileSizeEx(file, &size) && size.QuadPart < 1024 * 1024) { DWORD written; WriteFile(file, text, (DWORD)strlen(text), &written, nullptr); }
    CloseHandle(file);
}

class Lock {
    SRWLOCK* lock;
public:
    explicit Lock(SRWLOCK& l) : lock(&l) { AcquireSRWLockExclusive(lock); }
    ~Lock() { ReleaseSRWLockExclusive(lock); }
};

// ---- Shared frame written by PixiePrivacyPro.exe ----
#pragma pack(push, 1)
struct FrameHeader {
    UINT32 magic;            // 'PXVC'
    UINT32 version;          // 1
    UINT32 width, height;    // 1280 x 720, BGRA top-down
    volatile LONG sequence;  // odd while the writer copies a frame
    UINT32 reserved;
    volatile LONGLONG writtenTicks; // GetTickCount64 of the last written frame
    volatile LONGLONG readTicks;    // GetTickCount64 of the last frame delivered to an app
    BYTE pad[24];
};
#pragma pack(pop)
static_assert(sizeof(FrameHeader) == 64, "header layout is shared with Video.cs");
static const UINT32 FrameMagic = 0x43565850, FrameBytes = Width * Height * 4;
static const DWORD MapSize = sizeof(FrameHeader) + FrameBytes;
static const ULONGLONG StaleAfterMs = 1000;

class FrameReader {
    HANDLE map = nullptr;
    FrameHeader* view = nullptr;
    ULONGLONG lastAttempt = 0;
    bool Open() {
        ULONGLONG now = GetTickCount64();
        if (lastAttempt && now - lastAttempt < 1000) return false;
        lastAttempt = now;
        // The Frame Server runs as a service and may create Global\ objects; the user's Pro opens them.
        // Services and administrators get full access; signed-in users only read and write frames (no WRITE_DAC).
        PSECURITY_DESCRIPTOR sd = nullptr;
        SECURITY_ATTRIBUTES sa = { sizeof(sa), nullptr, FALSE };
        if (ConvertStringSecurityDescriptorToSecurityDescriptorW(L"D:(A;;GA;;;SY)(A;;GA;;;LS)(A;;GA;;;BA)(A;;GRGW;;;IU)(A;;GRGW;;;AU)",
                SDDL_REVISION_1, &sd, nullptr)) sa.lpSecurityDescriptor = sd;
        map = CreateFileMappingW(INVALID_HANDLE_VALUE, sd ? &sa : nullptr, PAGE_READWRITE, 0, MapSize, L"Global\\PixiePrivacyProCamera");
        DWORD error = GetLastError();
        if (sd) LocalFree(sd);
        if (!map) map = OpenFileMappingW(FILE_MAP_READ | FILE_MAP_WRITE, FALSE, L"Global\\PixiePrivacyProCamera");
        if (!map) { Log("frame mapping unavailable, error %lu", error); return false; }
        view = (FrameHeader*)MapViewOfFile(map, FILE_MAP_READ | FILE_MAP_WRITE, 0, 0, MapSize);
        if (!view) { Log("MapViewOfFile failed, error %lu", GetLastError()); CloseHandle(map); map = nullptr; return false; }
        if (view->magic != FrameMagic) {
            view->width = Width; view->height = Height; view->version = 1; view->writtenTicks = 0;
            MemoryBarrier(); view->magic = FrameMagic;
        }
        Log("frame mapping ready (%s)", error == ERROR_ALREADY_EXISTS ? "existing" : "created");
        return true;
    }
public:
    ~FrameReader() { if (view) UnmapViewOfFile(view); if (map) CloseHandle(map); }
    // Copies the latest frame; false means black should be sent (no writer or stale frame).
    bool Read(BYTE* bgra) {
        if (!view && !Open()) return false;
        ULONGLONG now = GetTickCount64();
        view->readTicks = (LONGLONG)now;
        if (view->magic != FrameMagic || view->width != Width || view->height != Height) return false;
        if (!view->writtenTicks || now - (ULONGLONG)view->writtenTicks > StaleAfterMs) return false;
        const BYTE* pixels = (const BYTE*)(view + 1);
        for (int attempt = 0; attempt < 3; attempt++) {
            LONG before = view->sequence;
            if (before & 1) { Sleep(1); continue; }
            MemoryBarrier();
            memcpy(bgra, pixels, FrameBytes);
            MemoryBarrier();
            if (view->sequence == before) return true;
        }
        return true; // A torn frame is still better than a stall; it is replaced 33 ms later.
    }
};

// ---- Pixel conversion ----
static inline BYTE Clamp(int v) { return (BYTE)(v < 0 ? 0 : v > 255 ? 255 : v); }
static void BgraToNv12(const BYTE* src, BYTE* y, LONG yPitch, BYTE* uv, LONG uvPitch) {
    for (UINT32 row = 0; row < Height; row += 2) {
        const BYTE* s0 = src + row * Width * 4;
        const BYTE* s1 = s0 + Width * 4;
        BYTE* y0 = y + row * yPitch;
        BYTE* y1 = y0 + yPitch;
        BYTE* c = uv + (row / 2) * uvPitch;
        for (UINT32 col = 0; col < Width; col += 2) {
            int sumR = 0, sumG = 0, sumB = 0;
            const BYTE* px[4] = { s0 + col * 4, s0 + col * 4 + 4, s1 + col * 4, s1 + col * 4 + 4 };
            BYTE* out[4] = { y0 + col, y0 + col + 1, y1 + col, y1 + col + 1 };
            for (int i = 0; i < 4; i++) {
                int b = px[i][0], g = px[i][1], r = px[i][2];
                *out[i] = (BYTE)(((66 * r + 129 * g + 25 * b + 128) >> 8) + 16); // BT.601 limited range
                sumR += r; sumG += g; sumB += b;
            }
            int r = sumR / 4, g = sumG / 4, b = sumB / 4;
            c[col] = Clamp(((-38 * r - 74 * g + 112 * b + 128) >> 8) + 128);
            c[col + 1] = Clamp(((112 * r - 94 * g - 18 * b + 128) >> 8) + 128);
        }
    }
}

// ---- IMFAttributes forwarding ----
#define FORWARD_ATTRIBUTES(store) \
    STDMETHODIMP GetItem(REFGUID k, PROPVARIANT* v) override { return store->GetItem(k, v); } \
    STDMETHODIMP GetItemType(REFGUID k, MF_ATTRIBUTE_TYPE* t) override { return store->GetItemType(k, t); } \
    STDMETHODIMP CompareItem(REFGUID k, REFPROPVARIANT v, WINBOOL* r) override { return store->CompareItem(k, v, r); } \
    STDMETHODIMP Compare(IMFAttributes* a, MF_ATTRIBUTES_MATCH_TYPE m, WINBOOL* r) override { return store->Compare(a, m, r); } \
    STDMETHODIMP GetUINT32(REFGUID k, UINT32* v) override { return store->GetUINT32(k, v); } \
    STDMETHODIMP GetUINT64(REFGUID k, UINT64* v) override { return store->GetUINT64(k, v); } \
    STDMETHODIMP GetDouble(REFGUID k, double* v) override { return store->GetDouble(k, v); } \
    STDMETHODIMP GetGUID(REFGUID k, GUID* v) override { return store->GetGUID(k, v); } \
    STDMETHODIMP GetStringLength(REFGUID k, UINT32* n) override { return store->GetStringLength(k, n); } \
    STDMETHODIMP GetString(REFGUID k, LPWSTR s, UINT32 n, UINT32* l) override { return store->GetString(k, s, n, l); } \
    STDMETHODIMP GetAllocatedString(REFGUID k, LPWSTR* s, UINT32* l) override { return store->GetAllocatedString(k, s, l); } \
    STDMETHODIMP GetBlobSize(REFGUID k, UINT32* n) override { return store->GetBlobSize(k, n); } \
    STDMETHODIMP GetBlob(REFGUID k, UINT8* b, UINT32 n, UINT32* l) override { return store->GetBlob(k, b, n, l); } \
    STDMETHODIMP GetAllocatedBlob(REFGUID k, UINT8** b, UINT32* n) override { return store->GetAllocatedBlob(k, b, n); } \
    STDMETHODIMP GetUnknown(REFGUID k, REFIID i, LPVOID* p) override { return store->GetUnknown(k, i, p); } \
    STDMETHODIMP SetItem(REFGUID k, REFPROPVARIANT v) override { return store->SetItem(k, v); } \
    STDMETHODIMP DeleteItem(REFGUID k) override { return store->DeleteItem(k); } \
    STDMETHODIMP DeleteAllItems() override { return store->DeleteAllItems(); } \
    STDMETHODIMP SetUINT32(REFGUID k, UINT32 v) override { return store->SetUINT32(k, v); } \
    STDMETHODIMP SetUINT64(REFGUID k, UINT64 v) override { return store->SetUINT64(k, v); } \
    STDMETHODIMP SetDouble(REFGUID k, double v) override { return store->SetDouble(k, v); } \
    STDMETHODIMP SetGUID(REFGUID k, REFGUID v) override { return store->SetGUID(k, v); } \
    STDMETHODIMP SetString(REFGUID k, LPCWSTR v) override { return store->SetString(k, v); } \
    STDMETHODIMP SetBlob(REFGUID k, const UINT8* b, UINT32 n) override { return store->SetBlob(k, b, n); } \
    STDMETHODIMP SetUnknown(REFGUID k, IUnknown* u) override { return store->SetUnknown(k, u); } \
    STDMETHODIMP LockStore() override { return store->LockStore(); } \
    STDMETHODIMP UnlockStore() override { return store->UnlockStore(); } \
    STDMETHODIMP GetCount(UINT32* n) override { return store->GetCount(n); } \
    STDMETHODIMP GetItemByIndex(UINT32 i, GUID* k, PROPVARIANT* v) override { return store->GetItemByIndex(i, k, v); } \
    STDMETHODIMP CopyAllItems(IMFAttributes* d) override { return store->CopyAllItems(d); }

#define IMPLEMENT_REFCOUNT \
    STDMETHODIMP_(ULONG) AddRef() override { return InterlockedIncrement(&refs); } \
    STDMETHODIMP_(ULONG) Release() override { LONG n = InterlockedDecrement(&refs); if (!n) delete this; return n; }

static HRESULT MakeMediaType(const GUID& subtype, UINT32 stride, UINT32 sampleSize, IMFMediaType** result) {
    Ptr<IMFMediaType> type;
    HRESULT hr = MFCreateMediaType(type.Out());
    if (SUCCEEDED(hr)) hr = type->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video);
    if (SUCCEEDED(hr)) hr = type->SetGUID(MF_MT_SUBTYPE, subtype);
    if (SUCCEEDED(hr)) hr = MFSetAttributeSize(type.Get(), MF_MT_FRAME_SIZE, Width, Height);
    if (SUCCEEDED(hr)) hr = type->SetUINT32(MF_MT_DEFAULT_STRIDE, stride);
    if (SUCCEEDED(hr)) hr = type->SetUINT32(MF_MT_INTERLACE_MODE, MFVideoInterlace_Progressive);
    if (SUCCEEDED(hr)) hr = type->SetUINT32(MF_MT_ALL_SAMPLES_INDEPENDENT, TRUE);
    if (SUCCEEDED(hr)) hr = type->SetUINT32(MF_MT_FIXED_SIZE_SAMPLES, TRUE);
    if (SUCCEEDED(hr)) hr = type->SetUINT32(MF_MT_SAMPLE_SIZE, sampleSize);
    if (SUCCEEDED(hr)) hr = MFSetAttributeRatio(type.Get(), MF_MT_FRAME_RATE, 30, 1);
    if (SUCCEEDED(hr)) hr = MFSetAttributeRatio(type.Get(), MF_MT_PIXEL_ASPECT_RATIO, 1, 1);
    if (SUCCEEDED(hr)) hr = type->SetUINT32(MF_MT_AVG_BITRATE, sampleSize * 8 * 30);
    if (SUCCEEDED(hr)) { *result = type.Get(); type->AddRef(); }
    return hr;
}

static HRESULT SetStreamAttributes(IMFAttributes* attributes) {
    HRESULT hr = attributes->SetGUID(MF_DEVICESTREAM_STREAM_CATEGORY_PIXIE, PINNAME_VIDEO_CAPTURE_PIXIE);
    if (SUCCEEDED(hr)) hr = attributes->SetUINT32(MF_DEVICESTREAM_STREAM_ID_PIXIE, 0);
    if (SUCCEEDED(hr)) hr = attributes->SetUINT32(MF_DEVICESTREAM_FRAMESERVER_SHARED_PIXIE, 1);
    if (SUCCEEDED(hr)) hr = attributes->SetUINT32(MF_DEVICESTREAM_ATTRIBUTE_FRAMESOURCE_TYPES_PIXIE, FrameSourceTypesColor);
    return hr;
}

class Source;

// ---- Stream ----
class Stream : public IMFMediaStream2Pixie, public IKsControlPixie {
    volatile LONG refs = 1;
    SRWLOCK lock = SRWLOCK_INIT;
    Ptr<IMFMediaEventQueue> queue;
    Ptr<IMFStreamDescriptor> descriptor;
    Ptr<IMFAttributes> attributes;
    Ptr<IMFMediaSource> source;
    FrameReader* reader;
    int state = StreamStopped;
    GUID format = MFVideoFormat_NV12;
    LONGLONG nextFrame = 0;
    BYTE* frame = nullptr;
    ~Stream() { delete[] frame; InterlockedDecrement(&g_objects); }
public:
    Stream(IMFMediaSource* owner, FrameReader* frames) : reader(frames) { source.Attach(owner); owner->AddRef(); InterlockedIncrement(&g_objects); }
    HRESULT Initialize() {
        frame = new (std::nothrow) BYTE[FrameBytes];
        if (!frame) return E_OUTOFMEMORY;
        HRESULT hr = MFCreateEventQueue(queue.Out());
        if (SUCCEEDED(hr)) hr = MFCreateAttributes(attributes.Out(), 4);
        if (SUCCEEDED(hr)) hr = SetStreamAttributes(attributes.Get());
        Ptr<IMFMediaType> nv12, rgb32;
        if (SUCCEEDED(hr)) hr = MakeMediaType(MFVideoFormat_NV12, Width, Width * Height * 3 / 2, nv12.Out());
        if (SUCCEEDED(hr)) hr = MakeMediaType(MFVideoFormat_RGB32, Width * 4, FrameBytes, rgb32.Out());
        IMFMediaType* types[2] = { nv12.Get(), rgb32.Get() };
        if (SUCCEEDED(hr)) hr = MFCreateStreamDescriptor(0, 2, types, descriptor.Out());
        if (SUCCEEDED(hr)) hr = SetStreamAttributes(descriptor.Get());
        Ptr<IMFMediaTypeHandler> handler;
        if (SUCCEEDED(hr)) hr = descriptor->GetMediaTypeHandler(handler.Out());
        if (SUCCEEDED(hr)) hr = handler->SetCurrentMediaType(nv12.Get());
        return hr;
    }
    IMFStreamDescriptor* Descriptor() { return descriptor.Get(); }
    IMFAttributes* Attributes() { return attributes.Get(); }
    bool Running() { Lock l(lock); return state == StreamRunning; }
    HRESULT Start(IMFMediaType* type) {
        Lock l(lock);
        if (!queue) return MF_E_SHUTDOWN;
        if (type) { GUID subtype; if (SUCCEEDED(type->GetGUID(MF_MT_SUBTYPE, &subtype))) format = subtype; }
        state = StreamRunning; nextFrame = 0;
        Log("stream started (%s)", format == MFVideoFormat_RGB32 ? "RGB32" : "NV12");
        return queue->QueueEventParamVar(MEStreamStarted, GUID_NULL, S_OK, nullptr);
    }
    HRESULT Stop() {
        Lock l(lock);
        if (!queue) return MF_E_SHUTDOWN;
        state = StreamStopped;
        Log("stream stopped");
        return queue->QueueEventParamVar(MEStreamStopped, GUID_NULL, S_OK, nullptr);
    }
    void Shutdown() {
        Ptr<IMFMediaEventQueue> q;
        { Lock l(lock); q = queue; queue.Reset(); state = StreamStopped; source.Reset(); }
        if (q) q->Shutdown();
    }

    STDMETHODIMP QueryInterface(REFIID iid, void** ppv) override {
        if (!ppv) return E_POINTER;
        if (iid == IID_IUnknown || iid == IID_IMFMediaEventGenerator || iid == IID_IMFMediaStream || iid == IID_IMFMediaStream2Pixie)
            *ppv = static_cast<IMFMediaStream2Pixie*>(this);
        else if (iid == IID_IKsControlPixie) *ppv = static_cast<IKsControlPixie*>(this);
        else { *ppv = nullptr; return E_NOINTERFACE; }
        AddRef(); return S_OK;
    }
    IMPLEMENT_REFCOUNT

    // IMFMediaEventGenerator
    Ptr<IMFMediaEventQueue> Queue() { Lock l(lock); return queue; }
    STDMETHODIMP GetEvent(DWORD flags, IMFMediaEvent** e) override { auto q = Queue(); return q ? q->GetEvent(flags, e) : MF_E_SHUTDOWN; }
    STDMETHODIMP BeginGetEvent(IMFAsyncCallback* c, IUnknown* s) override { auto q = Queue(); return q ? q->BeginGetEvent(c, s) : MF_E_SHUTDOWN; }
    STDMETHODIMP EndGetEvent(IMFAsyncResult* r, IMFMediaEvent** e) override { auto q = Queue(); return q ? q->EndGetEvent(r, e) : MF_E_SHUTDOWN; }
    STDMETHODIMP QueueEvent(MediaEventType t, REFGUID g, HRESULT h, const PROPVARIANT* v) override { auto q = Queue(); return q ? q->QueueEventParamVar(t, g, h, v) : MF_E_SHUTDOWN; }

    // IMFMediaStream
    STDMETHODIMP GetMediaSource(IMFMediaSource** result) override {
        if (!result) return E_POINTER;
        Lock l(lock);
        if (!source) return MF_E_SHUTDOWN;
        *result = source.Get(); source->AddRef(); return S_OK;
    }
    STDMETHODIMP GetStreamDescriptor(IMFStreamDescriptor** result) override {
        if (!result) return E_POINTER;
        Lock l(lock);
        if (!queue) return MF_E_SHUTDOWN;
        *result = descriptor.Get(); descriptor->AddRef(); return S_OK;
    }
    STDMETHODIMP RequestSample(IUnknown* token) override {
        Ptr<IMFMediaEventQueue> q;
        GUID subtype;
        LONGLONG wait = 0;
        {
            Lock l(lock);
            if (!queue) return MF_E_SHUTDOWN;
            if (state != StreamRunning) return MF_E_MEDIA_SOURCE_WRONGSTATE;
            q = queue; subtype = format;
            LONGLONG now = MFGetSystemTime();
            if (nextFrame > now) wait = nextFrame - now;
            nextFrame = (nextFrame > now ? nextFrame : now) + FrameDuration;
        }
        if (wait > 0) Sleep((DWORD)(wait / 10000)); // keep 30 fps when an app asks faster
        Ptr<IMFSample> sample;
        HRESULT hr = BuildSample(subtype, sample.Out());
        if (SUCCEEDED(hr) && token) hr = sample->SetUnknown(MFSampleExtension_Token, token);
        if (SUCCEEDED(hr)) hr = q->QueueEventParamUnk(MEMediaSample, GUID_NULL, S_OK, sample.Get());
        if (FAILED(hr)) Log("RequestSample failed 0x%08lx", hr);
        return hr;
    }

    // IMFMediaStream2
    STDMETHODIMP SetStreamState(int value) override {
        int current;
        { Lock l(lock); current = state; }
        if (value == current) return S_OK;
        switch (value) {
            case StreamRunning: return Start(nullptr);
            case StreamStopped: return Stop();
            case StreamPaused:
                if (current != StreamRunning) return MF_E_INVALID_STATE_TRANSITION;
                { Lock l(lock); state = StreamPaused; }
                return S_OK;
        }
        return E_INVALIDARG;
    }
    STDMETHODIMP GetStreamState(int* value) override { if (!value) return E_POINTER; Lock l(lock); *value = state; return S_OK; }

    // IKsControl: no camera controls are exposed.
    STDMETHODIMP KsProperty(void*, ULONG, void*, ULONG, ULONG*) override { return HRESULT_FROM_WIN32(ERROR_SET_NOT_FOUND); }
    STDMETHODIMP KsMethod(void*, ULONG, void*, ULONG, ULONG*) override { return HRESULT_FROM_WIN32(ERROR_SET_NOT_FOUND); }
    STDMETHODIMP KsEvent(void*, ULONG, void*, ULONG, ULONG*) override { return HRESULT_FROM_WIN32(ERROR_SET_NOT_FOUND); }

private:
    HRESULT BuildSample(const GUID& subtype, IMFSample** result) {
        bool fresh = reader->Read(frame);
        bool nv12 = subtype != MFVideoFormat_RGB32;
        Ptr<IMFMediaBuffer> buffer;
        HRESULT hr = MFCreate2DMediaBuffer(Width, Height, nv12 ? MFVideoFormat_NV12.Data1 : 22 /* D3DFMT_X8R8G8B8 */, FALSE, buffer.Out());
        Ptr<IMF2DBuffer> buffer2d;
        if (SUCCEEDED(hr)) hr = buffer->QueryInterface(IID_IMF2DBuffer, (void**)buffer2d.Out());
        BYTE* scan = nullptr; LONG pitch = 0;
        if (SUCCEEDED(hr)) hr = buffer2d->Lock2D(&scan, &pitch);
        if (SUCCEEDED(hr)) {
            if (nv12) {
                BYTE* uv = scan + pitch * Height;
                if (fresh) BgraToNv12(frame, scan, pitch, uv, pitch);
                else { for (UINT32 r = 0; r < Height; r++) memset(scan + r * pitch, 16, Width); for (UINT32 r = 0; r < Height / 2; r++) memset(uv + r * pitch, 128, Width); }
            } else {
                for (UINT32 r = 0; r < Height; r++) {
                    if (fresh) memcpy(scan + r * pitch, frame + r * Width * 4, Width * 4);
                    else memset(scan + r * pitch, 0, Width * 4);
                }
            }
            buffer2d->Unlock2D();
            DWORD length = 0;
            if (SUCCEEDED(buffer2d->GetContiguousLength(&length))) buffer->SetCurrentLength(length);
        }
        Ptr<IMFSample> sample;
        if (SUCCEEDED(hr)) hr = MFCreateSample(sample.Out());
        if (SUCCEEDED(hr)) hr = sample->AddBuffer(buffer.Get());
        if (SUCCEEDED(hr)) hr = sample->SetSampleTime(MFGetSystemTime());
        if (SUCCEEDED(hr)) hr = sample->SetSampleDuration(FrameDuration);
        if (SUCCEEDED(hr)) { *result = sample.Get(); sample->AddRef(); }
        return hr;
    }
};

// ---- Source ----
class Source : public IMFMediaSourceEx, public IMFGetService, public IKsControlPixie, public IMFAttributes {
    volatile LONG refs = 1;
    SRWLOCK lock = SRWLOCK_INIT;
    Ptr<IMFMediaEventQueue> queue;
    Ptr<IMFPresentationDescriptor> presentation;
    Ptr<IMFAttributes> attributes;
    Stream* stream = nullptr;
    FrameReader reader;
    bool streamAnnounced = false, started = false;
    ~Source() {
        if (stream) stream->Release();
        MFShutdown();
        InterlockedDecrement(&g_objects);
        Log("source released");
    }
public:
    Source() { InterlockedIncrement(&g_objects); }
    HRESULT Initialize(IMFAttributes* from) {
        HRESULT hr = MFStartup(MF_VERSION, MFSTARTUP_NOSOCKET);
        if (SUCCEEDED(hr)) hr = MFCreateAttributes(attributes.Out(), 4);
        if (SUCCEEDED(hr) && from) hr = from->CopyAllItems(attributes.Get());
        if (SUCCEEDED(hr)) hr = MFCreateEventQueue(queue.Out());
        if (SUCCEEDED(hr)) { stream = new (std::nothrow) Stream(static_cast<IMFMediaSourceEx*>(this), &reader); if (!stream) hr = E_OUTOFMEMORY; }
        if (SUCCEEDED(hr)) hr = stream->Initialize();
        IMFStreamDescriptor* descriptors[1] = { stream ? stream->Descriptor() : nullptr };
        if (SUCCEEDED(hr)) hr = MFCreatePresentationDescriptor(1, descriptors, presentation.Out());
        if (SUCCEEDED(hr)) hr = presentation->SelectStream(0);
        Log("source initialized 0x%08lx", hr);
        return hr;
    }

    STDMETHODIMP QueryInterface(REFIID iid, void** ppv) override {
        if (!ppv) return E_POINTER;
        if (iid == IID_IUnknown || iid == IID_IMFMediaEventGenerator || iid == IID_IMFMediaSource || iid == IID_IMFMediaSourceEx)
            *ppv = static_cast<IMFMediaSourceEx*>(this);
        else if (iid == IID_IMFGetService) *ppv = static_cast<IMFGetService*>(this);
        else if (iid == IID_IKsControlPixie) *ppv = static_cast<IKsControlPixie*>(this);
        else if (iid == IID_IMFAttributes) *ppv = static_cast<IMFAttributes*>(this);
        else { *ppv = nullptr; return E_NOINTERFACE; }
        AddRef(); return S_OK;
    }
    IMPLEMENT_REFCOUNT
    FORWARD_ATTRIBUTES(attributes)

    // IMFMediaEventGenerator
    Ptr<IMFMediaEventQueue> Queue() { Lock l(lock); return queue; }
    STDMETHODIMP GetEvent(DWORD flags, IMFMediaEvent** e) override { auto q = Queue(); return q ? q->GetEvent(flags, e) : MF_E_SHUTDOWN; }
    STDMETHODIMP BeginGetEvent(IMFAsyncCallback* c, IUnknown* s) override { auto q = Queue(); return q ? q->BeginGetEvent(c, s) : MF_E_SHUTDOWN; }
    STDMETHODIMP EndGetEvent(IMFAsyncResult* r, IMFMediaEvent** e) override { auto q = Queue(); return q ? q->EndGetEvent(r, e) : MF_E_SHUTDOWN; }
    STDMETHODIMP QueueEvent(MediaEventType t, REFGUID g, HRESULT h, const PROPVARIANT* v) override { auto q = Queue(); return q ? q->QueueEventParamVar(t, g, h, v) : MF_E_SHUTDOWN; }

    // IMFMediaSource
    STDMETHODIMP GetCharacteristics(DWORD* value) override { if (!value) return E_POINTER; *value = MFMEDIASOURCE_IS_LIVE; return S_OK; }
    STDMETHODIMP CreatePresentationDescriptor(IMFPresentationDescriptor** result) override {
        if (!result) return E_POINTER;
        Lock l(lock);
        if (!presentation) return MF_E_SHUTDOWN;
        return presentation->Clone(result);
    }
    STDMETHODIMP Start(IMFPresentationDescriptor* requested, const GUID* timeFormat, const PROPVARIANT*) override {
        if (!requested) return E_INVALIDARG;
        if (timeFormat && *timeFormat != GUID_NULL) return MF_E_UNSUPPORTED_TIME_FORMAT;
        Lock l(lock);
        if (!queue) return MF_E_SHUTDOWN;
        WINBOOL selected = FALSE;
        Ptr<IMFStreamDescriptor> descriptor;
        HRESULT hr = requested->GetStreamDescriptorByIndex(0, &selected, descriptor.Out());
        if (FAILED(hr)) return hr;
        Ptr<IMFMediaType> type;
        Ptr<IMFMediaTypeHandler> handler;
        if (SUCCEEDED(descriptor->GetMediaTypeHandler(handler.Out()))) handler->GetCurrentMediaType(type.Out());
        if (selected) {
            presentation->SelectStream(0);
            hr = queue->QueueEventParamUnk(streamAnnounced ? MEUpdatedStream : MENewStream, GUID_NULL, S_OK, static_cast<IMFMediaStream2Pixie*>(stream));
            streamAnnounced = true;
            if (SUCCEEDED(hr)) hr = stream->Start(type.Get());
        } else {
            presentation->DeselectStream(0);
            if (stream->Running()) stream->Stop();
        }
        PROPVARIANT time; PropVariantInit(&time);
        time.vt = VT_I8; time.hVal.QuadPart = MFGetSystemTime();
        if (SUCCEEDED(hr)) hr = queue->QueueEventParamVar(MESourceStarted, GUID_NULL, S_OK, &time);
        started = SUCCEEDED(hr);
        Log("source start 0x%08lx", hr);
        return hr;
    }
    STDMETHODIMP Stop() override {
        Lock l(lock);
        if (!queue) return MF_E_SHUTDOWN;
        if (stream->Running()) stream->Stop();
        presentation->DeselectStream(0);
        started = false;
        PROPVARIANT time; PropVariantInit(&time);
        time.vt = VT_I8; time.hVal.QuadPart = MFGetSystemTime();
        return queue->QueueEventParamVar(MESourceStopped, GUID_NULL, S_OK, &time);
    }
    STDMETHODIMP Pause() override { return MF_E_INVALID_STATE_TRANSITION; }
    STDMETHODIMP Shutdown() override {
        Ptr<IMFMediaEventQueue> q;
        {
            Lock l(lock);
            if (!queue) return MF_E_SHUTDOWN;
            q = queue; queue.Reset(); presentation.Reset();
        }
        q->Shutdown();
        if (stream) stream->Shutdown();
        Log("source shut down");
        return S_OK;
    }

    // IMFMediaSourceEx
    STDMETHODIMP GetSourceAttributes(IMFAttributes** result) override {
        if (!result) return E_POINTER;
        Lock l(lock);
        if (!attributes) return MF_E_SHUTDOWN;
        *result = attributes.Get(); attributes->AddRef(); return S_OK;
    }
    STDMETHODIMP GetStreamAttributes(DWORD id, IMFAttributes** result) override {
        if (!result) return E_POINTER;
        if (id != 0 || !stream) return MF_E_INVALIDSTREAMNUMBER;
        *result = stream->Attributes(); (*result)->AddRef(); return S_OK;
    }
    STDMETHODIMP SetD3DManager(IUnknown*) override { return S_OK; } // frames are produced in system memory

    // IMFGetService
    STDMETHODIMP GetService(REFGUID, REFIID, LPVOID* ppv) override { if (ppv) *ppv = nullptr; return MF_E_UNSUPPORTED_SERVICE; }

    // IKsControl
    STDMETHODIMP KsProperty(void*, ULONG, void*, ULONG, ULONG*) override { return HRESULT_FROM_WIN32(ERROR_SET_NOT_FOUND); }
    STDMETHODIMP KsMethod(void*, ULONG, void*, ULONG, ULONG*) override { return HRESULT_FROM_WIN32(ERROR_SET_NOT_FOUND); }
    STDMETHODIMP KsEvent(void*, ULONG, void*, ULONG, ULONG*) override { return HRESULT_FROM_WIN32(ERROR_SET_NOT_FOUND); }
};

// ---- Activator: the Frame Server creates the CLSID and asks it for the media source ----
class Activator : public IMFActivate {
    volatile LONG refs = 1;
    SRWLOCK lock = SRWLOCK_INIT;
    Ptr<IMFAttributes> store;
    Ptr<IMFMediaSource> source;
    ~Activator() { InterlockedDecrement(&g_objects); }
public:
    Activator() { InterlockedIncrement(&g_objects); }
    HRESULT Initialize() { return MFCreateAttributes(store.Out(), 1); }
    STDMETHODIMP QueryInterface(REFIID iid, void** ppv) override {
        if (!ppv) return E_POINTER;
        if (iid == IID_IUnknown || iid == IID_IMFAttributes || iid == IID_IMFActivate) *ppv = static_cast<IMFActivate*>(this);
        else { *ppv = nullptr; return E_NOINTERFACE; }
        AddRef(); return S_OK;
    }
    IMPLEMENT_REFCOUNT
    FORWARD_ATTRIBUTES(store)
    STDMETHODIMP ActivateObject(REFIID iid, void** ppv) override {
        if (!ppv) return E_POINTER;
        Lock l(lock);
        if (!source) {
            Source* created = new (std::nothrow) Source();
            if (!created) return E_OUTOFMEMORY;
            HRESULT hr = created->Initialize(store.Get());
            if (FAILED(hr)) { created->Release(); return hr; }
            source.Attach(created);
        }
        return source->QueryInterface(iid, ppv);
    }
    STDMETHODIMP ShutdownObject() override {
        Ptr<IMFMediaSource> s;
        { Lock l(lock); s = source; source.Reset(); }
        if (s) s->Shutdown();
        return S_OK;
    }
    STDMETHODIMP DetachObject() override { Lock l(lock); source.Reset(); return S_OK; }
};

class Factory : public IClassFactory {
public:
    STDMETHODIMP QueryInterface(REFIID iid, void** ppv) override {
        if (!ppv) return E_POINTER;
        if (iid == IID_IUnknown || iid == IID_IClassFactory) { *ppv = static_cast<IClassFactory*>(this); return S_OK; }
        *ppv = nullptr; return E_NOINTERFACE;
    }
    STDMETHODIMP_(ULONG) AddRef() override { return 2; }
    STDMETHODIMP_(ULONG) Release() override { return 1; }
    STDMETHODIMP CreateInstance(IUnknown* outer, REFIID iid, void** ppv) override {
        if (!ppv) return E_POINTER;
        *ppv = nullptr;
        if (outer) return CLASS_E_NOAGGREGATION;
        Activator* activator = new (std::nothrow) Activator();
        if (!activator) return E_OUTOFMEMORY;
        HRESULT hr = activator->Initialize();
        if (SUCCEEDED(hr)) hr = activator->QueryInterface(iid, ppv);
        activator->Release();
        Log("activator created 0x%08lx", hr);
        return hr;
    }
    STDMETHODIMP LockServer(WINBOOL lockIt) override { if (lockIt) InterlockedIncrement(&g_locks); else InterlockedDecrement(&g_locks); return S_OK; }
};
static Factory g_factory;

// ---- COM exports ----
extern "C" BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) { g_module = instance; DisableThreadLibraryCalls(instance); }
    return TRUE;
}
extern "C" HRESULT WINAPI DllGetClassObject(REFCLSID clsid, REFIID iid, void** ppv) {
    if (!ppv) return E_POINTER;
    *ppv = nullptr;
    if (clsid != CLSID_PixieVirtualCamera) return CLASS_E_CLASSNOTAVAILABLE;
    return g_factory.QueryInterface(iid, ppv);
}
extern "C" HRESULT WINAPI DllCanUnloadNow() { return g_objects == 0 && g_locks == 0 ? S_OK : S_FALSE; }

// The MSI writes these keys itself; DllRegisterServer is for development (elevated regsvr32).
extern "C" HRESULT WINAPI DllRegisterServer() {
    wchar_t path[MAX_PATH], key[128];
    if (!GetModuleFileNameW(g_module, path, MAX_PATH)) return HRESULT_FROM_WIN32(GetLastError());
    swprintf(key, 128, L"Software\\Classes\\CLSID\\%ls\\InprocServer32", ClsidString);
    HKEY handle;
    LSTATUS status = RegCreateKeyExW(HKEY_LOCAL_MACHINE, key, 0, nullptr, 0, KEY_WRITE, nullptr, &handle, nullptr);
    if (status != ERROR_SUCCESS) return HRESULT_FROM_WIN32(status);
    RegSetValueExW(handle, nullptr, 0, REG_SZ, (const BYTE*)path, (DWORD)((wcslen(path) + 1) * sizeof(wchar_t)));
    const wchar_t both[] = L"Both";
    RegSetValueExW(handle, L"ThreadingModel", 0, REG_SZ, (const BYTE*)both, sizeof(both));
    RegCloseKey(handle);
    return S_OK;
}
extern "C" HRESULT WINAPI DllUnregisterServer() {
    wchar_t key[128];
    swprintf(key, 128, L"Software\\Classes\\CLSID\\%ls", ClsidString);
    LSTATUS status = RegDeleteTreeW(HKEY_LOCAL_MACHINE, key);
    return status == ERROR_SUCCESS || status == ERROR_FILE_NOT_FOUND ? S_OK : HRESULT_FROM_WIN32(status);
}

// ---- Helper exports for PixiePrivacyPro.exe ----
static SRWLOCK g_cameraLock = SRWLOCK_INIT;
static IMFVirtualCameraPixie* g_camera;

// Registers a session virtual camera: it exists while the calling process keeps it, no admin rights needed.
extern "C" HRESULT WINAPI PixieVCam_Create(LPCWSTR friendlyName) {
    Lock l(g_cameraLock);
    if (g_camera) return S_FALSE;
    HRESULT hr = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    if (FAILED(hr) && hr != RPC_E_CHANGED_MODE) return hr;
    hr = MFStartup(MF_VERSION, MFSTARTUP_NOSOCKET);
    if (FAILED(hr)) return hr;
    HMODULE sensors = LoadLibraryExW(L"mfsensorgroup.dll", nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
    CreateVirtualCameraFn create = sensors ? (CreateVirtualCameraFn)(void*)GetProcAddress(sensors, "MFCreateVirtualCamera") : nullptr;
    if (!create) { MFShutdown(); return HRESULT_FROM_WIN32(ERROR_PROC_NOT_FOUND); } // Windows 10 has no MFCreateVirtualCamera
    IMFVirtualCameraPixie* camera = nullptr;
    hr = create(0 /* SoftwareCameraSource */, 0 /* Session */, 0 /* CurrentUser */, friendlyName, ClsidString, nullptr, 0, &camera);
    if (SUCCEEDED(hr)) {
        // Sanity check of the hand-written interface declaration.
        IUnknown* check = nullptr;
        hr = camera->QueryInterface(IID_IMFVirtualCameraPixie, (void**)&check);
        if (check) check->Release();
    }
    if (SUCCEEDED(hr)) hr = camera->Start(nullptr);
    Log("PixieVCam_Create 0x%08lx", hr);
    if (FAILED(hr)) { if (camera) { camera->Shutdown(); camera->Release(); } MFShutdown(); return hr; }
    g_camera = camera;
    return S_OK;
}

extern "C" HRESULT WINAPI PixieVCam_Remove() {
    Lock l(g_cameraLock);
    if (!g_camera) return S_FALSE;
    HRESULT hr = g_camera->Remove();
    g_camera->Shutdown();
    g_camera->Release();
    g_camera = nullptr;
    MFShutdown();
    Log("PixieVCam_Remove 0x%08lx", hr);
    return hr;
}
