namespace ClipboardSaver.Core;

public interface IPngImage : IDisposable
{
    void WritePng(Stream destination);
}

// Optional identity of the native image object. Unlike the clipboard sequence,
// this survives metadata updates and delayed rendering of the same publication.
public sealed record ClipboardRead(uint Sequence, IPngImage? Image, long? ImageIdentity = null);

public interface IClipboardSource
{
    uint Sequence { get; }
    ClipboardRead Read();
}

public sealed class ClipboardBusyException : Exception
{
    public ClipboardBusyException() : base("A área de transferência está ocupada.") { }
}

public interface IImageStore
{
    string Save(IPngImage image, string directory, DateTimeOffset timestamp);
}

public sealed record CaptureError(string Message, Exception? Exception, bool Paused);

/// <summary>
/// All public methods and callbacks belong to the host's synchronization context (STA on Windows).
/// Only image encoding and file writes run on the thread pool.
/// </summary>
public sealed class ClipboardCapture(IClipboardSource source, IImageStore store)
{
    private sealed record Pending(IPngImage Image, string Directory, DateTimeOffset Timestamp);
    private readonly Queue<Pending> queue = new();
    private CancellationTokenSource? pendingRead;
    private Task writing = Task.CompletedTask;
    private uint lastObserved;
    private long? lastAcceptedImage;
    private bool draining;
    private string directory = "";
    private const int QueueLimit = 32;
    private static readonly int[] RetryDelays = [40, 80, 160, 320, 400];

    public bool Active { get; private set; }
    public event Action<string>? Saved;
    public event Action<CaptureError>? Error;
    public event Action? StateChanged;

    public void Resume(string destination)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        CancelRead();
        directory = destination;
        lastObserved = source.Sequence;
        lastAcceptedImage = null;
        Active = true;
        StateChanged?.Invoke();
    }

    public void Pause()
    {
        Active = false;
        CancelRead();
        StateChanged?.Invoke();
    }

    public Task ClipboardChangedAsync()
    {
        uint sequence = source.Sequence;
        if (!Active || sequence == lastObserved)
            return Task.CompletedTask;

        lastObserved = sequence;
        CancelRead();
        pendingRead = new CancellationTokenSource();
        return CaptureAsync(sequence, pendingRead.Token);
    }

    private async Task CaptureAsync(uint sequence, CancellationToken cancellation)
    {
        try
        {
            for (int attempt = 0; ; attempt++)
            {
                cancellation.ThrowIfCancellationRequested();
                if (source.Sequence != sequence)
                {
                    await ClipboardChangedAsync();
                    return;
                }

                ClipboardRead result;
                try { result = source.Read(); }
                catch (ClipboardBusyException) when (attempt < RetryDelays.Length)
                {
                    await Task.Delay(RetryDelays[attempt], cancellation);
                    continue;
                }

                // A delayed renderer may advance the sequence during a read. Re-read the
                // new version, so its subsequent notification cannot produce a duplicate.
                if (cancellation.IsCancellationRequested || result.Sequence != sequence)
                {
                    result.Image?.Dispose();
                    if (!cancellation.IsCancellationRequested)
                        await ClipboardChangedAsync();
                    return;
                }

                if (result.Image is null)
                {
                    lastAcceptedImage = null;
                    return;
                }
                if (result.ImageIdentity is not null && result.ImageIdentity == lastAcceptedImage)
                {
                    result.Image.Dispose();
                    return;
                }
                if (queue.Count >= QueueLimit)
                {
                    result.Image.Dispose();
                    Fail("Captura pausada: a fila de gravação atingiu o limite. Aguarde e retome pelo menu.", null);
                    return;
                }

                lastAcceptedImage = result.ImageIdentity;
                queue.Enqueue(new Pending(result.Image, directory, DateTimeOffset.Now));
                if (!draining)
                    writing = DrainAsync();
                return;
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Error?.Invoke(new CaptureError("Não foi possível ler esta imagem. A captura continua ativa; copie-a novamente.", ex, false));
        }
    }

    private async Task DrainAsync()
    {
        draining = true;
        try
        {
            while (queue.TryDequeue(out var item))
            {
                using (item.Image)
                {
                    string path;
                    try
                    {
                        path = await Task.Run(() => store.Save(item.Image, item.Directory, item.Timestamp));
                    }
                    catch (Exception ex)
                    {
                        Fail("Captura pausada: não foi possível salvar uma imagem. Verifique a pasta e retome pelo menu.", ex);
                        while (queue.TryDequeue(out var abandoned))
                            abandoned.Image.Dispose();
                        return;
                    }
                    Saved?.Invoke(path);
                }
            }
        }
        finally { draining = false; }
    }

    private void Fail(string message, Exception? exception)
    {
        Pause();
        Error?.Invoke(new CaptureError(message, exception, true));
    }

    public async Task StopAsync()
    {
        Pause();
        await writing;
    }

    private void CancelRead()
    {
        pendingRead?.Cancel();
        pendingRead?.Dispose();
        pendingRead = null;
    }
}
