using System.Globalization;

namespace ClipboardSaver.Core;

public sealed class PngFileStore : IImageStore
{
    public string Save(IPngImage image, string directory, DateTimeOffset timestamp)
    {
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException("A pasta de destino não está disponível.");

        string temporary = Path.Combine(directory, $".clipboard-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                image.WritePng(stream);
                stream.Flush(flushToDisk: true);
            }

            string stem = timestamp.ToString("yyyy-MM-dd_HH-mm-ss-fff", CultureInfo.InvariantCulture);
            for (int suffix = 0; ; suffix++)
            {
                string name = suffix == 0 ? $"{stem}.png" : $"{stem}_{suffix}.png";
                string path = Path.Combine(directory, name);
                try
                {
                    File.Move(temporary, path, overwrite: false);
                    return path;
                }
                catch (IOException) when (File.Exists(path)) { }
            }
        }
        finally
        {
            // Cleanup failure must not hide the original storage error.
            try { File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
