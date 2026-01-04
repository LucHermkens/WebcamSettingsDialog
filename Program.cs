using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace WebcamSettings;

// Custom IEnumMoniker interface for .NET 10 compatibility
[ComImport, Guid("00000102-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IEnumMoniker
{
    [PreserveSig]
    int Next([In] int celt, [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] IMoniker[] rgelt, [Out] out int pceltFetched);
    [PreserveSig]
    int Skip([In] int celt);
    [PreserveSig]
    int Reset();
    [PreserveSig]
    int Clone([Out] out IEnumMoniker ppenum);
}

class Program
{
    // COM interfaces
    [ComImport, Guid("29840822-5B84-11D0-BD3B-00A0C911CE86")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ICreateDevEnum
    {
        int CreateClassEnumerator([In] ref Guid pType, [Out] out IEnumMoniker ppEnumMoniker, [In] int dwFlags);
    }

    [ComImport, Guid("B196B28B-BAB4-101A-B69C-00AA00341D07")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ISpecifyPropertyPages
    {
        int GetPages(out CAUUID pPages);
    }

    [ComImport, Guid("55272A00-42CB-11CE-8135-00AA004BB851")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyBag
    {
        int Read([In, MarshalAs(UnmanagedType.LPWStr)] string pszPropName, [In, Out] ref object pVar, [In] IntPtr pErrorLog);
        int Write([In, MarshalAs(UnmanagedType.LPWStr)] string pszPropName, [In] ref object pVar);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct CAUUID
    {
        public int cElems;
        public IntPtr pElems;
    }

    // P/Invoke declarations
    [DllImport("oleaut32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    static extern int OleCreatePropertyFrame(
        IntPtr hwndOwner,
        int x,
        int y,
        [MarshalAs(UnmanagedType.LPWStr)] string lpszCaption,
        int cObjects,
        [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.IUnknown)] object[] ppUnk,
        int cPages,
        IntPtr lpPageClsID,
        int lcid,
        int dwReserved,
        IntPtr lpvReserved);

    [DllImport("ole32.dll")]
    static extern int CoInitialize(IntPtr pvReserved);

    [DllImport("ole32.dll")]
    static extern void CoUninitialize();

    [DllImport("ole32.dll")]
    static extern int CoCreateInstance(
        [In] ref Guid rclsid,
        IntPtr pUnkOuter,
        uint dwClsContext,
        [In] ref Guid riid,
        [MarshalAs(UnmanagedType.IUnknown)] out object ppv);

    [DllImport("ole32.dll")]
    static extern void CoTaskMemFree(IntPtr pv);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll")]
    static extern bool ReadConsoleInput(IntPtr hConsoleInput, out INPUT_RECORD lpBuffer, uint nLength, out uint lpNumberOfEventsRead);

    [DllImport("kernel32.dll")]
    static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll")]
    static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

    const int STD_INPUT_HANDLE = -10;
    const uint ENABLE_ECHO_INPUT = 0x0004;
    const uint ENABLE_LINE_INPUT = 0x0002;
    const uint ENABLE_PROCESSED_INPUT = 0x0001;
    const uint CLSCTX_INPROC_SERVER = 1;

    [StructLayout(LayoutKind.Explicit, CharSet = CharSet.Unicode)]
    struct INPUT_RECORD
    {
        [FieldOffset(0)]
        public ushort EventType;
        [FieldOffset(4)]
        public KEY_EVENT_RECORD KeyEvent;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct KEY_EVENT_RECORD
    {
        public bool bKeyDown;
        public ushort wRepeatCount;
        public ushort wVirtualKeyCode;
        public ushort wVirtualScanCode;
        public char UnicodeChar;
        public uint dwControlKeyState;
    }

    static void Main(string[] args)
    {
        // Initialize COM
        CoInitialize(IntPtr.Zero);
        try
        {
            // Enumerate video capture devices
            var devices = EnumerateVideoDevices();

            if (devices.Count == 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("No video devices found.");
                Console.ResetColor();
                Environment.Exit(1);
            }

            // Display device menu
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\nAvailable video devices:\n");
            Console.ResetColor();

            for (int i = 0; i < devices.Count; i++)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"  {i + 1}. {devices[i].Name}");
                Console.ResetColor();
            }
            Console.WriteLine();

            // Get user selection
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write($"Select device number (1-{devices.Count}): ");
            Console.ResetColor();

            int selection = GetKeyPress() - '0';
            Console.WriteLine();

            if (selection < 1 || selection > devices.Count)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Invalid selection.");
                Console.ResetColor();
                Environment.Exit(1);
            }

            var selectedDevice = devices[selection - 1];

            // Open the device settings dialog
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"\nOpening settings dialog for: {selectedDevice.Name}\n");
            Console.ResetColor();

            OpenDevicePropertyPage(selectedDevice.Moniker);
        }
        finally
        {
            CoUninitialize();
        }
    }

    static char GetKeyPress()
    {
        IntPtr hStdin = GetStdHandle(STD_INPUT_HANDLE);
        GetConsoleMode(hStdin, out uint mode);
        uint originalMode = mode;
        SetConsoleMode(hStdin, mode & ~(ENABLE_LINE_INPUT | ENABLE_ECHO_INPUT | ENABLE_PROCESSED_INPUT));

        INPUT_RECORD inputRecord;
        uint eventsRead;

        while (true)
        {
            ReadConsoleInput(hStdin, out inputRecord, 1, out eventsRead);
            if (inputRecord.EventType == 1 && inputRecord.KeyEvent.bKeyDown) // KEY_EVENT = 1
            {
                char ch = inputRecord.KeyEvent.UnicodeChar;
                if (char.IsDigit(ch))
                {
                    SetConsoleMode(hStdin, originalMode);
                    return ch;
                }
            }
        }
    }

    static List<DeviceInfo> EnumerateVideoDevices()
    {
        var devices = new List<DeviceInfo>();

        // CLSID for System Device Enumerator
        Guid clsidSystemDeviceEnum = new Guid("62BE5D10-60EB-11d0-BD3B-00A0C911CE86");
        Guid iidICreateDevEnum = new Guid("29840822-5B84-11D0-BD3B-00A0C911CE86");

        int hr = CoCreateInstance(ref clsidSystemDeviceEnum, IntPtr.Zero, CLSCTX_INPROC_SERVER, ref iidICreateDevEnum, out object devEnumObj);
        if (hr != 0)
        {
            Console.WriteLine($"CoCreateInstance failed: 0x{hr:X8}");
            return devices;
        }

        ICreateDevEnum devEnum = (ICreateDevEnum)devEnumObj;

        // CLSID for video input devices
        Guid videoInputDeviceCategory = new Guid("860BB310-5D01-11d0-BD3B-00A0C911CE86");

        hr = devEnum.CreateClassEnumerator(ref videoInputDeviceCategory, out IEnumMoniker enumMoniker, 0);
        if (hr != 0 || enumMoniker == null)
        {
            Console.WriteLine($"CreateClassEnumerator failed: 0x{hr:X8}");
            return devices;
        }

        IMoniker[] monikers = new IMoniker[1];
        int fetched = 0;

        int nextHr = enumMoniker.Next(1, monikers, out fetched);
        while (nextHr == 0 && fetched == 1)
        {
            IMoniker moniker = monikers[0];

            try
            {
                Guid iidIPropertyBag = new Guid("55272A00-42CB-11CE-8135-00AA004BB851");
                moniker.BindToStorage(null!, null!, ref iidIPropertyBag, out object? propBagObj);

                if (propBagObj is IPropertyBag propBag)
                {
                    object? friendlyName = null;
                    propBag.Read("FriendlyName", ref friendlyName!, IntPtr.Zero);

                    devices.Add(new DeviceInfo
                    {
                        Name = friendlyName?.ToString() ?? "Unknown Device",
                        Moniker = moniker
                    });
                }
            }
            catch (Exception ex)
            {
                // Skip devices that can't be read
                Console.WriteLine($"Error reading device: {ex.Message}");
            }

            nextHr = enumMoniker.Next(1, monikers, out fetched);
        }

        return devices;
    }

    static void OpenDevicePropertyPage(IMoniker moniker)
    {
        try
        {
            // Bind to IUnknown first, then cast to ISpecifyPropertyPages
            // The moniker will create the filter object
            Guid iidIUnknown = new Guid("00000000-0000-0000-C000-000000000046");
            moniker.BindToObject(null!, null!, ref iidIUnknown, out object? filterObj);

            // Try to get ISpecifyPropertyPages interface
            ISpecifyPropertyPages? propertyPages = filterObj as ISpecifyPropertyPages;

            if (propertyPages != null)
            {
                propertyPages.GetPages(out CAUUID pages);

                if (pages.cElems > 0 && pages.pElems != IntPtr.Zero)
                {
                    // Copy GUIDs to managed array
                    int guidSize = Marshal.SizeOf(typeof(Guid));
                    Guid[] pageGuids = new Guid[pages.cElems];

                    for (int i = 0; i < pages.cElems; i++)
                    {
                        IntPtr guidPtr = new IntPtr(pages.pElems.ToInt64() + i * guidSize);
                        pageGuids[i] = Marshal.PtrToStructure<Guid>(guidPtr);
                    }

                    // Allocate unmanaged memory for GUIDs
                    IntPtr guidArray = Marshal.AllocCoTaskMem(guidSize * pages.cElems);
                    try
                    {
                        for (int i = 0; i < pages.cElems; i++)
                        {
                            IntPtr guidPtr = new IntPtr(guidArray.ToInt64() + i * guidSize);
                            Marshal.StructureToPtr(pageGuids[i], guidPtr, false);
                        }

                        object[] objects = new object[] { filterObj };

                        // Create property frame
                        OleCreatePropertyFrame(
                            IntPtr.Zero, // parent window
                            0, 0, // x, y
                            "Webcam Settings", // title
                            objects.Length, // number of objects
                            objects, // objects
                            pages.cElems, // number of property pages
                            guidArray, // page CLSIDs
                            0, // locale ID
                            0, // reserved
                            IntPtr.Zero); // reserved
                    }
                    finally
                    {
                        Marshal.FreeCoTaskMem(guidArray);
                        CoTaskMemFree(pages.pElems);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error opening settings dialog: {ex.Message}");
            Console.ResetColor();
            Environment.Exit(1);
        }
    }

    class DeviceInfo
    {
        public string Name { get; set; } = "";
        public IMoniker Moniker { get; set; } = null!;
    }
}
