using System.ComponentModel;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using ClipboardSaver.Core;
using ClipboardSaver.Tests;
using Microsoft.Win32;

namespace ClipboardSaver.Windows.Tests;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var run = new TestRun();
        try
        {
            if (args.FirstOrDefault() != "--isolated")
            {
                using var desktop = new IsolatedDesktop();
                int result = desktop.RunChild(args);
                if (args.Length == 2 && args[0] == "--results" && File.Exists(args[1]))
                    Console.WriteLine(File.ReadAllText(args[1]));
                return result;
            }
            args = args[1..];
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            using var dispatcher = new Control();
            dispatcher.CreateControl();
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            using var context = new ApplicationContext();
            dispatcher.BeginInvoke(async () =>
            {
                try { await RunTests(run); }
                catch (Exception ex) { await run.Test("Test harness", () => throw new InvalidOperationException("Harness failure", ex)); }
                finally { context.ExitThread(); }
            });
            Application.Run(context);
        }
        catch (Exception ex)
        {
            run.Test("Isolated Windows desktop setup", () => throw new InvalidOperationException("Setup failure", ex)).GetAwaiter().GetResult();
        }

        string report = string.Join(Environment.NewLine, run.Results) + $"{Environment.NewLine}{run.Passed} passed; {run.Failed} failed.";
        Console.WriteLine(report);
        if (args.Length == 2 && args[0] == "--results") File.WriteAllText(args[1], report);
        return run.Failed == 0 ? 0 : 1;
    }

    private static async Task RunTests(TestRun run)
    {
        await run.Test("Native clipboard notifications capture real bitmaps and ignore existing data", async () =>
        {
            using var fixture = new CaptureFixture();
            using var bitmap = MakeBitmap();
            Clipboard.SetImage(bitmap);
            fixture.Start();
            await Task.Delay(150);
            Check.Equal(0, fixture.Files.Length);
            Clipboard.SetImage(bitmap);
            await fixture.WaitForFiles(1);
            using var saved = new Bitmap(fixture.Files[0]);
            Check.Equal(bitmap.Size, saved.Size);
            Check.Equal(Color.CornflowerBlue.ToArgb(), saved.GetPixel(0, 0).ToArgb());
            Check.Equal(0, fixture.Errors.Count);
        });

        await run.Test("Copying an identical bitmap twice produces two files without event duplicates", async () =>
        {
            using var fixture = new CaptureFixture();
            fixture.Start();
            using var bitmap = MakeBitmap();
            Clipboard.SetImage(bitmap);
            await fixture.WaitForFiles(1);
            Clipboard.SetImage(bitmap);
            await fixture.WaitForFiles(2);
            await Task.Delay(150);
            Check.Equal(2, fixture.Files.Length);
            Check.True(File.ReadAllBytes(fixture.Files[0]).SequenceEqual(File.ReadAllBytes(fixture.Files[1])));
        });

        await run.Test("Adding clipboard metadata after an image does not represent another copy", async () =>
        {
            using var fixture = new CaptureFixture();
            fixture.Start();
            using var bitmap = MakeBitmap();
            Clipboard.SetImage(bitmap);
            await fixture.WaitForFiles(1);
            int notifications = fixture.Notifications;
            PutData(fixture.Handle, NativeMethods.RegisterClipboardFormat("ClipboardSaver.Test.Metadata"), [1, 0, 0, 0], empty: false);
            await Check.Eventually(() => fixture.Notifications > notifications);
            await fixture.Capture.StopAsync();
            Check.Equal(1, fixture.Files.Length);
        });

        await run.Test("Flushing a delayed OLE clipboard image does not represent another copy", async () =>
        {
            using var fixture = new CaptureFixture();
            fixture.Start();
            using var bitmap = MakeBitmap();
            Clipboard.SetDataObject(bitmap, copy: false);
            await fixture.WaitForFiles(1);
            Marshal.ThrowExceptionForHR(OleFlushClipboard());
            await Task.Delay(200);
            Check.Equal(1, fixture.Files.Length);
            Clipboard.SetDataObject(bitmap, copy: false);
            await fixture.WaitForFiles(2);
            Marshal.ThrowExceptionForHR(OleFlushClipboard());
            await Task.Delay(200);
            await fixture.Capture.StopAsync();
            Check.Equal(2, fixture.Files.Length);
        });

        await run.Test("Publishing PNG after the bitmap of the same screenshot does not save twice", async () =>
        {
            using var fixture = new CaptureFixture();
            fixture.Start();
            PutData(fixture.Handle, NativeMethods.CfDibV5, MakeDib(-2));
            await fixture.WaitForFiles(1);
            using var bitmap = new Bitmap(1, 2, PixelFormat.Format32bppArgb);
            bitmap.SetPixel(0, 0, Color.FromArgb(128, 200, 100, 50));
            bitmap.SetPixel(0, 1, Color.FromArgb(255, 20, 40, 30));
            using var png = new MemoryStream();
            bitmap.Save(png, ImageFormat.Png);
            int notifications = fixture.Notifications;
            PutData(fixture.Handle, NativeMethods.RegisterClipboardFormat("PNG"), png.ToArray(), empty: false);
            await Check.Eventually(() => fixture.Notifications > notifications);
            await fixture.Capture.StopAsync();
            Check.Equal(1, fixture.Files.Length);
        });

        await run.Test("Text and Explorer file-drop clipboard data do not create images", async () =>
        {
            using var fixture = new CaptureFixture();
            fixture.Start();
            Clipboard.SetText("Not an image");
            await Task.Delay(100);
            var files = new System.Collections.Specialized.StringCollection { @"C:\example.png" };
            Clipboard.SetFileDropList(files);
            await Task.Delay(100);
            Check.Equal(0, fixture.Files.Length);
            Check.Equal(0, fixture.Errors.Count);
        });

        await run.Test("PNG clipboard data preserves transparent and semitransparent pixels", async () =>
        {
            using var fixture = new CaptureFixture();
            fixture.Start();
            using var bitmap = new Bitmap(3, 2, PixelFormat.Format32bppArgb);
            bitmap.SetPixel(0, 0, Color.FromArgb(0, 0, 0, 0));
            bitmap.SetPixel(1, 0, Color.FromArgb(128, 200, 100, 50));
            bitmap.SetPixel(2, 0, Color.Red);
            using var png = new MemoryStream();
            bitmap.Save(png, ImageFormat.Png);
            PutData(fixture.Handle, NativeMethods.RegisterClipboardFormat("PNG"), png.ToArray());
            await fixture.WaitForFiles(1);
            using var saved = new Bitmap(fixture.Files[0]);
            Check.Equal(0, (int)saved.GetPixel(0, 0).A);
            Check.Equal(bitmap.GetPixel(1, 0).ToArgb(), saved.GetPixel(1, 0).ToArgb());
            Check.Equal(Color.Red.ToArgb(), saved.GetPixel(2, 0).ToArgb());
        });

        foreach (int height in new[] { 2, -2 })
        {
            await run.Test($"DIBV5 explicit alpha and row orientation (height {height})", async () =>
            {
                using var fixture = new CaptureFixture();
                fixture.Start();
                byte[] dib = MakeDib(height);
                PutData(fixture.Handle, NativeMethods.CfDibV5, dib);
                await fixture.WaitForFiles(1);
                using var saved = new Bitmap(fixture.Files[0]);
                int top = height > 0 ? 1 : 0;
                Check.Equal(128, (int)saved.GetPixel(0, top).A);
                Check.Equal(200, (int)saved.GetPixel(0, top).R);
                Check.Equal(255, (int)saved.GetPixel(0, 1 - top).A);
                Check.Equal(20, (int)saved.GetPixel(0, 1 - top).R);
            });
        }

        await run.Test("Pausing and resuming the real listener excludes prior clipboard contents", async () =>
        {
            using var fixture = new CaptureFixture();
            fixture.Start();
            fixture.Capture.Pause();
            using var bitmap = MakeBitmap();
            Clipboard.SetImage(bitmap);
            await Task.Delay(100);
            fixture.Start();
            await Task.Delay(100);
            Check.Equal(0, fixture.Files.Length);
            Clipboard.SetImage(bitmap);
            await fixture.WaitForFiles(1);
        });

        await run.Test("A real clipboard lock is retried until the clipboard is released", async () =>
        {
            using var fixture = new CaptureFixture();
            using var owner = new ClipboardListener();
            fixture.Start();
            using var bitmap = MakeBitmap();
            Clipboard.SetImage(bitmap);
            Check.True(NativeMethods.OpenClipboard(owner.Handle));
            try { await Task.Delay(180); Check.Equal(0, fixture.Files.Length); }
            finally { NativeMethods.CloseClipboard(); }
            await fixture.WaitForFiles(1);
            Check.Equal(0, fixture.Errors.Count);
        });

        await run.Test("Rapid clipboard changes save each image read by the listener", async () =>
        {
            using var fixture = new CaptureFixture();
            fixture.Start();
            using var bitmap = MakeBitmap();
            for (int i = 0; i < 8; i++)
            {
                bitmap.SetPixel(0, 0, Color.FromArgb(255, i * 20, 50, 100));
                int previousReads = fixture.Notifications;
                Clipboard.SetImage(bitmap);
                await Check.Eventually(() => fixture.Notifications > previousReads);
            }
            await fixture.WaitForFiles(8);
            Check.Equal(0, fixture.Errors.Count);
        });

        await run.Test("A missing destination pauses capture with a visible error event", async () =>
        {
            using var fixture = new CaptureFixture();
            string missing = Path.Combine(fixture.Directory, "missing");
            fixture.Capture.Resume(missing);
            using var bitmap = MakeBitmap();
            Clipboard.SetImage(bitmap);
            await Check.Eventually(() => fixture.Errors.Count > 0);
            Check.True(!fixture.Capture.Active && fixture.Errors[0].Paused);
            Check.True(!System.IO.Directory.Exists(missing));
        });

        await run.Test("Windows folder permissions reject writes and leave no partial PNG", async () =>
        {
            using var fixture = new CaptureFixture();
            var directory = new DirectoryInfo(fixture.Directory);
            var original = FileSystemAclExtensions.GetAccessControl(directory);
            var restricted = FileSystemAclExtensions.GetAccessControl(directory);
            var identity = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("No Windows user SID");
            restricted.AddAccessRule(new FileSystemAccessRule(identity, FileSystemRights.CreateFiles, AccessControlType.Deny));
            try
            {
                FileSystemAclExtensions.SetAccessControl(directory, restricted);
                fixture.Start();
                using var bitmap = MakeBitmap();
                Clipboard.SetImage(bitmap);
                await Check.Eventually(() => fixture.Errors.Count > 0);
                Check.True(!fixture.Capture.Active);
                Check.Equal(0, fixture.Files.Length);
            }
            finally { FileSystemAclExtensions.SetAccessControl(directory, original); }
        });

        await run.Test("Malformed PNG is rejected without leaving partial output", async () =>
        {
            using var fixture = new CaptureFixture();
            fixture.Start();
            PutData(fixture.Handle, NativeMethods.RegisterClipboardFormat("PNG"), [1, 2, 3, 4]);
            await Check.Eventually(() => fixture.Errors.Count > 0);
            Check.True(!fixture.Capture.Active);
            Check.Equal(0, System.IO.Directory.GetFiles(fixture.Directory).Length);
        });

        await run.Test("Windows settings round-trip a real absolute Unicode path", () =>
        {
            using var directory = new TemporaryDirectory();
            var store = new SettingsStore(directory.Path);
            var expected = new AppSettings(Path.Combine(directory.Path, "Imagens ação"));
            store.Save(expected);
            Check.Equal(expected, store.Load());
        });

        await run.Test("Native local mutex refuses a second instance", () =>
        {
            string name = @"Local\ClipboardSaver.Tests." + Guid.NewGuid().ToString("N");
            using var first = new Mutex(true, name, out bool created);
            try
            {
                Check.True(created);
                using var second = new Mutex(true, name, out bool duplicateCreated);
                Check.True(!duplicateCreated);
            }
            finally { first.ReleaseMutex(); }
        });

        await run.Test("Startup registration uses the quoted executable path and can be removed", () =>
        {
            // Redirect HKCU only inside this test process. The user's real Run key is untouched.
            string isolatedKey = @"Software\ClipboardSaver.Tests." + Guid.NewGuid().ToString("N");
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(isolatedKey);
                nint currentUser = new(unchecked((int)0x80000001));
                int error = RegOverridePredefKey(currentUser, key.Handle.DangerousGetHandle());
                if (error != 0) throw new Win32Exception(error);
                try
                {
                    Check.True(!StartupRegistration.Enabled);
                    StartupRegistration.SetEnabled(true);
                    Check.True(StartupRegistration.Enabled);
                    using var runKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
                    Check.Equal($"\"{Environment.ProcessPath}\"", runKey?.GetValue("ClipboardSaver") as string);
                    StartupRegistration.SetEnabled(false);
                    Check.True(!StartupRegistration.Enabled);
                }
                finally
                {
                    int restoreError = RegOverridePredefKey(currentUser, 0);
                    if (restoreError != 0) throw new Win32Exception(restoreError);
                }
            }
            finally { Registry.CurrentUser.DeleteSubKeyTree(isolatedKey, throwOnMissingSubKey: false); }
        });

        await run.Test("Tray application initializes from settings and saves using the production host", async () =>
        {
            using var root = new TemporaryDirectory();
            string output = Path.Combine(root.Path, "images");
            System.IO.Directory.CreateDirectory(output);
            new SettingsStore(root.Path).Save(new AppSettings(output));
            using var app = new TrayApplication(root.Path, new AppLog(root.Path));
            await Task.Delay(150);
            using var bitmap = MakeBitmap();
            Clipboard.SetImage(bitmap);
            await Check.Eventually(() => System.IO.Directory.GetFiles(output, "*.png").Length == 1);
            // Let the production writer return to the UI context before disposing the host.
            await Task.Delay(100);
        });
    }

    private static Bitmap MakeBitmap()
    {
        var bitmap = new Bitmap(24, 12);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.CornflowerBlue);
        return bitmap;
    }


    private static byte[] MakeDib(int height)
    {
        byte[] bytes = new byte[124 + 8];
        BitConverter.GetBytes(124).CopyTo(bytes, 0);
        BitConverter.GetBytes(1).CopyTo(bytes, 4);
        BitConverter.GetBytes(height).CopyTo(bytes, 8);
        BitConverter.GetBytes((short)1).CopyTo(bytes, 12);
        BitConverter.GetBytes((short)32).CopyTo(bytes, 14);
        BitConverter.GetBytes(3).CopyTo(bytes, 16);
        BitConverter.GetBytes(8).CopyTo(bytes, 20);
        BitConverter.GetBytes(0x00ff0000u).CopyTo(bytes, 40);
        BitConverter.GetBytes(0x0000ff00u).CopyTo(bytes, 44);
        BitConverter.GetBytes(0x000000ffu).CopyTo(bytes, 48);
        BitConverter.GetBytes(0xff000000u).CopyTo(bytes, 52);
        new byte[] { 50, 100, 200, 128, 30, 40, 20, 255 }.CopyTo(bytes, 124);
        return bytes;
    }

    private static void PutData(nint window, uint format, byte[] bytes, bool empty = true)
    {
        if (!NativeMethods.OpenClipboard(window)) throw new Win32Exception(Marshal.GetLastWin32Error());
        nint memory = 0;
        try
        {
            if (empty) Check.True(EmptyClipboard());
            memory = GlobalAlloc(0x0042, (nuint)bytes.Length);
            if (memory == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            nint address = NativeMethods.GlobalLock(memory);
            if (address == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            try { Marshal.Copy(bytes, 0, address, bytes.Length); }
            finally { NativeMethods.GlobalUnlock(memory); }
            if (SetClipboardData(format, memory) == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            memory = 0; // Clipboard now owns this allocation.
        }
        finally
        {
            if (memory != 0) GlobalFree(memory);
            NativeMethods.CloseClipboard();
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyClipboard();
    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetClipboardData(uint format, nint memory);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GlobalAlloc(uint flags, nuint size);
    [DllImport("kernel32.dll")]
    private static extern nint GlobalFree(nint memory);
    [DllImport("advapi32.dll")]
    private static extern int RegOverridePredefKey(nint predefined, nint replacement);
    [DllImport("ole32.dll")]
    private static extern int OleFlushClipboard();
}

internal sealed class CaptureFixture : IDisposable
{
    private readonly TemporaryDirectory temporary = new();
    private readonly ClipboardListener listener = new();
    public ClipboardCapture Capture { get; }
    public List<CaptureError> Errors { get; } = [];
    public string Directory => temporary.Path;
    public string[] Files => System.IO.Directory.GetFiles(Directory, "*.png");
    public nint Handle => listener.Handle;
    public int Notifications { get; private set; }
    private int completed;

    public CaptureFixture()
    {
        Capture = new ClipboardCapture(new WindowsClipboard(listener.Handle), new PngFileStore());
        listener.Updated += OnUpdated;
        Capture.Error += error => Errors.Add(error);
        Capture.Saved += _ => completed++;
    }

    private async void OnUpdated()
    {
        await Capture.ClipboardChangedAsync();
        Notifications++;
    }

    public void Start() => Capture.Resume(Directory);
    public async Task WaitForFiles(int count)
    {
        await Check.Eventually(() => completed >= count || Errors.Count > 0);
        Check.True(Errors.Count == 0, string.Join("; ", Errors));
        Check.Equal(count, Files.Length);
    }

    public void Dispose()
    {
        Capture.Pause();
        listener.Updated -= OnUpdated;
        listener.Dispose();
        temporary.Dispose();
    }
}
