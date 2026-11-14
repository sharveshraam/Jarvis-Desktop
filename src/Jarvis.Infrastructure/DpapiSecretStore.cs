using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Jarvis.Core;

namespace Jarvis.Infrastructure;

public sealed class DpapiSecretStore(string directory) : ISecretStore
{
    public Task<string?> GetAsync(string name, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = GetPath(name);
        if (!File.Exists(path)) return Task.FromResult<string?>(null);
        var encrypted = File.ReadAllBytes(path);
        var clear = NativeDpapi.Unprotect(encrypted);
        try { return Task.FromResult<string?>(Encoding.UTF8.GetString(clear)); }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }

    public Task SetAsync(string name, string value, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Directory.CreateDirectory(directory);
        var clear = Encoding.UTF8.GetBytes(value);
        try
        {
            var encrypted = NativeDpapi.Protect(clear);
            File.WriteAllBytes(GetPath(name), encrypted);
        }
        finally { CryptographicOperations.ZeroMemory(clear); }
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string name, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = GetPath(name);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    private string GetPath(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("Secret name is invalid.", nameof(name));
        return Path.Combine(directory, $"{name}.dpapi");
    }
}

internal static class NativeDpapi
{
    private const int CryptprotectUiForbidden = 0x1;

    public static byte[] Protect(byte[] input) => Transform(input, protect: true);
    public static byte[] Unprotect(byte[] input) => Transform(input, protect: false);

    private static byte[] Transform(byte[] input, bool protect)
    {
        var inputBuffer = Blob.From(input);
        DataBlob output = default;
        try
        {
            var success = protect
                ? CryptProtectData(ref inputBuffer, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptprotectUiForbidden, ref output)
                : CryptUnprotectData(ref inputBuffer, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptprotectUiForbidden, ref output);
            if (!success) throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not protect the Gemini API key.");
            var result = new byte[output.Length];
            Marshal.Copy(output.Data, result, 0, result.Length);
            return result;
        }
        finally
        {
            if (inputBuffer.Data != IntPtr.Zero) Marshal.FreeHGlobal(inputBuffer.Data);
            if (output.Data != IntPtr.Zero) LocalFree(output.Data);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob { public int Length; public IntPtr Data; }

    private static class Blob
    {
        public static DataBlob From(byte[] value)
        {
            var ptr = Marshal.AllocHGlobal(value.Length);
            Marshal.Copy(value, 0, ptr, value.Length);
            return new DataBlob { Length = value.Length, Data = ptr };
        }
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref DataBlob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, ref DataBlob output);

    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref DataBlob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, ref DataBlob output);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
