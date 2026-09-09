using System.Text.Json;

namespace ClipboardSaver.Core;

public sealed record AppSettings(string? Destination = null);

public sealed class SettingsStore(string directory)
{
    public string FilePath { get; } = Path.Combine(directory, "settings.json");

    public AppSettings Load()
    {
        if (!File.Exists(FilePath))
            return new AppSettings();
        using var stream = File.OpenRead(FilePath);
        var settings = JsonSerializer.Deserialize<AppSettings>(stream)
            ?? throw new InvalidDataException("O arquivo de configurações está vazio.");
        if (settings.Destination is not null && !Path.IsPathFullyQualified(settings.Destination))
            throw new InvalidDataException("A pasta configurada precisa ter um caminho absoluto.");
        return settings;
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, $"settings-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, settings, new JsonSerializerOptions { WriteIndented = true });
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, FilePath, overwrite: true);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
