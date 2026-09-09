using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace ClipboardSaver.Windows.Tests;

/// <summary>Real Win32 clipboard tests, without reading or replacing the user's clipboard.</summary>
internal sealed class IsolatedDesktop : IDisposable
{
    private readonly nint originalStation = GetProcessWindowStation();
    private readonly string stationName = $"ClipboardSaverTests-{Guid.NewGuid():N}";
    private readonly nint station;
    private readonly nint desktop;

    public IsolatedDesktop()
    {
        station = CreateWindowStation(stationName, 0, 0x37f, 0);
        if (station == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (!SetProcessWindowStation(station)) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            desktop = CreateDesktop("TestDesktop", 0, 0, 0, 0x1ff, 0);
            if (desktop == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally { SetProcessWindowStation(originalStation); }
    }

    public int RunChild(string[] args)
    {
        // Assign the desktop before the CLR initializes STA/OLE and creates hidden windows.
        string executable = Environment.ProcessPath!;
        var command = new StringBuilder($"\"{executable}\" --isolated " + string.Join(" ", args.Select(value => $"\"{value}\"")));
        var startup = new StartupInfo { Size = Marshal.SizeOf<StartupInfo>(), Desktop = $"{stationName}\\TestDesktop" };
        if (!CreateProcess(executable, command, 0, 0, false, 0x08000000, 0, null, ref startup, out var process))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            if (WaitForSingleObject(process.Process, 90000) != 0)
            {
                TerminateProcess(process.Process, 1);
                throw new TimeoutException("Isolated Windows tests exceeded 90 seconds.");
            }
            if (!GetExitCodeProcess(process.Process, out uint code))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            return (int)code;
        }
        finally { CloseHandle(process.Thread); CloseHandle(process.Process); }
    }

    public void Dispose()
    {
        CloseDesktop(desktop);
        CloseWindowStation(station);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowStation(string name, uint flags, uint access, nint security);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateDesktop(string name, nint device, nint deviceMode, uint flags, uint access, nint security);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessWindowStation(nint station);
    [DllImport("user32.dll")]
    private static extern nint GetProcessWindowStation();
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseWindowStation(nint station);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseDesktop(nint desktop);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int Size;
        public string? Reserved, Desktop, Title;
        public int X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute, Flags;
        public short ShowWindow, Reserved2;
        public nint ReservedPointer, StdInput, StdOutput, StdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public nint Process, Thread;
        public uint ProcessId, ThreadId;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcess(string application, StringBuilder command, nint processSecurity,
        nint threadSecurity, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint flags, nint environment,
        string? directory, ref StartupInfo startup, out ProcessInformation process);
    [DllImport("kernel32.dll")]
    private static extern uint WaitForSingleObject(nint handle, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetExitCodeProcess(nint handle, out uint code);
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateProcess(nint handle, uint code);
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);
}
