using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SqlBackup.Core.Offsite;

/// <summary>
/// Establishes an authenticated session to a UNC share (the equivalent of
/// <c>net use \\server\share /user:…</c>) so file APIs can reach it under
/// credentials other than the process identity. Disposing drops the session.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsNetworkConnection : IDisposable
{
    private const int NoError = 0;
    private const int ErrorSessionCredentialConflict = 1219;
    private const int ResourceTypeDisk = 1;

    private readonly string _shareRoot;
    private bool _connected;

    public WindowsNetworkConnection(string shareRoot, string username, string password)
    {
        _shareRoot = shareRoot;

        var resource = new NetResource
        {
            Scope = 2, // RESOURCE_GLOBALNET
            ResourceType = ResourceTypeDisk,
            DisplayType = 3, // RESOURCEDISPLAYTYPE_SHARE
            Usage = 1, // RESOURCEUSAGE_CONNECTABLE
            RemoteName = shareRoot,
        };

        var result = WNetAddConnection2(resource, password, username, 0);
        if (result == ErrorSessionCredentialConflict)
        {
            // Windows allows only one credential set per share per logon session.
            throw new InvalidOperationException(
                $"Windows already has a session to '{shareRoot}' under different credentials " +
                "(only one credential set per share is possible per account). Disconnect it " +
                "(net use /delete) or use the same account the backup engine runs as.");
        }
        if (result != NoError)
            throw new Win32Exception(result, $"Could not connect to '{shareRoot}': {new Win32Exception(result).Message}");

        _connected = true;
    }

    public void Dispose()
    {
        if (!_connected)
            return;
        _connected = false;
        // force: false — never tear down a session another operation is still using.
        WNetCancelConnection2(_shareRoot, 0, false);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NetResource
    {
        public int Scope;
        public int ResourceType;
        public int DisplayType;
        public int Usage;
        public string? LocalName;
        public string? RemoteName;
        public string? Comment;
        public string? Provider;
    }

    [DllImport("mpr.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int WNetAddConnection2(NetResource netResource, string? password, string? username, int flags);

    [DllImport("mpr.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int WNetCancelConnection2(string name, int flags, bool force);
}
