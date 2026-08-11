namespace CrdDwmWatchdog;

internal sealed class AlertDialog : Form
{
    private readonly ComboBox _actionPicker;
    public RestartAction? SelectedAction { get; private set; }
    public bool SnoozeRequested { get; private set; }
    public bool RemindWhenIdleRequested { get; private set; }

    public AlertDialog(HealthSample sample, HealthAssessment assessment)
    {
        Text = "CRD & DWM health warning";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(610, 390);
        MinimumSize = new Size(560, 350);
        TopMost = true;
        ShowIcon = false;

        var messageBoxFont = SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont;
        var title = new Label
        {
            Text = "Sustained UI-path resource growth detected",
            Dock = DockStyle.Fill,
            AutoSize = true,
            Font = new Font(messageBoxFont.FontFamily, 12, FontStyle.Bold),
            Padding = new Padding(0, 0, 0, 8)
        };

        var details = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = SystemColors.Window,
            Text = string.Join(Environment.NewLine, assessment.Reasons.Select(reason => "• " + reason)) +
                   Environment.NewLine + Environment.NewLine + sample.CrdActivity.Evidence
        };

        _actionPicker = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 190,
            Enabled = !sample.CrdActivity.IsLikelyActive
        };
        _actionPicker.Items.Add(new ActionChoice(RestartAction.Dwm, "Restart DWM"));
        _actionPicker.Items.Add(new ActionChoice(RestartAction.Crd, "Restart CRD"));
        _actionPicker.Items.Add(new ActionChoice(RestartAction.Both, "Restart both"));
        _actionPicker.SelectedItem = assessment switch
        {
            { DwmProblem: true, CrdProblem: true } => _actionPicker.Items[2],
            { DwmProblem: true } => _actionPicker.Items[0],
            _ => _actionPicker.Items[1]
        };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            WrapContents = true
        };
        buttons.Controls.Add(_actionPicker);

        if (sample.CrdActivity.IsLikelyActive)
        {
            var defer = CreateButton("Remind when CRD is idle", (_, _) =>
            {
                RemindWhenIdleRequested = true;
                Close();
            });
            buttons.Controls.Add(defer);
        }
        else
        {
            buttons.Controls.Add(CreateButton("Restart…", (_, _) =>
            {
                if (_actionPicker.SelectedItem is ActionChoice choice)
                {
                    SelectedAction = choice.Action;
                    Close();
                }
            }));
        }

        buttons.Controls.Add(CreateButton("Snooze", (_, _) =>
        {
            SnoozeRequested = true;
            Close();
        }));
        buttons.Controls.Add(CreateButton("Close", (_, _) => Close()));

        var note = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Text = sample.CrdActivity.IsLikelyActive
                ? "Restart controls are deferred because a CRD media session appears active."
                : "The selected restart requires UAC and will not close VS Code, Docker, or terminals."
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 4,
            ColumnCount = 1,
            Padding = new Padding(12)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(title, 0, 0);
        layout.Controls.Add(details, 0, 1);
        layout.Controls.Add(note, 0, 2);
        layout.Controls.Add(buttons, 0, 3);
        Controls.Add(layout);
    }

    private static Button CreateButton(string text, EventHandler handler)
    {
        var button = new Button { Text = text, AutoSize = true, MinimumSize = new Size(90, 30) };
        button.Click += handler;
        return button;
    }

    private sealed record ActionChoice(RestartAction Action, string Label)
    {
        public override string ToString() => Label;
    }
}
