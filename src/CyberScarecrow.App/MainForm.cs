using System.Drawing;
using System.Windows.Forms;

namespace CyberScarecrow;

internal sealed class MainForm : Form
{
    private readonly AppConfig _config;
    private readonly DecoyManager _manager;
    private readonly bool _startMinimized;
    private readonly bool _autoArm;

    private readonly ListView _list = new();
    private readonly ColumnHeader _toolColumn = new();
    private readonly ColumnHeader _processColumn = new();
    private readonly ColumnHeader _titleColumn = new();
    private readonly ColumnHeader _statusColumn = new();
    private readonly ToggleSwitch _toggle = new();
    private readonly Label _armTitle = new();
    private readonly Label _armSubtitle = new();
    private readonly ComboBox _preset = new();
    private readonly PillLabel _headerPill = new();
    private readonly CheckBox _startWithWindows = new();
    private readonly CheckBox _minimizeToTray = new();
    private readonly CheckBox _deepDisguise = new();
    private readonly CheckBox _pauseOnFullscreen = new();
    private readonly NotifyIcon _tray = new();
    private readonly ToolStripMenuItem _trayToggle = new();
    private readonly System.Windows.Forms.Timer _timer = new();
    private readonly ToolTip _toolTip = new();

    private bool _armed;
    private bool _updatingUi;
    private bool _reallyExit;
    private bool _balloonShown;
    private bool _cleanedUp;
    private IntPtr _lastFullscreenWindow;

    private const string DeepDisguiseExplanation =
        "Deep disguise changes how decoys are deployed:\n\n" +
        "• Decoy files are copied to %LOCALAPPDATA%\\Programs\\<tool>\\ so they look like per-user installs.\n" +
        "• Decoys are launched as if started by Explorer, instead of by CyberScarecrow.\n" +
        "• Decoy files are removed when you disarm or exit.\n\n" +
        "It does not spoof digital signatures, version metadata, loaded modules, kernel drivers, or VM artifacts. Determined malware and anti-cheat software can still detect it.\n\n" +
        "Do not arm while playing anti-cheat games.";

    public MainForm(bool startMinimized, bool autoArm)
    {
        _startMinimized = startMinimized;
        _autoArm = autoArm;
        _config = AppConfig.Load();
        _manager = new DecoyManager();

        BuildUi();
        BuildTray();
        LoadConfigIntoUi();

        _timer.Interval = 1500;
        _timer.Tick += OnTimerTick;
        _timer.Start();

        Shown += OnShown;
    }

    private void BuildUi()
    {
        SuspendLayout();

        Text = "CyberScarecrow";
        ClientSize = new Size(920, 744);
        MinimumSize = new Size(840, 684);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Background;
        Font = new Font("Segoe UI", 9F);
        AutoScaleMode = AutoScaleMode.Dpi;
        DoubleBuffered = true;

        try
        {
            var icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (icon is not null)
            {
                Icon = icon;
            }
        }
        catch
        {
            // Keep the default icon if extraction fails.
        }

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Theme.Background,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 116F));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 80F));
        Controls.Add(root);

        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildContent(), 0, 1);
        root.Controls.Add(BuildFooter(), 0, 2);

        ResumeLayout(false);
        PerformLayout();
    }

    private Panel BuildHeader()
    {
        var header = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Header,
            Margin = Padding.Empty
        };

        header.Controls.Add(new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 3,
            BackColor = Theme.Accent
        });

        var logo = new PictureBox
        {
            Location = new Point(28, 28),
            Size = new Size(58, 58),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Transparent
        };

        try
        {
            var icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (icon is not null)
            {
                using var large = new Icon(icon, 64, 64);
                logo.Image = large.ToBitmap();
            }
        }
        catch
        {
            // Fall through to the system icon.
        }

        if (logo.Image is null)
        {
            logo.Image = SystemIcons.Shield.ToBitmap();
        }

        header.Controls.Add(logo);

        header.Controls.Add(new Label
        {
            Text = "CyberScarecrow",
            AutoSize = true,
            Location = new Point(102, 24),
            Font = new Font("Segoe UI", 22F, FontStyle.Bold),
            ForeColor = Color.White,
            BackColor = Color.Transparent
        });

        header.Controls.Add(new Label
        {
            Text = "Make your machine look like a malware analyst's lab.",
            AutoSize = true,
            Location = new Point(105, 66),
            Font = new Font("Segoe UI", 9.5F),
            ForeColor = Color.FromArgb(168, 173, 184),
            BackColor = Color.Transparent
        });

        _headerPill.Text = "DISARMED";
        _headerPill.FillColor = Theme.Muted;
        _headerPill.Size = new Size(118, 30);
        _headerPill.Location = new Point(ClientSize.Width - 28 - 118, 42);
        _headerPill.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        header.Controls.Add(_headerPill);

        return header;
    }

    private TableLayoutPanel BuildContent()
    {
        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Background,
            Padding = new Padding(28, 22, 28, 8),
            ColumnCount = 1,
            RowCount = 3,
            Margin = Padding.Empty
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 108F));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 16F));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        content.Controls.Add(BuildTopCard(), 0, 0);
        content.Controls.Add(new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Background,
            Margin = Padding.Empty
        }, 0, 1);
        content.Controls.Add(BuildListCard(), 0, 2);

        return content;
    }

    private CardPanel BuildTopCard()
    {
        var card = new CardPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty
        };

        _toggle.Location = new Point(26, 34);
        _toggle.Size = new Size(66, 36);
        _toggle.CheckedChanged += Toggle_CheckedChanged;
        _toolTip.SetToolTip(_toggle, "Arm or disarm all selected decoys.");
        card.Controls.Add(_toggle);

        _armTitle.AutoSize = true;
        _armTitle.Location = new Point(112, 24);
        _armTitle.Font = new Font("Segoe UI", 13F, FontStyle.Bold);
        _armTitle.ForeColor = Theme.Text;
        _armTitle.BackColor = Theme.Card;
        card.Controls.Add(_armTitle);

        _armSubtitle.AutoSize = true;
        _armSubtitle.Location = new Point(114, 55);
        _armSubtitle.Font = new Font("Segoe UI", 9F);
        _armSubtitle.ForeColor = Theme.Muted;
        _armSubtitle.BackColor = Theme.Card;
        card.Controls.Add(_armSubtitle);

        var presetPanel = new Panel
        {
            Dock = DockStyle.Right,
            Width = 238,
            BackColor = Theme.Card,
            Padding = new Padding(0, 22, 26, 0)
        };

        presetPanel.Controls.Add(new Label
        {
            Text = "PRESET",
            AutoSize = true,
            Location = new Point(0, 0),
            Font = new Font("Segoe UI", 7.5F, FontStyle.Bold),
            ForeColor = Theme.Muted,
            BackColor = Theme.Card
        });

        _preset.DropDownStyle = ComboBoxStyle.DropDownList;
        _preset.FlatStyle = FlatStyle.Flat;
        _preset.Font = new Font("Segoe UI", 9.5F);
        _preset.Location = new Point(0, 20);
        _preset.Size = new Size(184, 28);
        _preset.Items.AddRange(new object[] { "Low", "Medium", "High", "Custom" });
        _preset.SelectedIndexChanged += Preset_SelectedIndexChanged;
        presetPanel.Controls.Add(_preset);
        card.Controls.Add(presetPanel);

        return card;
    }

    private CardPanel BuildListCard()
    {
        var card = new CardPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty
        };

        var inner = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Theme.Card,
            Padding = new Padding(24, 18, 24, 20),
            Margin = Padding.Empty
        };
        inner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        inner.RowStyles.Add(new RowStyle(SizeType.Absolute, 60F));
        inner.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        card.Controls.Add(inner);

        var header = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Card,
            Margin = Padding.Empty
        };

        header.Controls.Add(new Label
        {
            Text = "Decoy processes",
            AutoSize = true,
            Location = new Point(0, 0),
            Font = new Font("Segoe UI", 11F, FontStyle.Bold),
            ForeColor = Theme.Text,
            BackColor = Theme.Card
        });

        header.Controls.Add(new Label
        {
            Text = "Choose the tools you want malware to see running.",
            AutoSize = true,
            Location = new Point(1, 27),
            Font = new Font("Segoe UI", 9F),
            ForeColor = Theme.Muted,
            BackColor = Theme.Card
        });

        var links = new Panel
        {
            Dock = DockStyle.Right,
            Width = 170,
            BackColor = Theme.Card
        };

        var selectAll = new LinkLabel
        {
            Text = "Select all",
            AutoSize = true,
            Location = new Point(26, 3),
            LinkColor = Theme.AccentDark,
            ActiveLinkColor = Theme.Accent,
            VisitedLinkColor = Theme.AccentDark,
            LinkBehavior = LinkBehavior.HoverUnderline,
            BackColor = Theme.Card
        };
        selectAll.LinkClicked += (_, _) => SetAllChecked(true);

        var clear = new LinkLabel
        {
            Text = "Clear",
            AutoSize = true,
            Location = new Point(112, 3),
            LinkColor = Theme.AccentDark,
            ActiveLinkColor = Theme.Accent,
            VisitedLinkColor = Theme.AccentDark,
            LinkBehavior = LinkBehavior.HoverUnderline,
            BackColor = Theme.Card
        };
        clear.LinkClicked += (_, _) => SetAllChecked(false);

        links.Controls.Add(selectAll);
        links.Controls.Add(clear);
        header.Controls.Add(links);
        inner.Controls.Add(header, 0, 0);

        _list.View = View.Details;
        _list.CheckBoxes = true;
        _list.FullRowSelect = true;
        _list.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        _list.BorderStyle = BorderStyle.None;
        _list.BackColor = Theme.Card;
        _list.ForeColor = Theme.Text;
        _list.Font = new Font("Segoe UI", 9F);
        _list.Dock = DockStyle.Fill;
        _list.Margin = new Padding(0, 6, 0, 0);
        _list.ShowItemToolTips = true;

        _toolColumn.Text = "Tool";
        _toolColumn.Width = 190;
        _processColumn.Text = "Process";
        _processColumn.Width = 205;
        _titleColumn.Text = "Window title";
        _titleColumn.Width = 300;
        _statusColumn.Text = "Status";
        _statusColumn.Width = 118;
        _list.Columns.AddRange(new[] { _toolColumn, _processColumn, _titleColumn, _statusColumn });

        _list.ItemChecked += List_ItemChecked;
        _list.Resize += (_, _) => AdjustColumns();

        var listBorder = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Border,
            Padding = new Padding(1),
            Margin = new Padding(0, 6, 0, 0)
        };
        listBorder.Controls.Add(_list);
        inner.Controls.Add(listBorder, 0, 1);

        return card;
    }

    private TableLayoutPanel BuildFooter()
    {
        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Background,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(30, 4, 30, 4),
            Margin = Padding.Empty
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
        footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        var options = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };

        _startWithWindows.Text = "Start with Windows";
        _startWithWindows.AutoSize = true;
        _startWithWindows.ForeColor = Theme.Muted;
        _startWithWindows.BackColor = Color.Transparent;
        _startWithWindows.Font = new Font("Segoe UI", 9F);
        _startWithWindows.Margin = new Padding(0, 4, 18, 0);
        _startWithWindows.CheckedChanged += StartWithWindows_CheckedChanged;
        options.Controls.Add(_startWithWindows);

        _minimizeToTray.Text = "Minimize to tray";
        _minimizeToTray.AutoSize = true;
        _minimizeToTray.ForeColor = Theme.Muted;
        _minimizeToTray.BackColor = Color.Transparent;
        _minimizeToTray.Font = new Font("Segoe UI", 9F);
        _minimizeToTray.Margin = new Padding(0, 4, 18, 0);
        _minimizeToTray.CheckedChanged += MinimizeToTray_CheckedChanged;
        options.Controls.Add(_minimizeToTray);

        _deepDisguise.Text = "Deep disguise";
        _deepDisguise.AutoSize = true;
        _deepDisguise.ForeColor = Theme.Text;
        _deepDisguise.BackColor = Color.Transparent;
        _deepDisguise.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        _deepDisguise.Margin = new Padding(0, 4, 4, 0);
        _deepDisguise.CheckedChanged += DeepDisguise_CheckedChanged;
        _toolTip.SetToolTip(
            _deepDisguise,
            "Copies decoys to per-user install paths and launches them as Explorer children. Still detectable.");
        options.Controls.Add(_deepDisguise);

        var deepInfo = new LinkLabel
        {
            Text = "what it does",
            AutoSize = true,
            LinkColor = Theme.AccentDark,
            ActiveLinkColor = Theme.Accent,
            VisitedLinkColor = Theme.AccentDark,
            LinkBehavior = LinkBehavior.HoverUnderline,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 5, 18, 0)
        };
        deepInfo.LinkClicked += (_, _) => ShowDeepDisguiseInfo();
        options.Controls.Add(deepInfo);

        _pauseOnFullscreen.Text = "Pause in fullscreen";
        _pauseOnFullscreen.AutoSize = true;
        _pauseOnFullscreen.ForeColor = Theme.Muted;
        _pauseOnFullscreen.BackColor = Color.Transparent;
        _pauseOnFullscreen.Font = new Font("Segoe UI", 9F);
        _pauseOnFullscreen.Margin = new Padding(0, 4, 18, 0);
        _pauseOnFullscreen.CheckedChanged += PauseOnFullscreen_CheckedChanged;
        _toolTip.SetToolTip(
            _pauseOnFullscreen,
            "Automatically disarm when a fullscreen app is in the foreground.");
        options.Controls.Add(_pauseOnFullscreen);

        var note = new Label
        {
            Text = "Harmless placeholders. No files or traffic are inspected.",
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = Theme.Muted,
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI", 8.5F),
            Margin = Padding.Empty
        };

        footer.Controls.Add(options, 0, 0);
        footer.Controls.Add(note, 0, 1);
        return footer;
    }

    private void BuildTray()
    {
        _tray.Icon = Icon ?? SystemIcons.Shield;
        _tray.Text = "CyberScarecrow — disarmed";
        _tray.Visible = true;
        _tray.DoubleClick += (_, _) => ShowFromTray();

        var menu = new ContextMenuStrip();
        var open = new ToolStripMenuItem("Open CyberScarecrow", null, (_, _) => ShowFromTray());
        open.Font = new Font(open.Font, FontStyle.Bold);

        _trayToggle.Click += (_, _) =>
        {
            if (_armed)
            {
                Disarm();
            }
            else
            {
                Arm();
            }
        };

        var exit = new ToolStripMenuItem("Exit", null, (_, _) => ExitApp());

        menu.Items.Add(open);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_trayToggle);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exit);
        _tray.ContextMenuStrip = menu;
    }

    private void LoadConfigIntoUi()
    {
        _updatingUi = true;
        try
        {
            _list.BeginUpdate();
            _list.Items.Clear();

            foreach (var decoy in DecoyCatalog.All)
            {
                var item = new ListViewItem(decoy.Tool)
                {
                    Tag = decoy,
                    Checked = _config.Selected.Contains(decoy.Id, StringComparer.OrdinalIgnoreCase),
                    ToolTipText = $"Runs a harmless placeholder process named {decoy.ExeName}."
                };
                item.UseItemStyleForSubItems = false;
                item.SubItems.Add(decoy.ExeName);
                item.SubItems.Add(decoy.WindowTitle);
                item.SubItems.Add("Stopped");
                _list.Items.Add(item);
            }

            _list.EndUpdate();

            var preset = Enum.TryParse<PresetLevel>(_config.Preset, true, out var parsed)
                ? parsed
                : PresetLevel.Custom;
            _preset.SelectedIndex = preset switch
            {
                PresetLevel.Low => 0,
                PresetLevel.Medium => 1,
                PresetLevel.High => 2,
                _ => 3
            };

            _minimizeToTray.Checked = _config.MinimizeToTray;
            _startWithWindows.Checked = StartupRegistration.IsEnabled();
            _deepDisguise.Checked = _config.DeepDisguise;
            _pauseOnFullscreen.Checked = _config.PauseOnFullscreen;
            _manager.DeepDisguise = _config.DeepDisguise;
        }
        finally
        {
            _updatingUi = false;
        }

        UpdateArmUi();
    }

    private void OnShown(object? sender, EventArgs e)
    {
        RefreshStatuses();

        var armed = _autoArm && Arm();
        if (_startMinimized && (!_autoArm || armed))
        {
            HideToTray();
        }
    }

    private bool Arm()
    {
        var selected = GetSelectedDecoys();
        if (selected.Count == 0)
        {
            _armed = false;
            UpdateArmUi();
            SetSubtitle("Choose at least one decoy first.", Theme.AccentDark);
            return false;
        }

        if (!_manager.HostAvailable)
        {
            _armed = false;
            UpdateArmUi();
            SetSubtitle("The decoy host is missing.", Theme.Red);
            MessageBox.Show(
                this,
                "The decoy host is missing.\n\n" +
                "CyberScarecrow.Host.exe must sit next to CyberScarecrow.exe. " +
                "Run build.ps1 to produce a complete build.",
                "CyberScarecrow",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return false;
        }

        var started = 0;
        var alreadyRunning = 0;
        var failed = 0;

        foreach (var decoy in selected)
        {
            switch (_manager.Start(decoy))
            {
                case DecoyStartResult.Started:
                    started++;
                    break;
                case DecoyStartResult.AlreadyRunning:
                    alreadyRunning++;
                    break;
                default:
                    failed++;
                    break;
            }
        }

        _armed = started > 0;
        _config.Save();
        UpdateArmUi();

        if (_armed)
        {
            if (failed > 0)
            {
                SetSubtitle(
                    $"{started} started, {failed} failed. See the status column.",
                    Theme.AccentDark);
            }
        }
        else
        {
            SetSubtitle(
                alreadyRunning > 0
                    ? "Those tools are already running on this machine."
                    : "Could not start the decoys. Check the status column.",
                alreadyRunning > 0 ? Theme.Blue : Theme.Red);
        }

        RefreshStatuses();
        return _armed;
    }

    private void Disarm()
    {
        _manager.StopAll();
        _armed = false;
        _config.Save();
        UpdateArmUi();
        RefreshStatuses();
    }

    private void ApplySelectionChanges()
    {
        if (!_armed)
        {
            RefreshStatuses();
            return;
        }

        var selected = GetSelectedDecoys();
        if (selected.Count == 0)
        {
            Disarm();
            return;
        }

        var selectedIds = selected
            .Select(d => d.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var decoy in DecoyCatalog.All)
        {
            if (!selectedIds.Contains(decoy.Id) && _manager.IsRunning(decoy))
            {
                _manager.Stop(decoy);
            }
        }

        foreach (var decoy in selected)
        {
            if (!_manager.IsRunning(decoy))
            {
                _manager.Start(decoy);
            }
        }

        _config.Selected = selectedIds.ToList();
        _config.Save();
        UpdateArmUi();
        RefreshStatuses();
    }

    private void UpdateArmUi()
    {
        _updatingUi = true;
        try
        {
            _toggle.Checked = _armed;
        }
        finally
        {
            _updatingUi = false;
        }

        var active = GetSelectedDecoys().Count(d => _manager.IsRunning(d));

        _armTitle.Text = _armed ? "Scarecrow armed" : "Scarecrow disarmed";
        _armSubtitle.ForeColor = Theme.Muted;
        _armSubtitle.Text = _armed
            ? ActiveSubtitle(active)
            : _config.DeepDisguise
                ? "No decoys are running. Deep disguise on."
                : "No decoys are running.";

        _headerPill.Text = _armed ? "ARMED" : "DISARMED";
        _headerPill.FillColor = _armed ? Theme.Accent : Theme.Muted;
        _trayToggle.Text = _armed ? "Disarm" : "Arm";
        _tray.Text = _armed ? "CyberScarecrow — armed" : "CyberScarecrow — disarmed";
    }

    private void RefreshStatuses()
    {
        if (!IsHandleCreated || IsDisposed) return;

        HashSet<string> external;
        try
        {
            external = _manager.GetExternalProcessNames();
        }
        catch
        {
            external = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        _list.BeginUpdate();
        try
        {
            foreach (ListViewItem item in _list.Items)
            {
                if (item.Tag is not Decoy decoy) continue;

                string status;
                Color color;

                if (_manager.IsRunning(decoy))
                {
                    status = "Running";
                    color = Theme.Green;
                }
                else if (external.Contains(Path.GetFileNameWithoutExtension(decoy.ExeName)))
                {
                    status = "Already running";
                    color = Theme.Blue;
                }
                else if (!_manager.HostAvailable)
                {
                    status = "Host missing";
                    color = Theme.Red;
                }
                else
                {
                    status = "Stopped";
                    color = Theme.Muted;
                }

                var subItem = item.SubItems[3];
                if (subItem.Text != status)
                {
                    subItem.Text = status;
                    subItem.ForeColor = color;
                }
                else if (subItem.ForeColor != color)
                {
                    subItem.ForeColor = color;
                }
            }
        }
        finally
        {
            _list.EndUpdate();
        }

        if (_armed)
        {
            var active = GetSelectedDecoys().Count(d => _manager.IsRunning(d));
            _armSubtitle.ForeColor = Theme.Muted;
            _armSubtitle.Text = ActiveSubtitle(active);
        }
    }

    private string ActiveSubtitle(int active)
    {
        var suffix = _config.DeepDisguise ? " Deep disguise on." : string.Empty;
        return active == 0
            ? "Waiting for decoys..." + suffix
            : $"{active} decoy{(active == 1 ? "" : "s")} running. Malware sees an analyst's box.{suffix}";
    }

    private List<Decoy> GetSelectedDecoys()
    {
        var selected = new List<Decoy>();
        foreach (ListViewItem item in _list.Items)
        {
            if (item.Checked && item.Tag is Decoy decoy)
            {
                selected.Add(decoy);
            }
        }
        return selected;
    }

    private void SetSubtitle(string text, Color? color = null)
    {
        _armSubtitle.Text = text;
        _armSubtitle.ForeColor = color ?? Theme.Muted;
    }

    private void AdjustColumns()
    {
        if (_list.Columns.Count < 4) return;
        var width = _list.ClientSize.Width;
        if (width <= 0) return;

        const int tool = 190;
        const int process = 205;
        const int status = 118;
        var title = Math.Max(160, width - tool - process - status - SystemInformation.VerticalScrollBarWidth - 4);

        _toolColumn.Width = tool;
        _processColumn.Width = process;
        _titleColumn.Width = title;
        _statusColumn.Width = status;
    }

    private void Toggle_CheckedChanged(object? sender, EventArgs e)
    {
        if (_updatingUi) return;

        if (_toggle.Checked)
        {
            Arm();
        }
        else
        {
            Disarm();
        }
    }

    private void Preset_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_updatingUi) return;

        var preset = _preset.SelectedIndex switch
        {
            0 => PresetLevel.Low,
            1 => PresetLevel.Medium,
            2 => PresetLevel.High,
            _ => PresetLevel.Custom
        };

        if (preset == PresetLevel.Custom)
        {
            _config.Preset = nameof(PresetLevel.Custom);
            _config.Save();
            return;
        }

        var ids = DecoyCatalog.ForPreset(preset)
            .Select(d => d.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        _updatingUi = true;
        try
        {
            foreach (ListViewItem item in _list.Items)
            {
                if (item.Tag is Decoy decoy)
                {
                    item.Checked = ids.Contains(decoy.Id);
                }
            }

            _config.Preset = preset.ToString();
            _config.Selected = ids.ToList();
        }
        finally
        {
            _updatingUi = false;
        }

        _config.Save();
        ApplySelectionChanges();
    }

    private void List_ItemChecked(object? sender, ItemCheckedEventArgs e)
    {
        if (_updatingUi) return;
        if (e.Item.Tag is not Decoy decoy) return;

        var wasSelected = _config.Selected.Contains(decoy.Id, StringComparer.OrdinalIgnoreCase);
        if (e.Item.Checked == wasSelected)
        {
            // This is the ListView syncing its initial state, not a user action.
            return;
        }

        var selected = _config.Selected.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (e.Item.Checked)
        {
            selected.Add(decoy.Id);
        }
        else
        {
            selected.Remove(decoy.Id);
        }

        _config.Selected = selected.ToList();
        _config.Preset = nameof(PresetLevel.Custom);

        _updatingUi = true;
        try
        {
            _preset.SelectedIndex = 3;
        }
        finally
        {
            _updatingUi = false;
        }

        _config.Save();
        QueueApplySelectionChanges();
    }

    private void SetAllChecked(bool checkedState)
    {
        _updatingUi = true;
        try
        {
            foreach (ListViewItem item in _list.Items)
            {
                item.Checked = checkedState;
            }

            _config.Selected = checkedState
                ? DecoyCatalog.All.Select(d => d.Id).ToList()
                : new List<string>();
            _config.Preset = nameof(PresetLevel.Custom);
            _preset.SelectedIndex = 3;
        }
        finally
        {
            _updatingUi = false;
        }

        _config.Save();
        ApplySelectionChanges();
    }

    private void QueueApplySelectionChanges()
    {
        if (IsHandleCreated && !IsDisposed)
        {
            BeginInvoke(new Action(ApplySelectionChanges));
        }
    }

    private void StartWithWindows_CheckedChanged(object? sender, EventArgs e)
    {
        if (_updatingUi) return;

        try
        {
            StartupRegistration.SetEnabled(_startWithWindows.Checked);
        }
        catch (Exception exception)
        {
            _updatingUi = true;
            try
            {
                _startWithWindows.Checked = StartupRegistration.IsEnabled();
            }
            finally
            {
                _updatingUi = false;
            }

            MessageBox.Show(
                this,
                "Could not update the startup setting.\n\n" + exception.Message,
                "CyberScarecrow",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private void MinimizeToTray_CheckedChanged(object? sender, EventArgs e)
    {
        if (_updatingUi) return;
        _config.MinimizeToTray = _minimizeToTray.Checked;
        _config.Save();
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        RefreshStatuses();

        if (!_config.PauseOnFullscreen)
        {
            _lastFullscreenWindow = IntPtr.Zero;
            return;
        }

        if (FullscreenDetector.TryGetFullscreenApp(out var app, out var window))
        {
            if (_armed && window != _lastFullscreenWindow)
            {
                _lastFullscreenWindow = window;
                Disarm();
                SetSubtitle(
                    $"Paused for fullscreen app ({app}). Re-arm when you're done.",
                    Theme.AccentDark);
                _tray.ShowBalloonTip(
                    3000,
                    "CyberScarecrow paused",
                    $"Fullscreen app detected ({app}). Decoys stopped to avoid anti-cheat false positives.",
                    ToolTipIcon.Info);
            }
        }
        else
        {
            _lastFullscreenWindow = IntPtr.Zero;
        }
    }

    private void DeepDisguise_CheckedChanged(object? sender, EventArgs e)
    {
        if (_updatingUi) return;

        if (_deepDisguise.Checked && !_config.DeepDisguiseWarningAccepted)
        {
            if (!ShowDeepDisguiseWarning())
            {
                _updatingUi = true;
                try
                {
                    _deepDisguise.Checked = false;
                }
                finally
                {
                    _updatingUi = false;
                }
                return;
            }

            _config.DeepDisguiseWarningAccepted = true;
        }

        _config.DeepDisguise = _deepDisguise.Checked;
        _config.Save();
        _manager.DeepDisguise = _deepDisguise.Checked;

        if (_armed)
        {
            // Restart the decoys so the new deployment mode takes effect.
            Disarm();
            Arm();
        }
        else
        {
            UpdateArmUi();
            RefreshStatuses();
        }
    }

    private void PauseOnFullscreen_CheckedChanged(object? sender, EventArgs e)
    {
        if (_updatingUi) return;

        _config.PauseOnFullscreen = _pauseOnFullscreen.Checked;
        _config.Save();

        if (!_pauseOnFullscreen.Checked)
        {
            _lastFullscreenWindow = IntPtr.Zero;
        }
    }

    private bool ShowDeepDisguiseWarning()
    {
        var result = MessageBox.Show(
            this,
            DeepDisguiseExplanation + "\n\nEnable deep disguise?",
            "Enable deep disguise?",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        return result == DialogResult.Yes;
    }

    private void ShowDeepDisguiseInfo()
    {
        MessageBox.Show(
            this,
            DeepDisguiseExplanation,
            "About deep disguise",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void ShowFromTray()
    {
        if (WindowState == FormWindowState.Minimized)
        {
            WindowState = FormWindowState.Normal;
        }

        Show();
        Activate();
        BringToFront();
    }

    private void HideToTray()
    {
        Hide();

        if (!_balloonShown)
        {
            _balloonShown = true;
            _tray.ShowBalloonTip(
                2500,
                "CyberScarecrow",
                _armed ? "Still armed in the background." : "Still running in the background.",
                ToolTipIcon.Info);
        }
    }

    private void ExitApp()
    {
        _reallyExit = true;
        Close();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);

        if (WindowState == FormWindowState.Minimized && _config.MinimizeToTray)
        {
            HideToTray();
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (e.Cancel) return;

        if (!_reallyExit &&
            _config.MinimizeToTray &&
            e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        Cleanup();
    }

    private void Cleanup()
    {
        if (_cleanedUp) return;
        _cleanedUp = true;

        _timer.Stop();
        _manager.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        _config.Save();
    }
}
