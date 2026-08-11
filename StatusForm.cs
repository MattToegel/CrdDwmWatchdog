using System.Text;

namespace CrdDwmWatchdog;

internal sealed class StatusForm : Form
{
    public RestartAction? SelectedAction { get; private set; }
    public bool CaptureBaselineRequested { get; private set; }
    public bool CheckNowRequested { get; private set; }

    public StatusForm(HealthSample? sample, HealthAssessment? assessment, BaselineState? baseline)
    {
        Text = "CRD & DWM Health Watchdog";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(680, 520);
        Size = new Size(760, 590);
        ShowIcon = false;

        var statusText = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font(FontFamily.GenericMonospace, 9.5f),
            BackColor = SystemColors.Window,
            Text = BuildStatusText(sample, assessment, baseline)
        };

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            WrapContents = true,
            Padding = new Padding(4)
        };

        buttonPanel.Controls.Add(CreateButton("Check now", (_, _) =>
        {
            CheckNowRequested = true;
            DialogResult = DialogResult.Retry;
            Close();
        }));
        buttonPanel.Controls.Add(CreateButton("Restart DWM…", (_, _) => Select(RestartAction.Dwm)));
        buttonPanel.Controls.Add(CreateButton("Restart CRD…", (_, _) => Select(RestartAction.Crd)));
        buttonPanel.Controls.Add(CreateButton("Restart both…", (_, _) => Select(RestartAction.Both)));
        buttonPanel.Controls.Add(CreateButton("Set current baseline", (_, _) =>
        {
            CaptureBaselineRequested = true;
            DialogResult = DialogResult.OK;
            Close();
        }));
        buttonPanel.Controls.Add(CreateButton("Close", (_, _) => Close()));

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(10)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(statusText, 0, 0);
        layout.Controls.Add(buttonPanel, 0, 1);
        Controls.Add(layout);
    }

    private void Select(RestartAction action)
    {
        SelectedAction = action;
        DialogResult = DialogResult.OK;
        Close();
    }

    private static Button CreateButton(string text, EventHandler clickHandler)
    {
        var button = new Button { Text = text, AutoSize = true, MinimumSize = new Size(105, 34) };
        button.Click += clickHandler;
        return button;
    }

    private static string BuildStatusText(
        HealthSample? sample,
        HealthAssessment? assessment,
        BaselineState? baseline)
    {
        if (sample is null)
        {
            return "Waiting for the first health sample…";
        }

        var builder = new StringBuilder();
        builder.AppendLine($"Last sample: {sample.Timestamp.LocalDateTime:G}");
        builder.AppendLine($"CRD session: {(sample.CrdActivity.IsLikelyActive ? "LIKELY ACTIVE" : "likely idle")}");
        builder.AppendLine($"  {sample.CrdActivity.Evidence}");
        builder.AppendLine();

        if (sample.Dwm is { } dwm)
        {
            builder.AppendLine("Desktop Window Manager");
            builder.AppendLine($"  PID {dwm.Id}, private {dwm.PrivateMb:N1} MB, working {dwm.WorkingSetMb:N1} MB");
            builder.AppendLine($"  handles {dwm.Handles:N0}, threads {dwm.Threads:N0}, responding {dwm.Responding}");
        }
        else
        {
            builder.AppendLine("Desktop Window Manager: unavailable");
        }

        builder.AppendLine();
        builder.AppendLine($"Chrome Remote Desktop ({sample.CrdProcesses.Count} process(es))");
        foreach (var crd in sample.CrdProcesses.OrderBy(process => process.Id))
        {
            builder.AppendLine($"  PID {crd.Id}, session {crd.SessionId}, private {crd.PrivateMb:N1} MB, " +
                               $"working {crd.WorkingSetMb:N1} MB, CPU {crd.CpuCorePercent:N1}% core");
            builder.AppendLine($"    handles {crd.Handles:N0}, threads {crd.Threads:N0}, responding {crd.Responding}");
        }

        builder.AppendLine();
        builder.AppendLine("Assessment");
        if (assessment is null || !assessment.HasProblem)
        {
            builder.AppendLine("  No sustained threshold violation detected.");
        }
        else
        {
            foreach (var reason in assessment.Reasons)
            {
                builder.AppendLine($"  • {reason}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("Clean baseline");
        if (baseline is null)
        {
            builder.AppendLine("  Not captured yet. Absolute safety thresholds are active.");
        }
        else
        {
            builder.AppendLine($"  Captured {baseline.CapturedAt.LocalDateTime:G}");
            builder.AppendLine($"  DWM private {baseline.DwmPrivateMb?.ToString("N1") ?? "n/a"} MB; " +
                               $"idle CRD private {baseline.IdleCrdPrivateMb?.ToString("N1") ?? "n/a"} MB");
        }

        builder.AppendLine();
        builder.AppendLine("Restarts are never automatic. An active CRD session always triggers an interruption warning.");
        return builder.ToString();
    }
}
