using System.Collections.Concurrent;

namespace ClipboardSaver.Tests;

internal static class Check
{
    public static void True(bool condition, string message = "Assertion failed")
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static void Equal<T>(T expected, T actual)
        => True(EqualityComparer<T>.Default.Equals(expected, actual), $"Expected {expected}; got {actual}.");

    public static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    public static async Task Eventually(Func<bool> condition, int milliseconds = 5000)
    {
        using var deadline = new CancellationTokenSource(milliseconds);
        while (!condition()) await Task.Delay(15, deadline.Token);
    }
}

internal sealed class TestRun
{
    public int Passed { get; private set; }
    public int Failed { get; private set; }
    public List<string> Results { get; } = [];

    public async Task Test(string name, Func<Task> test)
    {
        try
        {
            await test();
            Passed++;
            Results.Add($"PASS {name}");
        }
        catch (Exception ex)
        {
            Failed++;
            Results.Add($"FAIL {name}: {ex}");
        }
        Console.WriteLine(Results[^1]);
    }

    public Task Test(string name, Action test) => Test(name, () => { test(); return Task.CompletedTask; });
}

internal sealed class TemporaryDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"clipboard-tests-{Guid.NewGuid():N}");
    public TemporaryDirectory() => Directory.CreateDirectory(Path);
    public void Dispose() => Directory.Delete(Path, recursive: true);
}

internal sealed class TestSynchronizationContext : SynchronizationContext, IDisposable
{
    private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> work = new();
    public override void Post(SendOrPostCallback callback, object? state) => work.Add((callback, state));
    public void Run(Func<Task> action)
    {
        SetSynchronizationContext(this);
        Task task = action();
        while (!task.IsCompleted)
        {
            if (work.TryTake(out var item, 100)) item.Callback(item.State);
        }
        task.GetAwaiter().GetResult();
        SetSynchronizationContext(null);
    }
    public void Dispose() => work.Dispose();
}
