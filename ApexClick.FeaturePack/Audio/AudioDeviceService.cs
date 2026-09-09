using System.Runtime.InteropServices;
using System.Text;

namespace ApexClick.FeaturePack.Audio;

public sealed record AudioInputDevice(string Id, string Name);

public sealed class AudioDeviceService
{
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern uint waveInGetNumDevs();

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern int waveInGetDevCapsW(UIntPtr deviceId, ref WaveInCaps caps, uint size);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WaveInCaps
    {
        public ushort ManufacturerId;
        public ushort ProductId;
        public uint DriverVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
        public uint Formats;
        public ushort Channels;
        public ushort Reserved1;
        public uint Support;
    }

    public IReadOnlyList<AudioInputDevice> GetInputDevices()
    {
        var count = waveInGetNumDevs();
        var result = new List<AudioInputDevice>((int)count);
        for (uint i = 0; i < count; i++)
        {
            var caps = new WaveInCaps { Name = string.Empty };
            if (waveInGetDevCapsW((UIntPtr)i, ref caps, (uint)Marshal.SizeOf<WaveInCaps>()) == 0)
                result.Add(new AudioInputDevice(i.ToString(), caps.Name?.Trim() ?? $"Microphone {i + 1}"));
        }
        return result;
    }
}
