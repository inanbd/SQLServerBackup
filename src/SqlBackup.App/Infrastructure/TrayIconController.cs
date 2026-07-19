using System;
using System.Diagnostics;
using System.Windows;
using SqlBackup.Core;
using WinForms = System.Windows.Forms;

namespace SqlBackup.App.Infrastructure;

/// <summary>
/// System tray icon: open the control panel, run a job, jump to logs, exit.
/// Closing the main window hides it here; backups keep running in the service.
/// </summary>
public sealed class TrayIconController : IDisposable
{
    private readonly AppServices _services;
    private readonly Window _window;
    private readonly WinForms.NotifyIcon _icon;
    private readonly WinForms.ToolStripMenuItem _runJobMenu;
    private bool _hideHintShown;

    public TrayIconController(AppServices services, Window window)
    {
        _services = services;
        _window = window;

        var menu = new WinForms.ContextMenuStrip();
        var open = new WinForms.ToolStripMenuItem("Open Control Panel");
        open.Font = new System.Drawing.Font(open.Font, System.Drawing.FontStyle.Bold);
        open.Click += (_, _) => ShowWindow();

        _runJobMenu = new WinForms.ToolStripMenuItem("Run backup job");

        var openLogs = new WinForms.ToolStripMenuItem("Open logs folder");
        openLogs.Click += (_, _) => OpenFolder(AppPaths.LogDir);

        var exit = new WinForms.ToolStripMenuItem("Exit");
        exit.Click += (_, _) =>
        {
            App.ExitRequested = true;
            System.Windows.Application.Current.Shutdown();
        };

        menu.Items.Add(open);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(_runJobMenu);
        menu.Items.Add(openLogs);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(exit);
        menu.Opening += (_, _) => RebuildRunJobMenu();

        _icon = new WinForms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Shield,
            Text = "SQL Server Backup",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _icon.DoubleClick += (_, _) => ShowWindow();

        AppEvents.BackupFailureDetected += OnBackupFailure;
    }

    public void NotifyHiddenToTray()
    {
        if (_hideHintShown)
            return;
        _hideHintShown = true;
        _icon.ShowBalloonTip(4000, "SQL Server Backup",
            "The control panel keeps running in the tray. Backups run in the service either way. " +
            "Use the tray menu to exit.", WinForms.ToolTipIcon.Info);
    }

    private void OnBackupFailure(string message) =>
        _icon.ShowBalloonTip(10_000, "Backup failed", message, WinForms.ToolTipIcon.Error);

    private void RebuildRunJobMenu()
    {
        _runJobMenu.DropDownItems.Clear();
        try
        {
            var jobs = _services.ConfigStore.Load().Jobs;
            foreach (var job in jobs)
            {
                var item = new WinForms.ToolStripMenuItem(job.Name) { Enabled = job.Enabled };
                var jobId = job.Id;
                var jobName = job.Name;
                item.Click += async (_, _) =>
                {
                    try
                    {
                        var response = await _services.Ipc.RunJobAsync(jobId);
                        _icon.ShowBalloonTip(4000, "SQL Server Backup",
                            response.Accepted ? $"Job '{jobName}' started." : response.Reason ?? "The job was not started.",
                            response.Accepted ? WinForms.ToolTipIcon.Info : WinForms.ToolTipIcon.Warning);
                    }
                    catch (Exception ex)
                    {
                        _icon.ShowBalloonTip(6000, "SQL Server Backup", ex.Message, WinForms.ToolTipIcon.Error);
                    }
                };
                _runJobMenu.DropDownItems.Add(item);
            }
            _runJobMenu.Enabled = jobs.Count > 0;
        }
        catch
        {
            _runJobMenu.Enabled = false;
        }
    }

    private void ShowWindow()
    {
        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    private static void OpenFolder(string path)
    {
        try
        {
            System.IO.Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch
        {
            // Explorer refused — nothing sensible to do.
        }
    }

    public void Dispose()
    {
        AppEvents.BackupFailureDetected -= OnBackupFailure;
        _icon.Visible = false;
        _icon.Dispose();
    }
}
