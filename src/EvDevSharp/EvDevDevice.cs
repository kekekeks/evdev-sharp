using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using EvDevSharp.InteropStructs;
using static EvDevSharp.IoCtlRequest;

namespace EvDevSharp;

[SupportedOSPlatform("Linux")]
public unsafe sealed partial class EvDevDevice : IDisposable
{
    [DllImport("libc", SetLastError = true)]
    private static extern int ioctl(IntPtr fd, CULong request, void* data);

    [DllImport("libc", SetLastError = true)]
    private static extern int ioctl(IntPtr fd, CULong request, [Out] byte[] data);


    private const string InputPath = "/dev/input/";
    private const string InputPathSearchPattern = "event*";


    public EvDevDeviceId Id { get; }
    public string? UniqueId { get; }
    public Version DriverVersion { get; }
    public string? Name { get; }
    public string DevicePath { get; }
    public EvDevGuessedDeviceType GuessedDeviceType { get; set; }
    public Dictionary<EvDevEventType, List<int>> RawEventCodes { get; } = new();
    public List<EvDevKeyCode>? Keys { get; set; }
    public List<EvDevRelativeAxisCode>? RelativeAxises { get; set; }
    public Dictionary<EvDevAbsoluteAxisCode, EvDevAbsAxisInfo>? AbsoluteAxises { get; set; }
    public List<EvDevProperty> Properties { get; set; }


    private EvDevDevice(string path)
    {
        using var eventFile = File.OpenRead(path);
        var fd = eventFile.SafeFileHandle.DangerousGetHandle();

        DevicePath = path;

        int version = 0;
        if (ioctl(fd, new CULong(EVIOCGVERSION), &version) == -1)
            throw new Win32Exception($"Unable to get evdev driver version for {path}");

        DriverVersion = new Version(version >> 16, (version >> 8) & 0xff, version & 0xff);

        var id = stackalloc ushort[4];

        if (ioctl(fd, new CULong(EVIOCGID), id) == -1)
            throw new Win32Exception($"Unable to get evdev id for {path}");

        Id = new EvDevDeviceId
        {
            Bus = id[0],
            Vendor = id[1],
            Product = id[2],
            Version = id[3],
        };

        var str = stackalloc byte[256];
        if (ioctl(fd, new CULong(EVIOCGNAME(256)), str) == -1)
            throw new Win32Exception($"Unable to get evdev name for {path}");

        Name = Marshal.PtrToStringAnsi(new IntPtr(str));

        // if (ioctl(fd, new CULong(EVIOCGUNIQ(256)), str) == -1)
        //     throw new Win32Exception($"Unable to get evdev unique ID for {path}");

        // UniqueId = Marshal.PtrToStringAnsi(new IntPtr(str));

        var bitCount = (int)EvDevKeyCode.KEY_MAX;
        var bits = new byte[(int)bitCount / 8 + 1];

        ioctl(fd, new CULong(EVIOCGBIT(EvDevEventType.EV_SYN, bitCount)), bits);
        var supportedEvents = DecodeBits(bits).Cast<EvDevEventType>().ToList();
        foreach (var evType in supportedEvents)
        {
            if (evType == EvDevEventType.EV_SYN)
                continue;
            Array.Clear(bits, 0, bits.Length);
            ioctl(fd, new CULong(EVIOCGBIT(evType, bitCount)), bits);
            RawEventCodes[evType] = DecodeBits(bits);
        }

        if (RawEventCodes.TryGetValue(EvDevEventType.EV_KEY, out var keys))
            Keys = keys.Cast<EvDevKeyCode>().ToList();

        if (RawEventCodes.TryGetValue(EvDevEventType.EV_REL, out var rel))
            RelativeAxises = rel.Cast<EvDevRelativeAxisCode>().ToList();

        if (RawEventCodes.TryGetValue(EvDevEventType.EV_ABS, out var abs))
        {
            AbsoluteAxises = abs.ToDictionary(
                x => (EvDevAbsoluteAxisCode)x,
                x =>
                {
                    var absInfo = default(EvDevAbsAxisInfo);
                    ioctl(fd, new CULong(EVIOCGABS(x)), &absInfo);
                    return absInfo;
                });
        }

        Array.Clear(bits, 0, bits.Length);
        ioctl(fd, new CULong(EVIOCGPROP((int)EvDevProperty.INPUT_PROP_CNT)), bits);
        Properties = DecodeBits(bits, (int)EvDevProperty.INPUT_PROP_CNT).Cast<EvDevProperty>().ToList();

        GuessedDeviceType = GuessDeviceType();
    }

    private static List<int> DecodeBits(byte[] arr, int? max = null)
    {
        var rv = new List<int>();
        max ??= arr.Length * 8;
        for (int idx = 0; idx < max; idx++)
        {
            var b = arr[idx / 8];
            var shift = idx % 8;
            var v = (b >> shift) & 1;
            if (v != 0)
                rv.Add(idx);
        }

        return rv;
    }

    private EvDevGuessedDeviceType GuessDeviceType()
    {
        if (Name != null)
        {
            // Often device name says what it is
            var isAbsolutePointingDevice = AbsoluteAxises?.ContainsKey(EvDevAbsoluteAxisCode.ABS_X) == true;

            var n = Name.ToLowerInvariant();
            if (n.Contains("touchscreen")
                && isAbsolutePointingDevice
                && Keys?.Contains(EvDevKeyCode.BTN_TOUCH) == true)
                return EvDevGuessedDeviceType.TouchScreen;

            if (n.Contains("tablet")
                && isAbsolutePointingDevice
                && Keys?.Contains(EvDevKeyCode.BTN_LEFT) == true)
                return EvDevGuessedDeviceType.Tablet;

            if (n.Contains("touchpad")
                && isAbsolutePointingDevice
                && Keys?.Contains(EvDevKeyCode.BTN_LEFT) == true)
                return EvDevGuessedDeviceType.TouchPad;

            if (n.Contains("keyboard")
                && Keys != null)
                return EvDevGuessedDeviceType.Keyboard;

            if (n.Contains("gamepad") || n.Contains("joystick")
                && Keys != null)
                return EvDevGuessedDeviceType.GamePad;
        }

        if (Keys?.Contains(EvDevKeyCode.BTN_TOUCH) == true
            && Properties.Contains(EvDevProperty.INPUT_PROP_DIRECT))
            return EvDevGuessedDeviceType.TouchScreen;

        if (Keys?.Contains(EvDevKeyCode.BTN_SOUTH) == true)
            return EvDevGuessedDeviceType.GamePad;

        if (Keys?.Contains(EvDevKeyCode.BTN_LEFT) == true && Keys?.Contains(EvDevKeyCode.BTN_RIGHT) == true)
        {
            if (AbsoluteAxises != null)
            {
                if (AbsoluteAxises?.ContainsKey(EvDevAbsoluteAxisCode.ABS_X) == true)
                {
                    if (Properties.Contains(EvDevProperty.INPUT_PROP_DIRECT))
                        return EvDevGuessedDeviceType.Tablet;
                    return EvDevGuessedDeviceType.TouchPad;
                }
            }

            if (RelativeAxises?.Contains(EvDevRelativeAxisCode.REL_X) == true &&
                RelativeAxises.Contains(EvDevRelativeAxisCode.REL_Y) == true)
                return EvDevGuessedDeviceType.Mouse;
        }

        if (Keys != null)
            return EvDevGuessedDeviceType.Keyboard;

        return EvDevGuessedDeviceType.Unknown;
    }

    /// <summary>
    /// This method enumerates all of the Linux event files and generates a <c>EvDevDevice</c> object for each file.
    /// </summary>
    /// <exception cref="System.PlatformNotSupportedException"></exception>
    /// <exception cref="System.ComponentModel.Win32Exception"></exception>
    public static IEnumerable<EvDevDevice> GetDevices()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && !RuntimeInformation.IsOSPlatform(OSPlatform.FreeBSD))
            throw new PlatformNotSupportedException();

        return Directory.GetFiles(InputPath, InputPathSearchPattern)
            .AsParallel()
            .Select(path => new EvDevDevice(path));
    }
}
