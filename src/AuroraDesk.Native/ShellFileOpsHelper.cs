using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace AuroraDesk.Native;

public static class ShellFileOpsHelper
{
    public static Task<bool> PinToQuickAccessAsync(string path)
    {
        var result = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var worker = new Thread(() =>
        {
            object? shell = null;
            object? folder = null;
            object? item = null;
            try
            {
                if (!Directory.Exists(path) || Type.GetTypeFromProgID("Shell.Application") is not Type shellType) return;
                shell = Activator.CreateInstance(shellType);
                if (shell == null) return;
                folder = ((dynamic)shell).NameSpace(Path.GetFullPath(path));
                if (folder == null) return;
                item = ((dynamic)folder).Self;
                if (item == null) return;
                ((dynamic)item).InvokeVerb("pintohome");
                result.TrySetResult(true);
            }
            catch (Exception) { }
            finally
            {
                result.TrySetResult(false);
                if (item != null && Marshal.IsComObject(item)) Marshal.ReleaseComObject(item);
                if (folder != null && Marshal.IsComObject(folder)) Marshal.ReleaseComObject(folder);
                if (shell != null && Marshal.IsComObject(shell)) Marshal.ReleaseComObject(shell);
            }
        }) { IsBackground = true };
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
        return result.Task;
    }

    private const uint FO_DELETE = 0x0003;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_SILENT = 0x0004;

    private const uint SEE_MASK_INVOKEIDLIST = 0x0000000C;
    private const int SW_SHOW = 5;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        [MarshalAs(UnmanagedType.LPWStr)] public string pFrom;
        [MarshalAs(UnmanagedType.LPWStr)] public string? pTo;
        public ushort fFlags;
        public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszProgressTitle;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHELLEXECUTEINFO
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpVerb;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpFile;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpParameters;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpDirectory;
        public int nShow;
        public IntPtr hInstApp;
        public IntPtr lpIDList;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpClass;
        public IntPtr hkeyClass;
        public uint dwHotKey;
        public IntPtr hIcon;
        public IntPtr hProcess;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SHQUERYRBINFO
    {
        public uint cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT FileOp);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO lpExecInfo);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBin(string? pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);

    /// <summary>
    /// Queries total item count and total size in bytes across all drives' Recycle Bins.
    /// </summary>
    public static (long ItemCount, long SizeBytes) QueryRecycleBin()
    {
        var info = new SHQUERYRBINFO { cbSize = (uint)Marshal.SizeOf<SHQUERYRBINFO>() };
        int hr = SHQueryRecycleBin(null, ref info);
        return hr == 0 ? (info.i64NumItems, info.i64Size) : (0, 0);
    }

    /// <summary>
    /// Invokes Windows native Empty Recycle Bin dialog (with native confirmation &amp; sound).
    /// </summary>
    public static bool EmptyRecycleBin(IntPtr ownerHwnd)
    {
        int hr = SHEmptyRecycleBin(ownerHwnd, null, 0);
        return hr == 0;
    }

    /// <summary>
    /// Safely moves a file or directory to the Windows Recycle Bin (undoable).
    /// </summary>
    public static bool MoveToRecycleBin(string path)
    {
        var op = new SHFILEOPSTRUCT
        {
            wFunc = FO_DELETE,
            pFrom = path + "\0\0",
            fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT
        };
        int res = SHFileOperation(ref op);
        return res == 0 && !op.fAnyOperationsAborted;
    }

    /// <summary>
    /// Opens the native Windows Properties dialog for a file, shortcut, or folder.
    /// </summary>
    public static void ShowProperties(string path)
    {
        var info = new SHELLEXECUTEINFO
        {
            cbSize = Marshal.SizeOf<SHELLEXECUTEINFO>(),
            lpVerb = "properties",
            lpFile = path,
            nShow = SW_SHOW,
            fMask = SEE_MASK_INVOKEIDLIST
        };
        ShellExecuteEx(ref info);
    }
}
