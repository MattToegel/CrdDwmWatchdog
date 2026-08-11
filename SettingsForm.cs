namespace CrdDwmWatchdog;

internal sealed class SettingsForm : Form
{
    private readonly NumericUpDown _pollSeconds;
    private readonly NumericUpDown _consecutiveSamples;
    private readonly NumericUpDown _dwmPrivateMb;
    private readonly NumericUpDown _dwmGrowthMb;
    private readonly NumericUpDown _crdPrivateMb;
    private readonly NumericUpDown _crdCpu;
    private readonly NumericUpDown _snoozeMinutes;
    private readonly CheckBox _startWithWindows;

    public SettingsForm(WatchdogSettings settings)
    {
        Text = "CRD & DWM Watchdog settings";
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = false;

        _pollSeconds = CreateNumeric(15, 3600, settings.PollIntervalSeconds);
        _consecutiveSamples = CreateNumeric(1, 20, settings.ConsecutiveAlertSamples);
        _dwmPrivateMb = CreateNumeric(512, 32768, (decimal)settings.DwmPrivateMbThreshold);
        _dwmGrowthMb = CreateNumeric(256, 16384, (decimal)settings.DwmGrowthOverBaselineMb);
        _crdPrivateMb = CreateNumeric(32, 8192, (decimal)settings.IdleCrdPrivateMbThreshold);
        _crdCpu = CreateNumeric(1, 100, (decimal)settings.IdleCrdCpuCorePercentThreshold);
        _snoozeMinutes = CreateNumeric(5, 1440, settings.SnoozeMinutes);
        _startWithWindows = new CheckBox
        {
            Text = "Start with Windows",
            Checked = settings.StartWithWindows,
            AutoSize = true
        };

        var grid = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 9,
            AutoSize = true,
            Padding = new Padding(12)
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        AddRow(grid, 0, "Poll interval (seconds)", _pollSeconds);
        AddRow(grid, 1, "Consecutive samples before prompting", _consecutiveSamples);
        AddRow(grid, 2, "DWM private-memory threshold (MB)", _dwmPrivateMb);
        AddRow(grid, 3, "DWM growth over baseline (MB)", _dwmGrowthMb);
        AddRow(grid, 4, "Idle CRD private-memory threshold (MB)", _crdPrivateMb);
        AddRow(grid, 5, "Idle CRD CPU threshold (% of one core)", _crdCpu);
        AddRow(grid, 6, "Default snooze (minutes)", _snoozeMinutes);
        grid.Controls.Add(_startWithWindows, 1, 7);

        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        var save = new Button { Text = "Save", AutoSize = true, DialogResult = DialogResult.OK };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(save);
        grid.Controls.Add(buttons, 1, 8);

        AcceptButton = save;
        CancelButton = cancel;
        Controls.Add(grid);
    }

    public void ApplyTo(WatchdogSettings settings)
    {
        settings.PollIntervalSeconds = (int)_pollSeconds.Value;
        settings.ConsecutiveAlertSamples = (int)_consecutiveSamples.Value;
        settings.DwmPrivateMbThreshold = (double)_dwmPrivateMb.Value;
        settings.DwmGrowthOverBaselineMb = (double)_dwmGrowthMb.Value;
        settings.IdleCrdPrivateMbThreshold = (double)_crdPrivateMb.Value;
        settings.IdleCrdCpuCorePercentThreshold = (double)_crdCpu.Value;
        settings.SnoozeMinutes = (int)_snoozeMinutes.Value;
        settings.StartWithWindows = _startWithWindows.Checked;
    }

    private static NumericUpDown CreateNumeric(decimal min, decimal max, decimal value) => new()
    {
        Minimum = min,
        Maximum = max,
        Value = Math.Clamp(value, min, max),
        Width = 120,
        ThousandsSeparator = true
    };

    private static void AddRow(TableLayoutPanel grid, int row, string label, Control control)
    {
        grid.Controls.Add(new Label
        {
            Text = label,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Padding = new Padding(0, 5, 12, 5)
        }, 0, row);
        grid.Controls.Add(control, 1, row);
    }
}
