using System;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using SqlBackup.App.Infrastructure;
using SqlBackup.Core;
using SqlBackup.Core.Models;
using SqlBackup.Core.Notifications;

namespace SqlBackup.App.ViewModels;

public sealed class SettingsViewModel : ObservableObject, IActivatable
{
    private readonly AppServices _services;
    private NotificationSettings _notifications = new();
    private string? _newSmtpPassword;
    private string _serviceStateText = "";
    private string _emailTestResultText = "";
    private string _webhookTestResultText = "";
    private string _saveStatusText = "";
    private string _configTransferStatusText = "";

    public SettingsViewModel(AppServices services)
    {
        _services = services;
        ServiceCommands = new ServiceCommands(services, () =>
        {
            RefreshServiceState();
            return Task.CompletedTask;
        });
        SaveCommand = new AsyncRelayCommand(_ => SaveAsync());
        SendTestEmailCommand = new AsyncRelayCommand(_ => SendTestEmailAsync());
        SendTestWebhookCommand = new AsyncRelayCommand(_ => SendTestWebhookAsync());
        RefreshServiceStateCommand = new RelayCommand(_ => RefreshServiceState());
        OpenDataFolderCommand = new RelayCommand(_ => OpenFolder(AppPaths.DataDir));
        OpenLogsFolderCommand = new RelayCommand(_ => OpenFolder(AppPaths.LogDir));
        ExportConfigCommand = new AsyncRelayCommand(_ => ExportConfigAsync());
        ImportConfigCommand = new AsyncRelayCommand(_ => ImportConfigAsync());
    }

    public ServiceCommands ServiceCommands { get; }
    public ICommand SaveCommand { get; }
    public ICommand SendTestEmailCommand { get; }
    public ICommand SendTestWebhookCommand { get; }
    public ICommand RefreshServiceStateCommand { get; }
    public ICommand OpenDataFolderCommand { get; }
    public ICommand OpenLogsFolderCommand { get; }
    public ICommand ExportConfigCommand { get; }
    public ICommand ImportConfigCommand { get; }

    // --- Notification settings (bound fields) ---

    public bool EmailEnabled
    {
        get => _notifications.EmailEnabled;
        set { _notifications.EmailEnabled = value; OnPropertyChanged(); }
    }

    public string SmtpHost
    {
        get => _notifications.SmtpHost;
        set { _notifications.SmtpHost = value; OnPropertyChanged(); }
    }

    public string SmtpPortText { get; set; } = "587";

    public bool UseTls
    {
        get => _notifications.UseTls;
        set { _notifications.UseTls = value; OnPropertyChanged(); }
    }

    public string SmtpUsername
    {
        get => _notifications.SmtpUsername ?? "";
        set { _notifications.SmtpUsername = value.Length == 0 ? null : value; OnPropertyChanged(); }
    }

    public string FromAddress
    {
        get => _notifications.FromAddress;
        set { _notifications.FromAddress = value; OnPropertyChanged(); }
    }

    public string ToAddresses
    {
        get => _notifications.ToAddresses;
        set { _notifications.ToAddresses = value; OnPropertyChanged(); }
    }

    public bool OnlyOnFailure
    {
        get => _notifications.OnlyOnFailure;
        set { _notifications.OnlyOnFailure = value; OnPropertyChanged(); }
    }

    public bool WebhookEnabled
    {
        get => _notifications.WebhookEnabled;
        set { _notifications.WebhookEnabled = value; OnPropertyChanged(); }
    }

    public string WebhookUrl
    {
        get => _notifications.WebhookUrl;
        set { _notifications.WebhookUrl = value; OnPropertyChanged(); }
    }

    public bool EventLogEnabled
    {
        get => _notifications.EventLogEnabled;
        set { _notifications.EventLogEnabled = value; OnPropertyChanged(); }
    }

    public string SmtpPasswordHint =>
        _notifications.ProtectedSmtpPassword is { Length: > 0 } ? "Leave blank to keep the stored password." : "";

    public string ServiceStateText
    {
        get => _serviceStateText;
        private set => Set(ref _serviceStateText, value);
    }

    public string EmailTestResultText
    {
        get => _emailTestResultText;
        private set => Set(ref _emailTestResultText, value);
    }

    public string WebhookTestResultText
    {
        get => _webhookTestResultText;
        private set => Set(ref _webhookTestResultText, value);
    }

    public string ConfigTransferStatusText
    {
        get => _configTransferStatusText;
        private set => Set(ref _configTransferStatusText, value);
    }

    public string SaveStatusText
    {
        get => _saveStatusText;
        private set => Set(ref _saveStatusText, value);
    }

    public string DataFolderText => AppPaths.DataDir;

    public void SetSmtpPassword(string password) => _newSmtpPassword = password.Length == 0 ? null : password;

    public void Activated()
    {
        _notifications = _services.ConfigStore.Load().Notifications;
        SmtpPortText = _notifications.SmtpPort.ToString(CultureInfo.InvariantCulture);
        RefreshAllBindings();
        RefreshServiceState();
        SaveStatusText = "";
        EmailTestResultText = "";
    }

    private void RefreshAllBindings()
    {
        OnPropertyChanged(nameof(EmailEnabled));
        OnPropertyChanged(nameof(SmtpHost));
        OnPropertyChanged(nameof(SmtpPortText));
        OnPropertyChanged(nameof(UseTls));
        OnPropertyChanged(nameof(SmtpUsername));
        OnPropertyChanged(nameof(FromAddress));
        OnPropertyChanged(nameof(ToAddresses));
        OnPropertyChanged(nameof(OnlyOnFailure));
        OnPropertyChanged(nameof(SmtpPasswordHint));
        OnPropertyChanged(nameof(WebhookEnabled));
        OnPropertyChanged(nameof(WebhookUrl));
        OnPropertyChanged(nameof(EventLogEnabled));
    }

    private void RefreshServiceState()
    {
        ServiceStateText = _services.ServiceManager.GetState() switch
        {
            ServiceInstallState.NotInstalled => "Not installed",
            ServiceInstallState.Running => "Running",
            ServiceInstallState.Stopped => "Stopped",
            _ => "State changing…",
        };
    }

    private bool ApplyFields(out string error)
    {
        error = "";
        if (!int.TryParse(SmtpPortText, out var port) || port < 1 || port > 65535)
        {
            error = "SMTP port must be a number between 1 and 65535.";
            return false;
        }
        _notifications.SmtpPort = port;
        if (_newSmtpPassword is not null)
            _notifications.ProtectedSmtpPassword = _services.Protector.Protect(_newSmtpPassword);
        return true;
    }

    private async Task SaveAsync()
    {
        if (!ApplyFields(out var error))
        {
            MessageBox.Show(error, "SQL Server Backup", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _services.ConfigStore.Update(c => c.Notifications = _notifications);
        var notified = await _services.TryNotifyServiceOfConfigChangeAsync();
        SaveStatusText = notified
            ? $"Saved {DateTime.Now:HH:mm:ss} — service reloaded."
            : $"Saved {DateTime.Now:HH:mm:ss} — service not reachable (it picks the change up automatically).";
        OnPropertyChanged(nameof(SmtpPasswordHint));
    }

    private async Task SendTestEmailAsync()
    {
        if (!ApplyFields(out var error))
        {
            EmailTestResultText = "✗ " + error;
            return;
        }
        EmailTestResultText = "Sending…";
        try
        {
            await EmailNotifier.SendTestAsync(_notifications, _services.Protector);
            EmailTestResultText = "✓ Test email sent.";
        }
        catch (Exception ex)
        {
            EmailTestResultText = "✗ " + ex.Message;
        }
    }

    private async Task SendTestWebhookAsync()
    {
        if (string.IsNullOrWhiteSpace(WebhookUrl))
        {
            WebhookTestResultText = "✗ Enter the webhook URL first.";
            return;
        }
        WebhookTestResultText = "Sending…";
        try
        {
            await Core.Notifications.WebhookNotifier.SendAsync(WebhookUrl,
                Core.Notifications.WebhookNotifier.BuildAlertPayload(
                    "test", $"SqlBackup test notification from {Environment.MachineName}", Array.Empty<string>()));
            WebhookTestResultText = "✓ Webhook accepted the test payload.";
        }
        catch (Exception ex)
        {
            WebhookTestResultText = "✗ " + ex.Message;
        }
    }

    private async Task ExportConfigAsync()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export configuration",
            FileName = $"sqlbackup-config-{DateTime.Now:yyyyMMdd}.json",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
        };
        if (dialog.ShowDialog() != true)
            return;

        var config = _services.ConfigStore.Load();
        await Task.Run(() => Core.Config.ConfigPorter.ExportToFile(config, dialog.FileName));
        ConfigTransferStatusText =
            $"Exported to {dialog.FileName}. Passwords/secrets are NOT included — they are machine-bound; re-enter them after importing.";
    }

    private async Task ImportConfigAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import configuration",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
        };
        if (dialog.ShowDialog() != true)
            return;

        var result = Core.Config.ConfigPorter.LoadFromFile(dialog.FileName);
        var current = _services.ConfigStore.Load();
        var summary =
            $"Replace the current configuration?\n\n" +
            $"Current: {current.Connections.Count} connection(s), {current.Jobs.Count} job(s), {current.OffsiteDestinations.Count} off-site destination(s).\n" +
            $"File:    {result.Config.Connections.Count} connection(s), {result.Config.Jobs.Count} job(s), {result.Config.OffsiteDestinations.Count} off-site destination(s)." +
            (result.Warnings.Count > 0 ? "\n\nAfter importing:\n• " + string.Join("\n• ", result.Warnings) : "");
        if (MessageBox.Show(summary, "Import configuration", MessageBoxButton.YesNo, MessageBoxImage.Warning)
            != MessageBoxResult.Yes)
            return;

        _services.ConfigStore.Save(result.Config);
        await _services.TryNotifyServiceOfConfigChangeAsync();
        Activated();
        ConfigTransferStatusText = "Imported. " +
            (result.Warnings.Count > 0
                ? $"{result.Warnings.Count} credential(s) must be re-entered — see Connections/Off-site/Settings."
                : "No credentials need re-entering.");
    }

    private static void OpenFolder(string path)
    {
        try
        {
            System.IO.Directory.CreateDirectory(path);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });
        }
        catch
        {
            // Explorer refused — nothing sensible to do.
        }
    }
}
