using System.Collections.Concurrent;
using ClipboardSaver.Core;
using ClipboardSaver.Tests;

var run = new TestRun();
using var context = new TestSynchronizationContext();
context.Run(async () =>
{
    await run.Test("One native image is saved once across sequences, even while its first write is pending", async () =>
    {
        using var blocked = new ManualResetEventSlim(false);
        var source = new FakeClipboard { ImageIdentity = 10 };
        var store = new RecordingStore { Block = blocked };
        var capture = new ClipboardCapture(source, store);
        capture.Resume("destination");
        source.Sequence++;
        await capture.ClipboardChangedAsync();
        source.Sequence++;
        await capture.ClipboardChangedAsync();
        // A deliberate copy has a new native object, even with identical pixels.
        source.ImageIdentity = 11;
        source.Sequence++;
        await capture.ClipboardChangedAsync();
        blocked.Set();
        await capture.StopAsync();
        Check.Equal(2, store.Saved.Count);
        Check.True(source.Images.All(image => image.Disposed));
    });

    await run.Test("An unstable read does not consume the image identity before it can be saved", async () =>
    {
        var source = new FakeClipboard { ImageIdentity = 10, AdvanceDuringRead = true };
        var store = new RecordingStore();
        var capture = new ClipboardCapture(source, store);
        capture.Resume("destination");
        source.Sequence++;
        await capture.ClipboardChangedAsync();
        await capture.StopAsync();
        Check.Equal(1, store.Saved.Count);
        Check.True(source.Images.All(image => image.Disposed));
    });

    await run.Test("Initial clipboard and duplicate events are ignored; equal content copied again is saved", async () =>
    {
        var source = new FakeClipboard();
        var store = new RecordingStore();
        var capture = new ClipboardCapture(source, store);
        capture.Resume("destination");
        await capture.ClipboardChangedAsync();
        Check.Equal(0, source.Reads);
        source.Sequence++;
        await capture.ClipboardChangedAsync();
        await capture.ClipboardChangedAsync();
        source.Sequence++;
        await capture.ClipboardChangedAsync();
        await capture.StopAsync();
        Check.Equal(2, source.Reads);
        Check.Equal(2, store.Saved.Count);
        Check.True(source.Images.All(image => image.Disposed));
    });

    await run.Test("Pause and resume exclude images copied while paused", async () =>
    {
        var source = new FakeClipboard();
        var store = new RecordingStore();
        var capture = new ClipboardCapture(source, store);
        capture.Resume("destination");
        capture.Pause();
        source.Sequence++;
        await capture.ClipboardChangedAsync();
        capture.Resume("destination");
        await capture.ClipboardChangedAsync();
        Check.Equal(0, source.Reads);
        source.Sequence++;
        await capture.ClipboardChangedAsync();
        await capture.StopAsync();
        Check.Equal(1, store.Saved.Count);
    });

    await run.Test("Non-image clipboard data is ignored", async () =>
    {
        var source = new FakeClipboard { HasImage = false };
        var store = new RecordingStore();
        var capture = new ClipboardCapture(source, store);
        capture.Resume("destination");
        source.Sequence++;
        await capture.ClipboardChangedAsync();
        await capture.StopAsync();
        Check.Equal(0, store.Saved.Count);
    });

    await run.Test("Busy clipboard is retried without duplicating the event", async () =>
    {
        var source = new FakeClipboard { BusyReads = 2 };
        var store = new RecordingStore();
        var capture = new ClipboardCapture(source, store);
        capture.Resume("destination");
        source.Sequence++;
        await capture.ClipboardChangedAsync();
        await capture.StopAsync();
        Check.Equal(3, source.Reads);
        Check.Equal(1, store.Saved.Count);
    });

    await run.Test("A newer copy cancels an old busy retry", async () =>
    {
        var source = new FakeClipboard { BusyReads = 1 };
        var store = new RecordingStore();
        var capture = new ClipboardCapture(source, store);
        capture.Resume("destination");
        source.Sequence++;
        var old = capture.ClipboardChangedAsync();
        source.Sequence++;
        await capture.ClipboardChangedAsync();
        await old;
        await capture.StopAsync();
        Check.Equal(1, store.Saved.Count);
    });

    await run.Test("Pausing cancels outstanding clipboard retries", async () =>
    {
        var source = new FakeClipboard { BusyReads = 1 };
        var store = new RecordingStore();
        var capture = new ClipboardCapture(source, store);
        capture.Resume("destination");
        source.Sequence++;
        var pending = capture.ClipboardChangedAsync();
        capture.Pause();
        await pending;
        await capture.StopAsync();
        Check.Equal(0, store.Saved.Count);
    });

    await run.Test("Exhausted read retries report the loss and monitoring remains active", async () =>
    {
        var source = new FakeClipboard { BusyReads = 20 };
        var store = new RecordingStore();
        var capture = new ClipboardCapture(source, store);
        CaptureError? error = null;
        capture.Error += value => error = value;
        capture.Resume("destination");
        source.Sequence++;
        await capture.ClipboardChangedAsync();
        Check.True(capture.Active && error is { Paused: false });
        source.BusyReads = 0;
        source.Sequence++;
        await capture.ClipboardChangedAsync();
        await capture.StopAsync();
        Check.Equal(1, store.Saved.Count);
    });

    await run.Test("Delayed rendering advances the sequence and is saved only once", async () =>
    {
        var source = new FakeClipboard { AdvanceDuringRead = true };
        var store = new RecordingStore();
        var capture = new ClipboardCapture(source, store);
        capture.Resume("destination");
        source.Sequence++;
        await capture.ClipboardChangedAsync();
        await capture.ClipboardChangedAsync();
        await capture.StopAsync();
        Check.Equal(1, store.Saved.Count);
        Check.True(source.Images.All(image => image.Disposed));
    });

    await run.Test("Sequence counter wraparound still captures a new copy", async () =>
    {
        var source = new FakeClipboard { Sequence = uint.MaxValue };
        var store = new RecordingStore();
        var capture = new ClipboardCapture(source, store);
        capture.Resume("destination");
        source.Sequence = 0;
        await capture.ClipboardChangedAsync();
        await capture.StopAsync();
        Check.Equal(1, store.Saved.Count);
    });

    await run.Test("Writes are serialized and keep the destination selected at capture time", async () =>
    {
        using var blocked = new ManualResetEventSlim(false);
        var source = new FakeClipboard();
        var store = new RecordingStore { Block = blocked };
        var capture = new ClipboardCapture(source, store);
        capture.Resume("first");
        source.Sequence++;
        await capture.ClipboardChangedAsync();
        capture.Resume("second");
        source.Sequence++;
        await capture.ClipboardChangedAsync();
        blocked.Set();
        await capture.StopAsync();
        Check.True(store.Saved.ToArray().SequenceEqual(new[] { "first", "second" }));
        Check.Equal(1, store.MaxConcurrent);
    });

    await run.Test("Storage failure pauses capture and disposes queued images", async () =>
    {
        using var blocked = new ManualResetEventSlim(false);
        var source = new FakeClipboard();
        var store = new RecordingStore { Fail = true, Block = blocked };
        var capture = new ClipboardCapture(source, store);
        CaptureError? error = null;
        capture.Error += value => error = value;
        capture.Resume("destination");
        for (int i = 0; i < 3; i++)
        {
            source.Sequence++;
            await capture.ClipboardChangedAsync();
        }
        blocked.Set();
        await Check.Eventually(() => error is not null);
        Check.True(!capture.Active && error is { Paused: true });
        await capture.StopAsync();
        Check.True(source.Images.All(image => image.Disposed));
    });

    await run.Test("Queue overflow is reported, pauses new captures, and drains accepted images", async () =>
    {
        using var blocked = new ManualResetEventSlim(false);
        var source = new FakeClipboard();
        var store = new RecordingStore { Block = blocked };
        var capture = new ClipboardCapture(source, store);
        CaptureError? error = null;
        capture.Error += value => error = value;
        capture.Resume("destination");
        for (int i = 0; i < 34; i++)
        {
            source.Sequence++;
            await capture.ClipboardChangedAsync();
        }
        Check.True(!capture.Active && error is { Paused: true });
        blocked.Set();
        await capture.StopAsync();
        Check.Equal(33, store.Saved.Count);
        Check.True(source.Images.All(image => image.Disposed));
    });

    await run.Test("Atomic PNG writes never overwrite timestamp collisions", () =>
    {
        using var directory = new TemporaryDirectory();
        var store = new PngFileStore();
        var timestamp = new DateTimeOffset(2026, 9, 9, 14, 35, 22, 123, TimeSpan.FromHours(-3));
        string first = store.Save(new FakeImage(), directory.Path, timestamp);
        string second = store.Save(new FakeImage(), directory.Path, timestamp);
        Check.Equal("2026-09-09_14-35-22-123.png", Path.GetFileName(first));
        Check.Equal("2026-09-09_14-35-22-123_1.png", Path.GetFileName(second));
        Check.True(File.ReadAllBytes(first).SequenceEqual(new byte[] { 1, 2, 3 }));
        Check.Equal(2, Directory.GetFiles(directory.Path).Length);
    });

    await run.Test("Encoding failures remove partial files", () =>
    {
        using var directory = new TemporaryDirectory();
        Check.Throws<IOException>(() => new PngFileStore().Save(new FakeImage { Fail = true }, directory.Path, DateTimeOffset.Now));
        Check.Equal(0, Directory.GetFiles(directory.Path).Length);
    });

    await run.Test("Unavailable output folder is not silently recreated", () =>
    {
        using var directory = new TemporaryDirectory();
        string missing = Path.Combine(directory.Path, "missing");
        Check.Throws<DirectoryNotFoundException>(() => new PngFileStore().Save(new FakeImage(), missing, DateTimeOffset.Now));
        Check.True(!Directory.Exists(missing));
    });

    await run.Test("Settings persist Unicode paths and reject damaged or relative configurations", () =>
    {
        using var directory = new TemporaryDirectory();
        var store = new SettingsStore(directory.Path);
        Check.Equal(new AppSettings(), store.Load());
        var settings = new AppSettings(Path.Combine(directory.Path, "Imagens ação"));
        store.Save(settings);
        Check.Equal(settings, store.Load());
        Check.Equal(1, Directory.GetFiles(directory.Path).Length);
        File.WriteAllText(store.FilePath, "broken");
        Check.Throws<System.Text.Json.JsonException>(() => store.Load());
        File.WriteAllText(store.FilePath, "{\"Destination\":\"relative\"}");
        Check.Throws<InvalidDataException>(() => store.Load());
    });
});
Console.WriteLine($"{run.Passed} passed; {run.Failed} failed.");
return run.Failed == 0 ? 0 : 1;

sealed class FakeImage : IPngImage
{
    public bool Disposed { get; private set; }
    public bool Fail { get; init; }
    public void WritePng(Stream destination)
    {
        destination.Write(new byte[] { 1, 2, 3 });
        if (Fail) throw new IOException("Test encoding failure");
    }
    public void Dispose() => Disposed = true;
}

sealed class FakeClipboard : IClipboardSource
{
    public uint Sequence { get; set; } = 1;
    public long? ImageIdentity { get; set; }
    public int Reads { get; private set; }
    public int BusyReads { get; set; }
    public bool HasImage { get; init; } = true;
    public bool AdvanceDuringRead { get; set; }
    public List<FakeImage> Images { get; } = [];
    public ClipboardRead Read()
    {
        Reads++;
        if (BusyReads-- > 0) throw new ClipboardBusyException();
        if (AdvanceDuringRead) { Sequence++; AdvanceDuringRead = false; }
        if (!HasImage) return new ClipboardRead(Sequence, null);
        var image = new FakeImage();
        Images.Add(image);
        return new ClipboardRead(Sequence, image, ImageIdentity);
    }
}

sealed class RecordingStore : IImageStore
{
    public ConcurrentQueue<string> Saved { get; } = new();
    public bool Fail { get; init; }
    public ManualResetEventSlim? Block { get; init; }
    public int MaxConcurrent { get; private set; }
    private int concurrent;
    public string Save(IPngImage image, string directory, DateTimeOffset timestamp)
    {
        int count = Interlocked.Increment(ref concurrent);
        MaxConcurrent = Math.Max(count, MaxConcurrent);
        try
        {
            if (Block is not null && !Block.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
            if (Fail) throw new IOException("Test storage failure");
            Saved.Enqueue(directory);
            return directory;
        }
        finally { Interlocked.Decrement(ref concurrent); }
    }
}
