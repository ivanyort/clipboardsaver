using System.ComponentModel;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using ClipboardSaver.Core;

namespace ClipboardSaver.Windows;

internal static class NativeMethods
{
    internal const int WmClipboardUpdate = 0x031D;
    internal const uint CfBitmap = 2, CfDibV5 = 17, CfHDrop = 15;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AddClipboardFormatListener(nint hwnd);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RemoveClipboardFormatListener(nint hwnd);
    [DllImport("user32.dll")]
    internal static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool OpenClipboard(nint hwnd);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseClipboard();
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsClipboardFormatAvailable(uint format);
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nint GetClipboardData(uint format);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern uint RegisterClipboardFormat(string format);
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern nint GlobalLock(nint handle);
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GlobalUnlock(nint handle);
    [DllImport("kernel32.dll")]
    internal static extern nuint GlobalSize(nint handle);
}

internal sealed class ClipboardListener : NativeWindow, IDisposable
{
    public event Action? Updated;
    public ClipboardListener()
    {
        CreateHandle(new CreateParams { Caption = "ClipboardSaver.Listener", Parent = new nint(-3) });
        if (!NativeMethods.AddClipboardFormatListener(Handle))
        {
            var error = new Win32Exception(Marshal.GetLastWin32Error());
            DestroyHandle();
            throw error;
        }
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == NativeMethods.WmClipboardUpdate)
            Updated?.Invoke();
        base.WndProc(ref message);
    }

    public void Dispose()
    {
        if (Handle == nint.Zero) return;
        NativeMethods.RemoveClipboardFormatListener(Handle);
        DestroyHandle();
    }
}

internal sealed class WindowsClipboard(nint window) : IClipboardSource
{
    private readonly uint pngFormat = NativeMethods.RegisterClipboardFormat("PNG");
    public uint Sequence => NativeMethods.GetClipboardSequenceNumber();

    public ClipboardRead Read()
    {
        if (!NativeMethods.OpenClipboard(window))
            throw new ClipboardBusyException();
        try
        {
            IPngImage? image = null;
            nint bitmapHandle = 0;
            // Explorer file copies are deliberately outside the capture scope.
            if (!NativeMethods.IsClipboardFormatAvailable(NativeMethods.CfHDrop))
            {
                // The clipboard sequence also changes when OLE flushes an existing
                // image or another format is rendered. Its HBITMAP remains the same.
                // A new copy creates a new GDI object, even for identical pixels.
                if (NativeMethods.IsClipboardFormatAvailable(NativeMethods.CfBitmap))
                    bitmapHandle = NativeMethods.GetClipboardData(NativeMethods.CfBitmap);

                if (NativeMethods.IsClipboardFormatAvailable(pngFormat))
                    image = new EncodedPng(ReadBytes(pngFormat));
                else if (NativeMethods.IsClipboardFormatAvailable(NativeMethods.CfDibV5))
                    image = DibV5.TryRead(ReadBytes(NativeMethods.CfDibV5));

                if (image is null && NativeMethods.IsClipboardFormatAvailable(NativeMethods.CfBitmap))
                {
                    if (bitmapHandle == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
                    image = new BitmapPng(Image.FromHbitmap(bitmapHandle));
                }
            }
            return new ClipboardRead(Sequence, image, bitmapHandle == 0 ? null : (long)bitmapHandle);
        }
        finally { NativeMethods.CloseClipboard(); }
    }

    private static byte[] ReadBytes(uint format)
    {
        nint handle = NativeMethods.GetClipboardData(format);
        if (handle == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        int size = checked((int)NativeMethods.GlobalSize(handle));
        if (size == 0) throw new InvalidDataException("Imagem vazia no clipboard.");
        nint address = NativeMethods.GlobalLock(handle);
        if (address == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            byte[] data = new byte[size];
            Marshal.Copy(address, data, 0, size);
            return data;
        }
        finally { NativeMethods.GlobalUnlock(handle); }
    }
}

internal sealed class BitmapPng(Bitmap bitmap) : IPngImage
{
    public void WritePng(Stream destination) => bitmap.Save(destination, ImageFormat.Png);
    public void Dispose() => bitmap.Dispose();
}

internal sealed class EncodedPng(byte[] bytes) : IPngImage
{
    public void WritePng(Stream destination)
    {
        if (!bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            throw new InvalidDataException("O formato PNG do clipboard é inválido.");
        using var input = new MemoryStream(bytes, writable: false);
        using var image = Image.FromStream(input, useEmbeddedColorManagement: false, validateImageData: true);
        image.Save(destination, ImageFormat.Png);
    }
    public void Dispose() { }
}

internal static class DibV5
{
    // GDI's HBITMAP conversion discards alpha. Decode standard 32-bit DIBV5
    // directly when an explicit alpha mask exists; use GDI for other layouts.
    public static BitmapPng? TryRead(byte[] bytes)
    {
        if (bytes.Length < 124) return null;
        int header = BitConverter.ToInt32(bytes, 0);
        int width = BitConverter.ToInt32(bytes, 4);
        int signedHeight = BitConverter.ToInt32(bytes, 8);
        int compression = BitConverter.ToInt32(bytes, 16);
        if (header != 124 || width <= 0 || signedHeight == 0 || signedHeight == int.MinValue ||
            BitConverter.ToInt16(bytes, 12) != 1 || BitConverter.ToInt16(bytes, 14) != 32 ||
            compression != 3 || BitConverter.ToUInt32(bytes, 40) != 0x00ff0000 ||
            BitConverter.ToUInt32(bytes, 44) != 0x0000ff00 ||
            BitConverter.ToUInt32(bytes, 48) != 0x000000ff ||
            BitConverter.ToUInt32(bytes, 52) != 0xff000000)
            return null;

        int height = Math.Abs(signedHeight);
        int stride = checked(width * 4);
        int offset = checked(header + (int)BitConverter.ToUInt32(bytes, 32) * 4);
        if ((long)offset + (long)stride * height > bytes.Length)
            throw new InvalidDataException("Imagem DIBV5 incompleta.");
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        try
        {
            var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (int y = 0; y < height; y++)
                {
                    int row = signedHeight < 0 ? y : height - y - 1;
                    Marshal.Copy(bytes, checked(offset + row * stride), data.Scan0 + y * data.Stride, stride);
                }
            }
            finally { bitmap.UnlockBits(data); }
            return new BitmapPng(bitmap);
        }
        catch { bitmap.Dispose(); throw; }
    }
}
