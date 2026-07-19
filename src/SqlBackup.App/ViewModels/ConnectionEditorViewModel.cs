using System;
using System.Threading.Tasks;
using System.Windows.Input;
using SqlBackup.App.Infrastructure;
using SqlBackup.Core.Backup;
using SqlBackup.Core.Models;
using SqlBackup.Core.Security;

namespace SqlBackup.App.ViewModels;

public sealed class ConnectionEditorViewModel : ObservableObject
{
    private readonly ISecretProtector _protector;
    private string? _newPassword;
    private string _testResultText = "";
    private string _provisionResultText = "";

    public ConnectionEditorViewModel(ConnectionProfile working, ISecretProtector protector, bool isNew)
    {
        Working = working;
        _protector = protector;
        IsNew = isNew;
        TestConnectionCommand = new AsyncRelayCommand(_ => TestConnectionAsync());
        CreateBackupLoginCommand = new AsyncRelayCommand(_ => CreateBackupLoginAsync());
    }

    public ConnectionProfile Working { get; }
    public bool IsNew { get; }

    public string Name
    {
        get => Working.Name;
        set { Working.Name = value; OnPropertyChanged(); }
    }

    public string Server
    {
        get => Working.Server;
        set { Working.Server = value; OnPropertyChanged(); }
    }

    public bool IsWindowsAuth
    {
        get => Working.AuthMode == SqlAuthMode.Windows;
        set
        {
            Working.AuthMode = value ? SqlAuthMode.Windows : SqlAuthMode.Sql;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSqlAuth));
        }
    }

    public bool IsSqlAuth
    {
        get => Working.AuthMode == SqlAuthMode.Sql;
        set => IsWindowsAuth = !value;
    }

    public string Username
    {
        get => Working.Username ?? "";
        set { Working.Username = value; OnPropertyChanged(); }
    }

    public bool Encrypt
    {
        get => Working.Encrypt;
        set { Working.Encrypt = value; OnPropertyChanged(); }
    }

    public bool TrustServerCertificate
    {
        get => Working.TrustServerCertificate;
        set { Working.TrustServerCertificate = value; OnPropertyChanged(); }
    }

    public bool HasStoredPassword => Working.ProtectedPassword is { Length: > 0 };

    public string PasswordHint => HasStoredPassword ? "Leave blank to keep the stored password." : "";

    public string TestResultText
    {
        get => _testResultText;
        private set => Set(ref _testResultText, value);
    }

    public string ProvisionResultText
    {
        get => _provisionResultText;
        private set => Set(ref _provisionResultText, value);
    }

    public ICommand TestConnectionCommand { get; }
    public ICommand CreateBackupLoginCommand { get; }

    /// <summary>Called from the dialog's PasswordBox (PasswordBox does not support binding).</summary>
    public void SetPassword(string password) => _newPassword = password.Length == 0 ? null : password;

    public bool TryCommit(out string error)
    {
        error = "";
        if (string.IsNullOrWhiteSpace(Name))
            error = "Enter a display name.";
        else if (string.IsNullOrWhiteSpace(Server))
            error = @"Enter a server address (e.g. localhost, HOST\SQLEXPRESS or host,1433).";
        else if (IsSqlAuth && string.IsNullOrWhiteSpace(Username))
            error = "SQL authentication needs a login name.";
        else if (IsSqlAuth && _newPassword is null && !HasStoredPassword)
            error = "Enter the password for the SQL login.";
        if (error.Length > 0)
            return false;

        if (IsSqlAuth && _newPassword is not null)
            Working.ProtectedPassword = _protector.Protect(_newPassword);
        if (!IsSqlAuth)
        {
            Working.Username = null;
            Working.ProtectedPassword = null;
        }
        return true;
    }

    private async Task TestConnectionAsync()
    {
        TestResultText = "Connecting…";
        try
        {
            var info = await SqlServerQueries.TestConnectionAsync(BuildCurrentConnectionString());
            TestResultText = $"✓ Connected to {info.ServerName} — {info.Edition}, version {info.ProductVersion}";
        }
        catch (Exception ex)
        {
            TestResultText = "✗ " + ex.Message;
        }
    }

    /// <summary>
    /// Provisions (or reuses + re-keys) the dedicated iSQLBackup_* SQL login using the
    /// credentials currently entered above as the administrative connection, then switches
    /// this profile to SQL authentication with the generated credential.
    /// </summary>
    private async Task CreateBackupLoginAsync()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            ProvisionResultText = "✗ Enter the display name first — it forms the login name prefix.";
            return;
        }
        if (string.IsNullOrWhiteSpace(Server))
        {
            ProvisionResultText = "✗ Enter the server address first.";
            return;
        }

        ProvisionResultText = "Creating the backup login…";
        try
        {
            var result = await BackupLoginProvisioner.ProvisionAsync(BuildCurrentConnectionString(), Name);

            IsSqlAuth = true;
            Username = result.Username;
            SetPassword(result.Password);

            var verb = result.ReusedExistingLogin
                ? $"Reused existing login '{result.Username}' and reset its password"
                : $"Created login '{result.Username}'";
            ProvisionResultText =
                $"✓ {verb}. Backup rights granted on {result.GrantedDatabases.Count} database(s); " +
                "the generated password is stored encrypted when you click Save." +
                (result.Warnings.Count > 0 ? "  Warnings: " + string.Join(" | ", result.Warnings) : "");
        }
        catch (Exception ex)
        {
            ProvisionResultText = "✗ " + ex.Message;
        }
    }

    /// <summary>Connection string for the settings currently in the editor (including an unsaved password).</summary>
    private string BuildCurrentConnectionString()
    {
        var probe = Cloner.DeepClone(Working);
        if (IsSqlAuth && _newPassword is not null)
            probe.ProtectedPassword = _protector.Protect(_newPassword);
        return SqlConnectionFactory.BuildConnectionString(probe, _protector);
    }
}
