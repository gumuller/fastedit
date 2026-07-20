using System.ComponentModel;
using System.Runtime.InteropServices;

namespace FastEdit.Tests;

internal static class WindowsFileSystemTestHelper
{
    public static void CreateHardLink(string linkPath, string existingPath)
    {
        if (!CreateHardLinkNative(linkPath, existingPath, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    [DllImport(
        "kernel32.dll",
        CharSet = CharSet.Unicode,
        EntryPoint = "CreateHardLinkW",
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkNative(
        string fileName,
        string existingFileName,
        IntPtr securityAttributes);
}
