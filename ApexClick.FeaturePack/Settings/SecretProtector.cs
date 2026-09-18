using System.Runtime.InteropServices;
using System.Text;

namespace ApexClick.FeaturePack.Settings;

internal static class SecretProtector
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Blob
    {
        public int Size;
        public IntPtr Data;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref Blob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out Blob output);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(ref Blob input, StringBuilder? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out Blob output);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);

    public static string Protect(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var bytes = Encoding.UTF8.GetBytes(value);
        var handle = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, handle, bytes.Length);
            var input = new Blob { Size = bytes.Length, Data = handle };
            if (!CryptProtectData(ref input, "ApexClick", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out var output))
                throw new InvalidOperationException($"CryptProtectData failed: {Marshal.GetLastWin32Error()}");
            try
            {
                var protectedBytes = new byte[output.Size];
                Marshal.Copy(output.Data, protectedBytes, 0, output.Size);
                return Convert.ToBase64String(protectedBytes);
            }
            finally { LocalFree(output.Data); }
        }
        finally { Marshal.FreeHGlobal(handle); }
    }

    public static string Unprotect(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        byte[] bytes;
        try { bytes = Convert.FromBase64String(value); }
        catch { return string.Empty; }
        var handle = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, handle, bytes.Length);
            var input = new Blob { Size = bytes.Length, Data = handle };
            if (!CryptUnprotectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out var output))
                return string.Empty;
            try
            {
                var plain = new byte[output.Size];
                Marshal.Copy(output.Data, plain, 0, output.Size);
                return Encoding.UTF8.GetString(plain);
            }
            finally { LocalFree(output.Data); }
        }
        finally { Marshal.FreeHGlobal(handle); }
    }
}
