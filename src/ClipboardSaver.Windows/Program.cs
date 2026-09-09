namespace ClipboardSaver.Windows;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        using var instance = new Mutex(true, @"Local\ClipboardSaver.Application", out bool created);
        if (!created)
        {
            MessageBox.Show("O Clipboard Saver já está aberto. Procure o ícone na bandeja do Windows.",
                "Clipboard Saver", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        string dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClipboardSaver");
        var log = new AppLog(dataDirectory);
        try
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            using var app = new TrayApplication(dataDirectory, log);
            Application.Run(app);
        }
        catch (Exception ex)
        {
            log.Write("Falha inesperada", ex);
            MessageBox.Show($"Não foi possível continuar a execução.\n\n{ex.Message}\n\nLogs: {dataDirectory}",
                "Clipboard Saver", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { instance.ReleaseMutex(); }
    }
}
