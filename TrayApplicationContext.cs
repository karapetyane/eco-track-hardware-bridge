using System.Drawing;
using EcoTrack.HardwareBridge.Models;
using EcoTrack.HardwareBridge.Services;

namespace EcoTrack.HardwareBridge;

public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly BridgeService _bridge;
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _statusMenuItem;
    private readonly SynchronizationContext? _uiContext = SynchronizationContext.Current;

    public TrayApplicationContext(BridgeService bridge)
    {
        _bridge = bridge;
        _bridge.StatusChanged += OnBridgeStatusChanged;

        var menu = new ContextMenuStrip();
        _statusMenuItem = new ToolStripMenuItem("Status", null, OnStatusClick);
        var exitMenuItem = new ToolStripMenuItem("Exit", null, OnExitClick);

        menu.Items.Add(_statusMenuItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitMenuItem);

        _notifyIcon = new NotifyIcon
        {
            Icon = TrayIconLoader.Load(),
            Visible = true,
            Text = "EcoTrack Hardware Bridge",
            ContextMenuStrip = menu
        };

        _notifyIcon.DoubleClick += (_, _) => ShowStatusDialog();

        _bridge.Start();
        UpdateTrayPresentation();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _bridge.StatusChanged -= OnBridgeStatusChanged;
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _bridge.Dispose();
        }

        base.Dispose(disposing);
    }

    private void OnBridgeStatusChanged()
    {
        if (_uiContext != null)
        {
            _uiContext.Post(_ => UpdateTrayPresentation(), null);
            return;
        }

        UpdateTrayPresentation();
    }

    private void UpdateTrayPresentation()
    {
        var summary = BuildTraySummary();
        _statusMenuItem.Text = $"Status: {summary.ShortLabel}";
        _notifyIcon.Text = TruncateTooltip(summary.Tooltip);
    }

    private void OnStatusClick(object? sender, EventArgs e)
    {
        ShowStatusDialog();
    }

    private void OnExitClick(object? sender, EventArgs e)
    {
        ExitThread();
    }

    private void ShowStatusDialog()
    {
        MessageBox.Show(
            _bridge.GetStatusSummary(),
            "EcoTrack Hardware Bridge",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private TraySummary BuildTraySummary()
    {
        return _bridge.Status switch
        {
            BridgeStatus.Running when _bridge.CardPresent =>
                new TraySummary("Running — card on reader", "EcoTrack Bridge — Running (card detected)"),
            BridgeStatus.Running =>
                new TraySummary("Running", "EcoTrack Bridge — Running (ws://localhost:5001)"),
            BridgeStatus.Starting =>
                new TraySummary("Starting...", "EcoTrack Bridge — Starting"),
            BridgeStatus.NoReader =>
                new TraySummary("No reader", "EcoTrack Bridge — No PC/SC reader found"),
            BridgeStatus.Error =>
                new TraySummary("Error", $"EcoTrack Bridge — Error: {_bridge.StatusMessage}"),
            _ =>
                new TraySummary("Stopped", "EcoTrack Bridge — Stopped")
        };
    }

    private static string TruncateTooltip(string value)
    {
        const int maxLength = 63;
        return value.Length <= maxLength ? value : value[..(maxLength - 3)] + "...";
    }

    private sealed record TraySummary(string ShortLabel, string Tooltip);
}
