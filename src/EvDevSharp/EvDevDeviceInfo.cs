using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using static EvDevSharp.NativeUnsafeMethods;
using static EvDevSharp.Ioctl;
namespace EvDevSharp
{
    public unsafe class EvDevDeviceInfo
    {
        public EvDevDeviceId Id { get; }
        public string UniqueId { get; }
        public Version DriverVersion { get; }
        public string Name { get; }
        public string DevicePath { get; }
        public EvDevGuessedDeviceType GuessedDeviceType { get; set; }
        
        public Dictionary<EvDevEventType, List<int>> RawEventCodes { get; } = new Dictionary<EvDevEventType, List<int>>();
        
        public List<EvDevKey> Keys { get; set; }
        public List<EvDevRelativeAxis> RelativeAxises { get; set; }
        public Dictionary<EvDevAbsoluteAxis, EvDevAbsAxisInfo> AbsoluteAxises { get; set; }
        public List<EvDevProperties> Properties { get; set; }

        EvDevDeviceInfo(int fd, string path)
        {
            DevicePath = path;
            
            int version = 0;
            if (ioctl(fd, EVIOCGVERSION, &version) == -1)
                throw new Win32Exception($"Unable to get evdev driver version for {path}");
            DriverVersion = new Version(version >> 16, (version >> 8) & 0xff, version & 0xff);
            
            var id = stackalloc ushort[4];
            
            if (ioctl(fd, EVIOCGID, id) == -1)
                throw new Win32Exception($"Unable to get evdev id for {path}");
            Id = new EvDevDeviceId
            {
                Bus = id[0],
                Vendor = id[1],
                Product = id[2],
                Version = id[3],
            };
            
            var str = stackalloc byte[256];
            if (ioctl(fd, EVIOCGNAME(256), str) == -1)
                throw new Win32Exception($"Unable to get evdev name for {path}");
            Name = Marshal.PtrToStringAnsi(new IntPtr(str));

            if (ioctl(fd, EVIOCGUNIQ(256), str) == -1)
            {
                UniqueId = Marshal.PtrToStringAnsi(new IntPtr(str));
            }

            var bitCount = (int) EvDevKey.KEY_MAX;
            var bits = new byte[(int)bitCount / 8 + 1];

            ioctl(fd, EVIOCGBIT(EvDevEventType.EV_SYN, bitCount), bits);
            var supportedEvents = DecodeBits(bits).Cast<EvDevEventType>().ToList();
            foreach (var evType in supportedEvents)
            {
                if(evType == EvDevEventType.EV_SYN)
                    continue;
                Array.Clear(bits, 0, bits.Length);
                ioctl(fd, EVIOCGBIT(evType, bitCount), bits);
                RawEventCodes[evType] = DecodeBits(bits);
            }

            if (RawEventCodes.TryGetValue(EvDevEventType.EV_KEY, out var keys))
                Keys = keys.Cast<EvDevKey>().ToList();

            if (RawEventCodes.TryGetValue(EvDevEventType.EV_REL, out var rel))
                RelativeAxises = rel.Cast<EvDevRelativeAxis>().ToList();

            if (RawEventCodes.TryGetValue(EvDevEventType.EV_ABS, out var abs))
            {
                AbsoluteAxises = abs.ToDictionary(x => (EvDevAbsoluteAxis) x, x =>
                {
                    var absInfo = default(EvDevAbsAxisInfo);
                    ioctl(fd, EVIOCGABS(x), &absInfo);
                    return absInfo;
                });
            }

            Array.Clear(bits, 0, bits.Length);
            ioctl(fd, EVIOCGPROP((int) EvDevProperties.INPUT_PROP_CNT), bits);
            Properties = DecodeBits(bits, (int) EvDevProperties.INPUT_PROP_CNT).Cast<EvDevProperties>().ToList();

            GuessedDeviceType = GuessDeviceType();
        }


        static List<int> DecodeBits(byte[] arr, int? max = null)
        {
            var rv = new List<int>();
            var m = max ?? arr.Length * 8;
            for (int idx = 0; idx < m; idx++)
            {
                var b = arr[idx / 8];
                var shift = idx % 8;
                var v = (b >> shift) & 1;
                if (v != 0)
                    rv.Add(idx);
            }

            return rv;
        }
        
        

        public static EvDevDeviceInfo FromPath(string path)
        {
            var fd = open(path, 2048, 0);
            if (fd <= 0)
                throw new Win32Exception("Unable to open " + path);
            EvDevDeviceInfo info;
            try
            {
                return new EvDevDeviceInfo(fd, path);
            }
            finally
            {
                close(fd);
            }
        }
        
        public static List<EvDevDeviceInfo> EnumerateDevices()
        {
            var lst = new List<EvDevDeviceInfo>();
            foreach (var path in Directory.GetFiles("/dev/input/").Where(x => Path.GetFileName(x).StartsWith("event")))
            {
                var fd = open(path, 2048, 0);
                if (fd <= 0)
                    continue;
                EvDevDeviceInfo info;
                try
                {
                    lst.Add(new EvDevDeviceInfo(fd, path));
                }
                finally
                {
                    close(fd);
                }
            }

            return lst;
        }

        public EvDevGuessedDeviceType GuessDeviceType()
        {
            if (Name != null)
            {
                // Often device name says what it is

                var isAbsolutePointingDevice = AbsoluteAxises?.ContainsKey(EvDevAbsoluteAxis.ABS_X) == true;
                
                var n = Name.ToLowerInvariant();
                if (n.Contains("touchscreen")
                    && isAbsolutePointingDevice
                    && Keys?.Contains(EvDevKey.BTN_TOUCH) == true)
                    return EvDevGuessedDeviceType.TouchScreen;
                
                if (n.Contains("tablet")
                    && isAbsolutePointingDevice
                    && Keys?.Contains(EvDevKey.BTN_LEFT) == true)
                    return EvDevGuessedDeviceType.Tablet;
                
                if (n.Contains("touchpad")
                    && isAbsolutePointingDevice
                    && Keys?.Contains(EvDevKey.BTN_LEFT) == true)
                    return EvDevGuessedDeviceType.TouchPad;

                if (n.Contains("keyboard")
                    && Keys != null)
                    return EvDevGuessedDeviceType.Keyboard;

                if (n.Contains("gamepad") || n.Contains("joystick")
                    && Keys != null)
                    return EvDevGuessedDeviceType.GamePad;
            }

            if (Keys?.Contains(EvDevKey.BTN_TOUCH) == true
                && Properties.Contains(EvDevProperties.INPUT_PROP_DIRECT))
                return EvDevGuessedDeviceType.TouchScreen;

            
            if (Keys?.Contains(EvDevKey.BTN_SOUTH) == true)
                return EvDevGuessedDeviceType.GamePad;
            
            if (Keys?.Contains(EvDevKey.BTN_LEFT) == true && Keys?.Contains(EvDevKey.BTN_RIGHT) == true)
            {
                  
                if (AbsoluteAxises != null)
                {
                    if (AbsoluteAxises?.ContainsKey(EvDevAbsoluteAxis.ABS_X) == true)
                    {
                        if (Properties.Contains(EvDevProperties.INPUT_PROP_DIRECT))
                            return EvDevGuessedDeviceType.Tablet;
                        return EvDevGuessedDeviceType.TouchPad;
                    }
                }

                if (RelativeAxises?.Contains(EvDevRelativeAxis.REL_X) == true &&
                    RelativeAxises.Contains(EvDevRelativeAxis.REL_Y) == true)
                    return EvDevGuessedDeviceType.Mouse;
            }

            if (Keys != null)
                return EvDevGuessedDeviceType.Keyboard;

            return EvDevGuessedDeviceType.Unknown;
        }
    }

    public struct EvDevDeviceId
    {
        public ushort Bus { get; set; }
        public ushort Vendor { get; set; }
        public ushort Product { get; set; }
        public ushort Version { get; set; }

        public override string ToString()
        {
            return $"Bus: 0x{Bus:x} Vendor: 0x{Vendor:x} Product: 0x{Product:x} Version: 0x{Version:x}";
        }
    }
    
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct EvDevAbsAxisInfo
    {
        public int Value;
        public int Min;
        public int Max;
        public int Fuzz;
        public int Flat;
        public int Resolution;
    }

    public enum EvDevGuessedDeviceType
    {
        Unknown,
        Keyboard,
        Mouse,
        TouchPad,
        TouchScreen,
        Tablet,
        GamePad
    }
}