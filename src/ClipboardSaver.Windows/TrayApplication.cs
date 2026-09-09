using System.Diagnostics;
using ClipboardSaver.Core;

namespace ClipboardSaver.Windows;

internal sealed class TrayApplication : ApplicationContext
{
    private readonly Control dispatcher = new();
    private readonly ClipboardListener listener;
    private readonly ClipboardCapture capture;
    private readonly SettingsStore settingsStore;
    private readonly AppLog log;
    private readonly NotifyIcon tray;
    private readonly Icon icon;
    private readonly ContextMenuStrip menu = new();
    private readonly ToolStripMenuItem status = new("Captura pausada") { Enabled = false };
    private readonly ToolStripMenuItem pause = new("Retomar captura");
    private readonly ToolStripMenuItem startup = new("Iniciar com o Windows");
    private readonly ToolStripMenuItem lastErrorItem = new("Ver último erro") { Enabled = false };
    private AppSettings settings = new();
    private string? lastError;
    private bool exiting;

    public TrayApplication(string dataDirectory, AppLog log)
    {
        this.log = log;
        settingsStore = new SettingsStore(dataDirectory);
        dispatcher.CreateControl();
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        listener = new ClipboardListener();
        capture = new ClipboardCapture(new WindowsClipboard(listener.Handle), new PngFileStore());
        listener.Updated += OnClipboardUpdated;
        capture.StateChanged += RefreshState;
        capture.Saved += _ => { status.Text = $"Última captura: {DateTime.Now:HH:mm:ss}"; };
        capture.Error += error => ReportError(error.Message, error.Exception);

        using var iconStream = typeof(TrayApplication).Assembly.GetManifestResourceStream("ClipboardSaver.Windows.Assets.clipboard.ico")
            ?? throw new InvalidOperationException("Ícone indisponível.");
        icon = new Icon(iconStream);
        menu.Items.Add(status);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Escolher pasta…", null, (_, _) => Guard(ChooseFolder));
        menu.Items.Add("Abrir pasta", null, (_, _) => Guard(OpenFolder));
        pause.Click += (_, _) => Guard(TogglePause);
        menu.Items.Add(pause);
        startup.Click += (_, _) => Guard(() =>
        {
            StartupRegistration.SetEnabled(!StartupRegistration.Enabled);
            startup.Checked = StartupRegistration.Enabled;
        });
        menu.Items.Add(startup);
        menu.Items.Add(new ToolStripSeparator());
        lastErrorItem.Click += (_, _) => MessageBox.Show(lastError, "Clipboard Saver", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        menu.Items.Add(lastErrorItem);
        menu.Items.Add("Sair", null, async (_, _) => await QuitAsync());
        tray = new NotifyIcon { Icon = icon, ContextMenuStrip = menu, Text = "Clipboard Saver — pausado", Visible = true };
        tray.DoubleClick += (_, _) => Guard(OpenFolder);
        dispatcher.BeginInvoke(Initialize);
    }

    private void Initialize()
    {
        Guard(() => { startup.Checked = StartupRegistration.Enabled; });
        try { settings = settingsStore.Load(); }
        catch (Exception ex)
        {
            ReportError("Não foi possível ler as configurações. Escolha novamente a pasta de destino.", ex);
        }

        if (settings.Destination is null)
            Guard(ChooseFolder);
        else
            Guard(() => Resume(settings.Destination));
    }

    private async void OnClipboardUpdated() => await capture.ClipboardChangedAsync();

    private void ChooseFolder()
    {
        bool wasActive = capture.Active;
        capture.Pause();
        try
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Escolha onde salvar as imagens do clipboard",
                UseDescriptionForTitle = true,
                SelectedPath = settings.Destination ?? Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                ShowNewFolderButton = true
            };
            if (dialog.ShowDialog() != DialogResult.OK)
                return;

            CheckDirectory(dialog.SelectedPath);
            var updated = new AppSettings(dialog.SelectedPath);
            settingsStore.Save(updated);
            settings = updated;
            wasActive = true;
        }
        finally
        {
            if (wasActive && settings.Destination is not null)
                Resume(settings.Destination);
        }
    }

    private static void CheckDirectory(string directory)
    {
        if (!Path.IsPathFullyQualified(directory) || !Directory.Exists(directory))
            throw new DirectoryNotFoundException("A pasta escolhida não está disponível.");
        string probe = Path.Combine(directory, $".clipboard-check-{Guid.NewGuid():N}.tmp");
        using var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
        stream.WriteByte(0);
        stream.Flush(flushToDisk: true);
    }

    private void Resume(string directory)
    {
        CheckDirectory(directory);
        capture.Resume(directory);
    }

    private void TogglePause()
    {
        if (capture.Active) capture.Pause();
        else if (settings.Destination is null) ChooseFolder();
        else Resume(settings.Destination);
    }

    private void OpenFolder()
    {
        if (settings.Destination is null) { ChooseFolder(); return; }
        if (!Directory.Exists(settings.Destination))
            throw new DirectoryNotFoundException("A pasta de destino não está disponível.");
        Process.Start(new ProcessStartInfo(settings.Destination) { UseShellExecute = true });
    }

    private void RefreshState()
    {
        pause.Text = capture.Active ? "Pausar captura" : "Retomar captura";
        status.Text = capture.Active ? "Captura ativa" : "Captura pausada";
        tray.Text = capture.Active ? "Clipboard Saver — captura ativa" : "Clipboard Saver — pausado";
    }

    private void Guard(Action action)
    {
        try { action(); }
        catch (Exception ex) { ReportError(ex.Message, ex); }
    }

    private void ReportError(string message, Exception? exception)
    {
        log.Write(message, exception);
        lastError = exception is null ? message : $"{message}\n\n{exception.Message}";
        lastErrorItem.Enabled = true;
        tray.ShowBalloonTip(5000, "Clipboard Saver", message, ToolTipIcon.Warning);
    }

    private async Task QuitAsync()
    {
        if (exiting) return;
        exiting = true;
        menu.Enabled = false;
        await capture.StopAsync();
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            listener.Updated -= OnClipboardUpdated;
            listener.Dispose();
            tray.Visible = false;
            tray.Dispose();
            menu.Dispose();
            icon.Dispose();
            dispatcher.Dispose();
        }
        base.Dispose(disposing);
    }
}
