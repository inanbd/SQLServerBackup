using System.Security.Cryptography;
using System.Text;

namespace SqlBackup.Core.Security;

public interface ISecretProtector
{
    /// <summary>Encrypts a secret for storage. Returns an opaque prefixed blob.</summary>
    string Protect(string plaintext);

    /// <summary>Decrypts a blob produced by <see cref="Protect"/>.</summary>
    string Unprotect(string protectedValue);
}

/// <summary>
/// Protects secrets with Windows DPAPI (machine scope, so both the desktop app
/// and the service account can decrypt them). Machine scope means any process on
/// this machine that can read the config file could decrypt too — restrict the
/// data folder ACL and prefer Windows authentication where possible.
///
/// On non-Windows platforms (development/CI only) DPAPI does not exist; secrets
/// are base64-obfuscated instead, and only when SQLBACKUP_ALLOW_PLAINTEXT_SECRETS=1
/// is set explicitly. Otherwise the operation fails rather than silently
/// downgrading security.
/// </summary>
public sealed class SecretProtector : ISecretProtector
{
    public const string AllowPlaintextEnvVar = "SQLBACKUP_ALLOW_PLAINTEXT_SECRETS";

    private const string DpapiPrefix = "dpapi:";
    private const string PlainPrefix = "plain:";

    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SqlBackup.v1");

    private static bool PlaintextAllowed =>
        Environment.GetEnvironmentVariable(AllowPlaintextEnvVar) == "1";

    public string Protect(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        if (OperatingSystem.IsWindows())
        {
            var blob = ProtectedData.Protect(Encoding.UTF8.GetBytes(plaintext), Entropy, DataProtectionScope.LocalMachine);
            return DpapiPrefix + Convert.ToBase64String(blob);
        }

        if (PlaintextAllowed)
            return PlainPrefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(plaintext));

        throw new PlatformNotSupportedException(
            "DPAPI secret protection is only available on Windows. " +
            $"For development on this platform set {AllowPlaintextEnvVar}=1 (secrets will NOT be encrypted).");
    }

    public string Unprotect(string protectedValue)
    {
        ArgumentNullException.ThrowIfNull(protectedValue);

        if (protectedValue.StartsWith(DpapiPrefix, StringComparison.Ordinal))
        {
            if (!OperatingSystem.IsWindows())
                throw new PlatformNotSupportedException("This secret was protected with Windows DPAPI and can only be read on Windows.");

            var blob = Convert.FromBase64String(protectedValue[DpapiPrefix.Length..]);
            var raw = ProtectedData.Unprotect(blob, Entropy, DataProtectionScope.LocalMachine);
            return Encoding.UTF8.GetString(raw);
        }

        if (protectedValue.StartsWith(PlainPrefix, StringComparison.Ordinal))
        {
            if (!PlaintextAllowed)
                throw new InvalidOperationException(
                    $"Config contains an unencrypted secret but {AllowPlaintextEnvVar} is not set. Re-enter the credential on Windows.");
            return Encoding.UTF8.GetString(Convert.FromBase64String(protectedValue[PlainPrefix.Length..]));
        }

        throw new FormatException("Unrecognized protected secret format.");
    }
}
