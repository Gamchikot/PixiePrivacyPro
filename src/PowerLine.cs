using System;
using System.Runtime.InteropServices;

// Anti-flicker: the standard UVC power line frequency control of EMEET PIXY (1 = 50 Hz, 2 = 60 Hz).
// The camera takes a new value only while it streams video; Windows keeps the last one in the driver's
// registry and the camera keeps it across reconnects. Pro re-applies the user's choice when video starts.
public static class PowerLine {
    public const int Hz50=1,Hz60=2;
    const int Property=13; // KSPROPERTY_VIDEOPROCAMP_POWERLINE_FREQUENCY

    // 0 when the camera is not found or does not answer.
    public static int Read() {
        int value=0;
        Use(amp=>{int v,flags;if(amp.Get(Property,out v,out flags)==0 && (v==Hz50||v==Hz60)) value=v;});
        return value;
    }
    // Returns what the camera reports afterwards.
    public static int Write(int value) {
        Use(amp=>amp.Set(Property,value,2));
        return Read();
    }

    static void Use(Action<IAMVideoProcAmp> action) {
        var devEnum=(ICreateDevEnum)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("62BE5D10-60EB-11d0-BD3B-00A0C911CE86")));
        try {
            var category=new Guid("860BB310-5D01-11d0-BD3B-00A0C911CE86"); // CLSID_VideoInputDeviceCategory
            IEnumMoniker list;
            if(devEnum.CreateClassEnumerator(ref category,out list,0)!=0 || list==null) return;
            try {
                var item=new IMoniker[1];
                while(list.Next(1,item,IntPtr.Zero)==0) {
                    try {
                        object bag,path,filter;
                        var bagId=typeof(IPropertyBag).GUID;
                        item[0].BindToStorage(IntPtr.Zero,IntPtr.Zero,ref bagId,out bag);
                        try {((IPropertyBag)bag).Read("DevicePath",out path,IntPtr.Zero);} finally {Marshal.ReleaseComObject(bag);}
                        if(path==null || path.ToString().IndexOf("vid_328f&pid_00c0",StringComparison.OrdinalIgnoreCase)<0) continue;
                        var filterId=new Guid("56a86899-0ad4-11ce-b03a-0020af0ba770"); // IBaseFilter
                        item[0].BindToObject(IntPtr.Zero,IntPtr.Zero,ref filterId,out filter);
                        try {var amp=filter as IAMVideoProcAmp;if(amp!=null) {action(amp);return;}}
                        finally {Marshal.ReleaseComObject(filter);}
                    } finally {Marshal.ReleaseComObject(item[0]);}
                }
            } finally {Marshal.ReleaseComObject(list);}
        } finally {Marshal.ReleaseComObject(devEnum);}
    }

    [ComImport, Guid("29840822-5B84-11D0-BD3B-00A0C911CE86"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ICreateDevEnum {[PreserveSig] int CreateClassEnumerator(ref Guid category,out IEnumMoniker list,int flags);}
    [ComImport, Guid("00000102-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IEnumMoniker {[PreserveSig] int Next(int count,[Out, MarshalAs(UnmanagedType.LPArray,SizeParamIndex=0)] IMoniker[] items,IntPtr fetched);}
    [ComImport, Guid("0000000f-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMoniker {
        void GetClassID(out Guid id);[PreserveSig] int IsDirty();void Load(IntPtr stream);void Save(IntPtr stream,bool clearDirty);void GetSizeMax(out long size);
        void BindToObject(IntPtr context,IntPtr left,ref Guid iid,[MarshalAs(UnmanagedType.Interface)] out object result);
        void BindToStorage(IntPtr context,IntPtr left,ref Guid iid,[MarshalAs(UnmanagedType.Interface)] out object result);
    }
    [ComImport, Guid("55272A00-42CB-11CE-8135-00AA004BB851"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyBag {[PreserveSig] int Read([MarshalAs(UnmanagedType.LPWStr)] string name,out object value,IntPtr log);}
    [ComImport, Guid("C6E13360-30AC-11d0-A18C-00A0C9118956"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAMVideoProcAmp {
        [PreserveSig] int GetRange(int property,out int min,out int max,out int step,out int defaultValue,out int flags);
        [PreserveSig] int Set(int property,int value,int flags);
        [PreserveSig] int Get(int property,out int value,out int flags);
    }
}
