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
    private string _saveStatusText = "";

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
        RefreshServiceStateCommand = new RelayCommand(_ => RefreshServiceState());
        OpenDataFolderCommand = new RelayCommand(_ => OpenFolder(AppPaths.DataDir));
        OpenLogsFolderCommand = new RelayCommand(_ => OpenFolder(AppPaths.LogDir));
    }

    public ServiceCommands ServiceCommands { get; }
    public ICommand SaveCommand { get; }
    public ICommand SendTestEmailCommand { get; }
    public ICommand RefreshServiceStateCommand { get; }
    public ICommand OpenDataFolderCommand { get; }
    public ICommand OpenLogsFolderCommand { get; }

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
