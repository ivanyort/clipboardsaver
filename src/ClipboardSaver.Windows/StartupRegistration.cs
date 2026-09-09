using Microsoft.Win32;

namespace ClipboardSaver.Windows;

internal static class StartupRegistration
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ClipboardSaver";

    public static bool Enabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
            return key?.GetValue(ValueName) is string;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true);
        if (enabled)
        {
            string executable = Environment.ProcessPath ?? throw new InvalidOperationException("Caminho do executável indisponível.");
            key.SetValue(ValueName, $"\"{executable}\"", RegistryValueKind.String);
        }
        else { key.DeleteValue(ValueName, throwOnMissingValue: false); }
    }
}
