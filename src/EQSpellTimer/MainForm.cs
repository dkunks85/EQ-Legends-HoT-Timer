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
    private readonly DataGridView _grid = new() { Dock=DockStyle.Fill, AutoGenerateColumns=false, AllowUserToAddRows=true, AllowUserToDeleteRows=true, BackgroundColor=Color.FromArgb(31,35,42), BorderStyle=BorderStyle.None };
    private readonly System.Windows.Forms.Timer _uiTimer = new() { Interval=100 };
    private readonly Label _status = new() { AutoSize=true, ForeColor=Color.Silver, Text="Not watching" };

    public MainForm()
    {
        Text = "EQ Legends Spell Timer — C# Edition";
        Width=940; Height=760; MinimumSize=new Size(760,600); StartPosition=FormStartPosition.CenterScreen;
        BackColor=Color.FromArgb(24,27,33); ForeColor=Color.White;
        _spells = new BindingList<SpellDefinition>(_store.LoadSpells());
        var settings = _store.LoadSettings(); _logPath.Text=settings.LogPath;
        _engine = new TimerEngine(() => _spells.ToList(), CharacterName);
        _engine.Activity += message => Ui(() => Log(message));
        _engine.TimersChanged += () => Ui(RenderTimers);
        _tailer.LineReceived += line => _engine.Process(line);
        _tailer.Error += ex => Ui(() => Log("Watcher error: "+ex.Message));
        BuildUi(); BindGrid();
        _uiTimer.Tick += (_,_) => { _engine.RemoveExpired(DateTime.Now); UpdateCountdowns(); };
        _uiTimer.Start();
        FormClosing += (_,_) => { _tailer.Dispose(); SaveAll(); };
    }

    private void BuildUi()
    {
        var header = new TableLayoutPanel { Dock=DockStyle.Top, Height=46, ColumnCount=4, Padding=new Padding(10,8,10,4) };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.Controls.Add(new Label { Text="EQ log:", AutoSize=true, Anchor=AnchorStyles.Left, ForeColor=Color.Gainsboro, Padding=new Padding(0,6,5,0) },0,0);
        header.Controls.Add(_logPath,1,0);
        var browse = new Button { Text="Browse…", AutoSize=true }; browse.Click += Browse;
        header.Controls.Add(browse,2,0); header.Controls.Add(_watch,3,0); _watch.Click += async (_,_) => await ToggleWatchAsync();
        Controls.Add(header);

        var tabs = new TabControl { Dock=DockStyle.Fill };
        tabs.TabPages.Add(TimersTab()); tabs.TabPages.Add(SetupTab()); tabs.TabPages.Add(ActivityTab());
        Controls.Add(tabs); tabs.BringToFront();

        var footer = new Panel { Dock=DockStyle.Bottom, Height=28, Padding=new Padding(10,5,10,0), BackColor=Color.FromArgb(19,22,27) };
        footer.Controls.Add(_status); Controls.Add(footer);
    }

    private TabPage TimersTab()
    {
        var page=Page("Timers");
        var split=new SplitContainer { Dock=DockStyle.Fill, Orientation=Orientation.Horizontal, SplitterDistance=410, BackColor=BackColor };
        split.Panel1.Controls.Add(_hotPanel); split.Panel1.Controls.Add(Section("Healing-over-time", DockStyle.Top));
        split.Panel2.Controls.Add(_buffPanel); split.Panel2.Controls.Add(Section("Buffs", DockStyle.Top));
        page.Controls.Add(split); return page;
    }

    private TabPage SetupTab()
    {
        var page=Page("Spell Setup");
        var tools=new FlowLayoutPanel { Dock=DockStyle.Top, Height=42, Padding=new Padding(8), BackColor=Color.FromArgb(29,33,40) };
        var save=new Button { Text="Save Spells", AutoSize=true }; save.Click += (_,_) => SaveAll();
        var defaults=new Button { Text="Restore Defaults", AutoSize=true }; defaults.Click += (_,_) => { _spells.Clear(); foreach(var s in ConfigStore.Defaults()) _spells.Add(s); SaveAll(); };
        tools.Controls.Add(save); tools.Controls.Add(defaults); page.Controls.Add(_grid); page.Controls.Add(tools); return page;
    }

    private TabPage ActivityTab() { var p=Page("Activity Log"); p.Controls.Add(_activity); return p; }

    private void BindGrid()
    {
        _grid.DataSource=_spells;
        AddCheck(nameof(SpellDefinition.Enabled),"Enabled",60);
        AddText(nameof(SpellDefinition.Name),"Spell",150);
        AddCombo(nameof(SpellDefinition.Category),"Category",80,["HoT","Buff"]);
        AddText(nameof(SpellDefinition.Duration),"Duration",75);
        AddCombo(nameof(SpellDefinition.DetectionMode),"Detection",135,["Auto HoT Family","Landing Message"]);
        AddText(nameof(SpellDefinition.MatchName),"Match name",130);
        AddText(nameof(SpellDefinition.LandingPattern),"Landing pattern",250);
        AddText(nameof(SpellDefinition.FadePattern),"Fade pattern",220);
        _grid.DataError += (_,_) => { };
        _grid.CellEndEdit += (_,e) => { if (_grid.Columns[e.ColumnIndex].DataPropertyName==nameof(SpellDefinition.Name) && _grid.Rows[e.RowIndex].DataBoundItem is SpellDefinition s && string.IsNullOrWhiteSpace(s.MatchName)) s.MatchName=SpellNames.Base(s.Name); };
    }

    private async Task ToggleWatchAsync()
    {
        if (_tailer.IsRunning) { _tailer.Stop(); _watch.Text="Start Watching"; _status.Text="Not watching"; Log("Stopped watching"); return; }
        var path=_logPath.Text.Trim();
        if (!File.Exists(path)) { MessageBox.Show(this,"Choose a valid EverQuest log file first.","Log file",MessageBoxButtons.OK,MessageBoxIcon.Warning); return; }
        try { await _tailer.StartAsync(path,true); _watch.Text="Stop Watching"; _status.Text="Watching: "+Path.GetFileName(path); SaveAll(); Log("Watching "+path); }
        catch(Exception ex) { MessageBox.Show(this,ex.Message,"Could not open log",MessageBoxButtons.OK,MessageBoxIcon.Error); }
    }

    private void Browse(object? sender, EventArgs e)
    {
        using var dialog=new OpenFileDialog { Filter="EverQuest logs (eqlog_*.txt)|eqlog_*.txt|Text files (*.txt)|*.txt|All files (*.*)|*.*", FileName=Path.GetFileName(_logPath.Text), InitialDirectory=Directory.Exists(Path.GetDirectoryName(_logPath.Text)) ? Path.GetDirectoryName(_logPath.Text) : null };
        if (dialog.ShowDialog(this)==DialogResult.OK) _logPath.Text=dialog.FileName;
    }

    private void RenderTimers()
    {
        _hotPanel.SuspendLayout(); _buffPanel.SuspendLayout(); _hotPanel.Controls.Clear(); _buffPanel.Controls.Clear();
        foreach(var timer in _engine.Timers.OrderBy(t=>t.End))
        {
            if (timer.Category.Equals("HoT",StringComparison.OrdinalIgnoreCase)) _hotPanel.Controls.Add(HotCard(timer));
            else _buffPanel.Controls.Add(BuffRow(timer));
        }
        if (_hotPanel.Controls.Count==0) _hotPanel.Controls.Add(Empty("No active HoTs"));
        if (_buffPanel.Controls.Count==0) _buffPanel.Controls.Add(Empty("No active buffs"));
        _hotPanel.ResumeLayout(); _buffPanel.ResumeLayout();
    }

    private Control HotCard(ActiveTimer timer)
    {
        var panel=new Panel { Width=850, Height=88, Margin=new Padding(8), BackColor=Color.FromArgb(38,43,52), Tag=timer };
        var name=new Label { Text=$"{timer.Spell}  •  {timer.Target}"+(timer.Source!="You"?$"  •  cast by {timer.Source}":""), Left=14,Top=10,AutoSize=true,Font=new Font("Segoe UI Semibold",13),ForeColor=Color.White };
        var time=new Label { Name="Time",Left=745,Top=8,Width=88,TextAlign=ContentAlignment.MiddleRight,Font=new Font("Segoe UI Semibold",16),ForeColor=Color.FromArgb(120,225,150) };
        var bar=new ProgressBar { Name="Bar",Left=14,Top=52,Width=819,Height=19,Maximum=1000,Style=ProgressBarStyle.Continuous };
        panel.Controls.AddRange([name,time,bar]); return panel;
    }

    private Control BuffRow(ActiveTimer timer)
    {
        var panel=new Panel { Width=850,Height=43,Margin=new Padding(8,4,8,4),BackColor=Color.FromArgb(42,47,56),Tag=timer };
        panel.Controls.Add(new Label { Text=$"{timer.Spell}  —  {timer.Target}",Left=12,Top=11,AutoSize=true,ForeColor=Color.White });
        panel.Controls.Add(new Label { Name="Time",Left=755,Top=9,Width=78,TextAlign=ContentAlignment.MiddleRight,Font=new Font("Segoe UI Semibold",11),ForeColor=Color.FromArgb(130,195,255) }); return panel;
    }

    private void UpdateCountdowns()
    {
        foreach(Control parent in _hotPanel.Controls.Cast<Control>().Concat(_buffPanel.Controls.Cast<Control>()))
        {
            if (parent.Tag is not ActiveTimer timer) continue;
            var remaining=Math.Max(0,(timer.End-DateTime.Now).TotalSeconds);
            var time=parent.Controls.Find("Time",false).FirstOrDefault() as Label;
            if(time is not null) time.Text=timer.Category=="HoT"?$"{remaining:0.0}s":remaining>=60?$"{(int)remaining/60}:{(int)remaining%60:00}":$"{remaining:0}s";
            var bar=parent.Controls.Find("Bar",false).FirstOrDefault() as ProgressBar;
            if(bar is not null) bar.Value=Math.Clamp((int)(1000*remaining/Math.Max(1,timer.Duration)),0,1000);
        }
    }

    private string CharacterName()
    {
        var leaf=Path.GetFileNameWithoutExtension(_logPath.Text.Trim());
        var match=Regex.Match(leaf??"",@"^eqlog_([^_]+)_",RegexOptions.IgnoreCase); return match.Success?match.Groups[1].Value:"You";
    }
    private void SaveAll() { _store.SaveSpells(_spells); _store.SaveSettings(new AppSettings { LogPath=_logPath.Text.Trim() }); Log("Settings saved"); }
    private void Log(string message) { _activity.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}"); _activity.SelectionStart=_activity.TextLength; _activity.ScrollToCaret(); }
    private void Ui(Action action) { if(IsDisposed)return; if(InvokeRequired) BeginInvoke(action); else action(); }

    private static FlowLayoutPanel Flow()=>new(){Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true,Padding=new Padding(4),BackColor=Color.FromArgb(24,27,33)};
    private static TabPage Page(string text)=>new(text){BackColor=Color.FromArgb(24,27,33),ForeColor=Color.White};
    private static Label Section(string text,DockStyle dock)=>new(){Text=text,Dock=dock,Height=37,Padding=new Padding(10,9,0,0),Font=new Font("Segoe UI Semibold",12),ForeColor=Color.Gold,BackColor=Color.FromArgb(29,33,40)};
    private static Label Empty(string text)=>new(){Text=text,AutoSize=true,Margin=new Padding(12),ForeColor=Color.Gray,Font=new Font("Segoe UI",11,FontStyle.Italic)};
    private void AddText(string property,string header,int width)=>_grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName=property,HeaderText=header,Width=width });
    private void AddCheck(string property,string header,int width)=>_grid.Columns.Add(new DataGridViewCheckBoxColumn { DataPropertyName=property,HeaderText=header,Width=width });
    private void AddCombo(string property,string header,int width,string[] values)=>_grid.Columns.Add(new DataGridViewComboBoxColumn { DataPropertyName=property,HeaderText=header,Width=width,DataSource=values });
}
