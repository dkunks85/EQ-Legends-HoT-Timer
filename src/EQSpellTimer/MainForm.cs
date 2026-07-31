using System.ComponentModel;
using System.Text.RegularExpressions;

namespace EQSpellTimer;

public sealed class MainForm : Form
{
    private readonly ConfigStore _store = new();
    private readonly BindingList<SpellDefinition> _spells;
    private readonly LogTailer _tailer = new();
    private readonly TimerEngine _engine;
    private readonly TextBox _logPath = new() { Dock=DockStyle.Fill };
    private readonly Button _watch = new() { Text="Start Watching", AutoSize=true };
    private readonly FlowLayoutPanel _hotPanel = Flow();
    private readonly FlowLayoutPanel _buffPanel = Flow();
    private readonly TextBox _activity = new() { Dock=DockStyle.Fill, Multiline=true, ReadOnly=true, ScrollBars=ScrollBars.Vertical, BackColor=Color.FromArgb(26,29,35), ForeColor=Color.Gainsboro, Font=new Font("Consolas",10) };
    private readonly DataGridView _grid = new()
    {
        Dock=DockStyle.Fill,
        AutoGenerateColumns=false,
        AllowUserToAddRows=true,
        AllowUserToDeleteRows=true,
        BackgroundColor=Color.FromArgb(31,35,42),
        BorderStyle=BorderStyle.None,
        ScrollBars=ScrollBars.Both,
        AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.None
    };
    private readonly System.Windows.Forms.Timer _uiTimer = new() { Interval=100 };
    private readonly Label _status = new() { AutoSize=true, ForeColor=Color.Silver, Text="Not watching" };
    private readonly CheckBox _learnHotDurations = new() { Text="Learn HoT durations by rank", AutoSize=true, ForeColor=Color.White, Checked=true, Padding=new Padding(8,4,0,0) };
    private SplitContainer? _timerSplit;

    public MainForm()
    {
        Text = "EQ Legends Companion";
        Width=940;
        Height=760;
        MinimumSize=new Size(640,500);
        StartPosition=FormStartPosition.CenterScreen;
        BackColor=Color.FromArgb(24,27,33);
        ForeColor=Color.White;

        _spells = new BindingList<SpellDefinition>(_store.LoadSpells());

        var settings = _store.LoadSettings();
        _logPath.Text=settings.LogPath;
        _learnHotDurations.Checked = settings.LearnHotDurations;
        Width = Math.Max(MinimumSize.Width, settings.WindowWidth);
        Height = Math.Max(MinimumSize.Height, settings.WindowHeight);

        _engine = new TimerEngine(
            () => _spells.ToList(),
            CharacterName,
            _store.AppDirectory)
        {
            LearnHotDurations = _learnHotDurations.Checked
        };

        _engine.Activity += message => Ui(() => Log(message));
        _engine.TimersChanged += () => Ui(RenderTimers);

        _tailer.LineReceived += line => _engine.Process(line);
        _tailer.Error += ex => Ui(() => Log("Watcher error: "+ex.Message));

        BuildUi();
        BindGrid();
        StyleSpellGrid();
        _learnHotDurations.CheckedChanged += (_,_) =>
            _engine.LearnHotDurations = _learnHotDurations.Checked;

        _hotPanel.ClientSizeChanged += (_,_) => ResizeTimerCards();
        _buffPanel.ClientSizeChanged += (_,_) => ResizeTimerCards();

        Shown += (_,_) =>
        {
            RenderTimers();
            ResizeTimerCards();
        };

        _uiTimer.Tick += (_,_) =>
        {
            _engine.RemoveExpired(DateTime.Now);
            UpdateCountdowns();
        };

        _uiTimer.Start();

        FormClosing += (_,_) =>
        {
            _tailer.Dispose();
            SaveAll();
        };
    }

    private void BuildUi()
    {
        var header = new TableLayoutPanel { Dock=DockStyle.Top, Height=46, ColumnCount=4, Padding=new Padding(10,8,10,4) };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        header.Controls.Add(new Label { Text="EQ log:", AutoSize=true, Anchor=AnchorStyles.Left, ForeColor=Color.Gainsboro, Padding=new Padding(0,6,5,0) },0,0);
        header.Controls.Add(_logPath,1,0);

        var browse = new Button { Text="Browse…", AutoSize=true };
        browse.Click += Browse;

        header.Controls.Add(browse,2,0);
        header.Controls.Add(_watch,3,0);

        _watch.Click += async (_,_) => await ToggleWatchAsync();

        Controls.Add(header);

        var tabs = new TabControl { Dock=DockStyle.Fill };
        tabs.TabPages.Add(TimersTab());
        tabs.TabPages.Add(SetupTab());
        tabs.TabPages.Add(ActivityTab());

        Controls.Add(tabs);
        tabs.BringToFront();

        var footer = new Panel { Dock=DockStyle.Bottom, Height=28, Padding=new Padding(10,5,10,0), BackColor=Color.FromArgb(19,22,27) };
        footer.Controls.Add(_status);
        Controls.Add(footer);
    }

    private void StyleSpellGrid()
    {
        _grid.EnableHeadersVisualStyles = false;
        _grid.BackgroundColor = Color.FromArgb(31, 35, 42);
        _grid.GridColor = Color.FromArgb(70, 75, 85);

        _grid.DefaultCellStyle.BackColor = Color.FromArgb(31, 35, 42);
        _grid.DefaultCellStyle.ForeColor = Color.White;
        _grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(0, 110, 190);
        _grid.DefaultCellStyle.SelectionForeColor = Color.White;

        _grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(36, 40, 48);

        _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(24, 27, 33);
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
        _grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(24, 27, 33);

        _grid.RowHeadersDefaultCellStyle.BackColor = Color.FromArgb(24, 27, 33);
        _grid.RowHeadersDefaultCellStyle.ForeColor = Color.White;

        _grid.RowsDefaultCellStyle.BackColor = Color.FromArgb(31, 35, 42);
        _grid.RowsDefaultCellStyle.ForeColor = Color.White;

        _grid.DefaultCellStyle.Font = new Font("Segoe UI", 10);
        _grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI Semibold", 10);
    }

    private TabPage TimersTab()
    {
        var page=Page("Timers");

        _timerSplit=new SplitContainer
        {
            Dock=DockStyle.Fill,
            Orientation=Orientation.Horizontal,
            SplitterDistance=350,
            BackColor=BackColor,
            Panel1MinSize=130,
            Panel2MinSize=90
        };

        _timerSplit.Panel1.Controls.Add(_hotPanel);
        _timerSplit.Panel1.Controls.Add(Section("Healing-over-time", DockStyle.Top));

        _timerSplit.Panel2.Controls.Add(_buffPanel);
        _timerSplit.Panel2.Controls.Add(Section("Buffs", DockStyle.Top));

        page.Controls.Add(_timerSplit);
        return page;
    }

    private TabPage SetupTab()
    {
        var page=Page("Spell Setup");

        var tools=new FlowLayoutPanel
        {
            Dock=DockStyle.Top,
            Height=42,
            Padding=new Padding(8),
            BackColor=Color.FromArgb(29,33,40)
        };

        var save=new Button { Text="Save Spells", AutoSize=true };
        save.Click += (_,_) => SaveAll();

        var defaults=new Button { Text="Restore Defaults", AutoSize=true };
        defaults.Click += (_,_) =>
        {
            _spells.Clear();

            foreach(var s in ConfigStore.Defaults())
                _spells.Add(s);

            SaveAll();
        };

        tools.Controls.Add(save);
        tools.Controls.Add(defaults);
        tools.Controls.Add(_learnHotDurations);
        tools.Controls.Add(new Label
        {
            Text = "Target landing: type only the text after the target name",
            AutoSize = true,
            ForeColor = Color.Silver,
            Padding = new Padding(12, 6, 0, 0)
        });

        page.Controls.Add(_grid);
        page.Controls.Add(tools);

        return page;
    }

    private TabPage ActivityTab()
    {
        var p=Page("Activity Log");
        p.Controls.Add(_activity);
        return p;
    }

    private void BindGrid()
    {
        _grid.DataSource=_spells;

        AddCheck(nameof(SpellDefinition.Enabled),"Enabled",60);
        AddText(nameof(SpellDefinition.Name),"Spell",150);
        AddCombo(nameof(SpellDefinition.Category),"Category",80,["HoT","Buff"]);
        AddText(nameof(SpellDefinition.Duration),"Duration",75);
        AddCombo(nameof(SpellDefinition.DetectionMode),"Detection",135,["Auto HoT Family","Landing Message"]);
        AddText(nameof(SpellDefinition.MatchName),"Match name",130);
        AddText(
            nameof(SpellDefinition.SelfLandingPattern),
            "Self landing",
            260);

        AddText(
            nameof(SpellDefinition.TargetLandingPattern),
            "Target landing (name implied)",
            300);

        _grid.DataError += (_,_) => { };

        _grid.CellEndEdit += (_,e) =>
        {
            if (_grid.Columns[e.ColumnIndex].DataPropertyName==nameof(SpellDefinition.Name) &&
                _grid.Rows[e.RowIndex].DataBoundItem is SpellDefinition s &&
                string.IsNullOrWhiteSpace(s.MatchName))
            {
                s.MatchName=SpellNames.Base(s.Name);
            }
        };
    }

    private async Task ToggleWatchAsync()
    {
        if (_tailer.IsRunning)
        {
            _tailer.Stop();
            _watch.Text="Start Watching";
            _status.Text="Not watching";
            Log("Stopped watching");
            return;
        }

        var path=_logPath.Text.Trim();

        if (!File.Exists(path))
        {
            MessageBox.Show(this,"Choose a valid EverQuest log file first.","Log file",MessageBoxButtons.OK,MessageBoxIcon.Warning);
            return;
        }

        try
        {
            await _tailer.StartAsync(path,true);
            _watch.Text="Stop Watching";
            _status.Text="Watching: "+Path.GetFileName(path);
            SaveAll();
            Log("Watching "+path);
        }
        catch(Exception ex)
        {
            MessageBox.Show(this,ex.Message,"Could not open log",MessageBoxButtons.OK,MessageBoxIcon.Error);
        }
    }

    private void Browse(object? sender, EventArgs e)
    {
        using var dialog=new OpenFileDialog
        {
            Filter="EverQuest logs (eqlog_*.txt)|eqlog_*.txt|Text files (*.txt)|*.txt|All files (*.*)|*.*",
            FileName=Path.GetFileName(_logPath.Text),
            InitialDirectory=Directory.Exists(Path.GetDirectoryName(_logPath.Text))
                ? Path.GetDirectoryName(_logPath.Text)
                : null
        };

        if (dialog.ShowDialog(this)==DialogResult.OK)
            _logPath.Text=dialog.FileName;
    }

    private void RenderTimers()
    {
        _hotPanel.SuspendLayout();
        _buffPanel.SuspendLayout();

        _hotPanel.Controls.Clear();
        _buffPanel.Controls.Clear();

        foreach(var timer in _engine.Timers.OrderBy(t=>t.End))
        {
            Control card = timer.Category.Equals("HoT",StringComparison.OrdinalIgnoreCase)
                ? HotCard(timer)
                : BuffRow(timer);

            card.Visible = true;

            if (timer.Category.Equals("HoT",StringComparison.OrdinalIgnoreCase))
                _hotPanel.Controls.Add(card);
            else
                _buffPanel.Controls.Add(card);
        }

        if (_hotPanel.Controls.Count==0)
            _hotPanel.Controls.Add(Empty("No active HoTs"));

        if (_buffPanel.Controls.Count==0)
            _buffPanel.Controls.Add(Empty("No active buffs"));

        _hotPanel.ResumeLayout(performLayout:true);
        _buffPanel.ResumeLayout(performLayout:true);

        ResizeTimerCards();

        _hotPanel.PerformLayout();
        _buffPanel.PerformLayout();

        _hotPanel.Invalidate(true);
        _buffPanel.Invalidate(true);

        _hotPanel.Refresh();
        _buffPanel.Refresh();
    }

    private Control HotCard(ActiveTimer timer)
    {
        var width = TimerCardWidth(_hotPanel);
        var familyColor = HotFamilyColor(timer.BaseName);

        var panel = new Panel
        {
            Width = width,
            MinimumSize = new Size(280, 88),
            Height = 88,
            Margin = new Padding(8),
            BackColor = Color.FromArgb(38, 43, 52),
            Tag = timer,
            Visible = true
        };

        var accent = new Panel
        {
            Name = "Accent",
            Left = 0,
            Top = 0,
            Width = 6,
            Height = panel.Height,
            BackColor = familyColor,
            Anchor = AnchorStyles.Left |
                     AnchorStyles.Top |
                     AnchorStyles.Bottom
        };

        var name = new Label
        {
            Text =
                $"{timer.Spell}  •  {timer.Target}" +
                (timer.Source != CharacterName()
                    ? $"  •  cast by {timer.Source}"
                    : ""),
            Left = 16,
            Top = 10,
            AutoEllipsis = true,
            Width = Math.Max(180, panel.Width - 132),
            Height = 28,
            Font = new Font("Segoe UI Semibold", 13),
            ForeColor = Color.White,
            Anchor = AnchorStyles.Left |
                     AnchorStyles.Right |
                     AnchorStyles.Top
        };

        var time = new Label
        {
            Name = "Time",
            Left = panel.Width - 102,
            Top = 8,
            Width = 88,
            Height = 30,
            TextAlign = ContentAlignment.MiddleRight,
            Font = new Font("Segoe UI Semibold", 16),
            ForeColor = familyColor,
            Anchor = AnchorStyles.Top |
                     AnchorStyles.Right
        };

        var barBack = new Panel
        {
            Name = "BarBack",
            Left = 16,
            Top = 52,
            Width = Math.Max(100, panel.Width - 30),
            Height = 19,
            BackColor = Color.FromArgb(24, 27, 33),
            Anchor = AnchorStyles.Left |
                     AnchorStyles.Right |
                     AnchorStyles.Top
        };

        var barFill = new Panel
        {
            Name = "BarFill",
            Left = 0,
            Top = 0,
            Width = barBack.Width,
            Height = barBack.Height,
            BackColor = familyColor,
            Anchor = AnchorStyles.Left |
                     AnchorStyles.Top |
                     AnchorStyles.Bottom
        };

        barBack.Controls.Add(barFill);
        panel.Controls.Add(accent);
        panel.Controls.Add(name);
        panel.Controls.Add(time);
        panel.Controls.Add(barBack);

        return panel;
    }

    private Control BuffRow(ActiveTimer timer)
    {
        var width = TimerCardWidth(_buffPanel);

        var panel=new Panel
        {
            Width=width,
            MinimumSize=new Size(280,43),
            Height=43,
            Margin=new Padding(8,4,8,4),
            BackColor=Color.FromArgb(42,47,56),
            Tag=timer,
            Visible=true
        };

        panel.Controls.Add(new Label
        {
            Text=$"{timer.Spell}  —  {timer.Target}",
            Left=12,
            Top=11,
            Width=Math.Max(160,panel.Width-110),
            Height=24,
            AutoEllipsis=true,
            ForeColor=Color.White,
            Anchor=AnchorStyles.Left|AnchorStyles.Right|AnchorStyles.Top
        });

        panel.Controls.Add(new Label
        {
            Name="Time",
            Left=panel.Width-90,
            Top=9,
            Width=78,
            Height=24,
            TextAlign=ContentAlignment.MiddleRight,
            Font=new Font("Segoe UI Semibold",11),
            ForeColor=Color.FromArgb(130,195,255),
            Anchor=AnchorStyles.Top|AnchorStyles.Right
        });

        return panel;
    }

    private void UpdateCountdowns()
    {
        foreach (Control parent in _hotPanel.Controls.Cast<Control>()
            .Concat(_buffPanel.Controls.Cast<Control>()))
        {
            if (parent.Tag is not ActiveTimer timer)
                continue;

            var remaining = Math.Max(
                0,
                (timer.End - DateTime.Now).TotalSeconds);

            var time = parent.Controls
                .Find("Time", false)
                .FirstOrDefault() as Label;

            if (time is not null)
            {
                time.Text = timer.Category.Equals(
                        "HoT",
                        StringComparison.OrdinalIgnoreCase)
                    ? $"{remaining:0.0}s"
                    : remaining >= 60
                        ? $"{(int)remaining / 60}:{(int)remaining % 60:00}"
                        : $"{remaining:0}s";
            }

            if (!timer.Category.Equals(
                    "HoT",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var familyColor = HotFamilyColor(timer.BaseName);
            var isNature = IsNatureHot(timer.BaseName);
            var shouldPulse = isNature &&
                              remaining > 0 &&
                              remaining <= 5;

            var displayColor = shouldPulse
                ? NatureWarningPulse(familyColor)
                : familyColor;

            if (time is not null)
                time.ForeColor = displayColor;

            var accent = parent.Controls
                .Find("Accent", false)
                .FirstOrDefault() as Panel;

            if (accent is not null)
                accent.BackColor = displayColor;

            var barBack = parent.Controls
                .Find("BarBack", true)
                .FirstOrDefault() as Panel;

            var barFill = parent.Controls
                .Find("BarFill", true)
                .FirstOrDefault() as Panel;

            if (barBack is not null && barFill is not null)
            {
                var fraction = Math.Clamp(
                    remaining / Math.Max(1, timer.Duration),
                    0,
                    1);

                barFill.Width = Math.Clamp(
                    (int)Math.Round(barBack.ClientSize.Width * fraction),
                    0,
                    barBack.ClientSize.Width);

                barFill.BackColor = displayColor;
            }

            parent.BackColor = shouldPulse
                ? BlendColor(
                    Color.FromArgb(38, 43, 52),
                    Color.FromArgb(105, 28, 28),
                    WarningPulseAmount())
                : Color.FromArgb(38, 43, 52);
        }
    }

    private static Color HotFamilyColor(string baseName)
    {
        var spell = SpellNames.Base(baseName);

        if (IsDruidHot(spell))
            return Color.FromArgb(76, 175, 80);

        if (IsShamanHot(spell))
            return Color.FromArgb(59, 130, 246);

        if (EngineContext.IsClericHotName(spell))
            return Color.FromArgb(250, 204, 21);

        return Color.FromArgb(160, 170, 185);
    }

    private static bool IsNatureHot(string baseName)
    {
        var spell = SpellNames.Base(baseName);
        return IsDruidHot(spell) || IsShamanHot(spell);
    }

    private static bool IsDruidHot(string spell)
    {
        return
            spell.Equals("Budding Heal", StringComparison.OrdinalIgnoreCase) ||
            spell.Equals("Sprouting Heal", StringComparison.OrdinalIgnoreCase) ||
            spell.Equals("Flowering Heal", StringComparison.OrdinalIgnoreCase) ||
            spell.Equals("Blooming Heal", StringComparison.OrdinalIgnoreCase) ||
            spell.Equals("Blossoming Heal", StringComparison.OrdinalIgnoreCase) ||
            spell.Equals("Efflorescing Heal", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsShamanHot(string spell)
    {
        return
            spell.Equals("Snails Healing", StringComparison.OrdinalIgnoreCase) ||
            spell.Equals("Tortoises Healing", StringComparison.OrdinalIgnoreCase) ||
            spell.Equals("Slugs Healing", StringComparison.OrdinalIgnoreCase);
    }

    private static Color NatureWarningPulse(Color normalColor)
    {
        return BlendColor(
            normalColor,
            Color.FromArgb(235, 65, 65),
            WarningPulseAmount());
    }

    private static double WarningPulseAmount()
    {
        // Smooth two-second pulse: normal -> red -> normal.
        var phase = DateTime.Now.TimeOfDay.TotalMilliseconds / 2000.0;
        return (Math.Sin(phase * Math.PI * 2) + 1) / 2;
    }

    private static Color BlendColor(
        Color from,
        Color to,
        double amount)
    {
        amount = Math.Clamp(amount, 0, 1);

        return Color.FromArgb(
            (int)Math.Round(from.R + (to.R - from.R) * amount),
            (int)Math.Round(from.G + (to.G - from.G) * amount),
            (int)Math.Round(from.B + (to.B - from.B) * amount));
    }

    private int TimerCardWidth(FlowLayoutPanel panel)
    {
        var scrollbar = panel.VerticalScroll.Visible
            ? SystemInformation.VerticalScrollBarWidth
            : 0;

        return Math.Max(
            280,
            panel.ClientSize.Width -
            panel.Padding.Horizontal -
            20 -
            scrollbar);
    }

    private void ResizeTimerCards()
    {
        ResizeCardsIn(_hotPanel);
        ResizeCardsIn(_buffPanel);
    }

    private void ResizeCardsIn(FlowLayoutPanel panel)
    {
        var width = TimerCardWidth(panel);

        panel.SuspendLayout();

        foreach (Control control in panel.Controls)
        {
            if (control.Tag is not ActiveTimer)
                continue;

            control.Width = width;
            control.Visible = true;
        }

        panel.ResumeLayout(performLayout:true);
        panel.PerformLayout();
        panel.Invalidate(true);
    }

    private string CharacterName()
    {
        var leaf=Path.GetFileNameWithoutExtension(_logPath.Text.Trim());
        var match=Regex.Match(leaf??"",@"^eqlog_([^_]+)_",RegexOptions.IgnoreCase);

        return match.Success
            ?match.Groups[1].Value
            :"You";
    }

    private void SaveAll()
    {
        _store.SaveSpells(_spells);

        _store.SaveSettings(new AppSettings
        {
            LogPath=_logPath.Text.Trim(),
            LearnHotDurations=_learnHotDurations.Checked,
            WindowWidth=Width,
            WindowHeight=Height
        });

        Log("Settings saved");
    }

    private void Log(string message)
    {
        _activity.AppendText(
            $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");

        _activity.SelectionStart=_activity.TextLength;
        _activity.ScrollToCaret();
    }

    private void Ui(Action action)
    {
        if(IsDisposed)
            return;

        if(InvokeRequired)
            BeginInvoke(action);
        else
            action();
    }

    private static FlowLayoutPanel Flow()=>new()
    {
        Dock=DockStyle.Fill,
        FlowDirection=FlowDirection.TopDown,
        WrapContents=false,
        AutoScroll=true,
        Padding=new Padding(4),
        BackColor=Color.FromArgb(24,27,33)
    };

    private static TabPage Page(string text)=>new(text)
    {
        BackColor=Color.FromArgb(24,27,33),
        ForeColor=Color.White
    };

    private static Label Section(string text,DockStyle dock)=>new()
    {
        Text=text,
        Dock=dock,
        Height=37,
        Padding=new Padding(10,9,0,0),
        Font=new Font("Segoe UI Semibold",12),
        ForeColor=Color.Gold,
        BackColor=Color.FromArgb(29,33,40)
    };

    private static Label Empty(string text)=>new()
    {
        Text=text,
        AutoSize=true,
        Margin=new Padding(12),
        ForeColor=Color.Gray,
        Font=new Font("Segoe UI",11,FontStyle.Italic)
    };

    private void AddText(string property,string header,int width)=>
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName=property,
            HeaderText=header,
            Width=width
        });

    private void AddCheck(string property,string header,int width)=>
        _grid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            DataPropertyName=property,
            HeaderText=header,
            Width=width
        });

    private void AddCombo(string property,string header,int width,string[] values)=>
        _grid.Columns.Add(new DataGridViewComboBoxColumn
        {
            DataPropertyName=property,
            HeaderText=header,
            Width=width,
            DataSource=values
        });
}
