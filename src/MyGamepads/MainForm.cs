using System.Drawing.Drawing2D;

static class Theme
{
    public static readonly Color Background = Color.FromArgb(19, 24, 34), Card = Color.FromArgb(29, 37, 51), Muted = Color.FromArgb(165, 181, 202), Accent = Color.FromArgb(91, 222, 175);
    public static Label Label(string text, float size = 10, bool bold = false) => new()
    {
        Text = text,
        AutoSize = true,
        Dock = DockStyle.Fill,
        ForeColor = Color.White,
        Font = new Font(bold ? "Segoe UI Semibold" : "Segoe UI", size),
        Margin = new Padding(0, 0, 0, 8),
        UseMnemonic = false
    };
    public static TableLayoutPanel Stack() => new() { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, ColumnCount = 1, Margin = Padding.Empty, Padding = Padding.Empty };
}

sealed class MainForm : Form
{
    readonly Label status = Theme.Label("Ожидаю подключения…");
    readonly TableLayoutPanel cards = new() { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, ColumnCount = 2, Margin = new Padding(0, 12, 0, 20) };
    readonly Button toggle = new(), trayButton = new();
    readonly Button installDriver = new();
    readonly Label driverInfo = Theme.Label("", 9);
    readonly NotifyIcon tray = new();
    readonly System.Windows.Forms.Timer timer = new() { Interval = 100 };
    Bridge? bridge;
    bool closing, busy;
    string? connectionError;
    public MainForm(bool startBridge = true)
    {
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        Text = "Мои геймпады"; Font = new Font("Segoe UI", 10); Icon = AppIcon.Value;
        ClientSize = new(800, 620); MinimumSize = new(720, 580);
        BackColor = Theme.Background; ForeColor = Color.White; StartPosition = FormStartPosition.CenterScreen;
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(28) };
        var content = Theme.Stack();
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.Controls.Add(Theme.Label("Мои геймпады", 25, true));
        var subtitle = Theme.Label("Подключите контроллеры — и запускайте игру."); subtitle.ForeColor = Theme.Muted;
        content.Controls.Add(subtitle);
        var driverBar = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, ColumnCount = 2, Margin = new Padding(0, 8, 0, 8) };
        driverBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); driverBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        driverInfo.Text = DriverSetup.GetStatus().Description; driverInfo.ForeColor = Theme.Muted; driverInfo.TextAlign = ContentAlignment.MiddleLeft; driverInfo.Margin = new Padding(0, 0, 12, 0);
        installDriver.Text = "Установить драйвер"; installDriver.AutoSize = true; installDriver.AutoSizeMode = AutoSizeMode.GrowAndShrink; installDriver.Padding = new Padding(12, 8, 12, 8);
        installDriver.FlatStyle = FlatStyle.Flat; installDriver.FlatAppearance.BorderColor = Color.FromArgb(75, 94, 119); installDriver.BackColor = Color.FromArgb(38, 51, 69); installDriver.ForeColor = Color.White; installDriver.Cursor = Cursors.Hand; installDriver.Margin = Padding.Empty;
        installDriver.AccessibleDescription = "Открыть официальный установщик ViGEmBus. Потребуется подтверждение Windows.";
        driverBar.Controls.Add(driverInfo, 0, 0); driverBar.Controls.Add(installDriver, 1, 0); content.Controls.Add(driverBar);
        status.ForeColor = Theme.Accent; status.Margin = new Padding(0, 8, 0, 0); content.Controls.Add(status);
        cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        content.Controls.Add(cards);
        var help = Theme.Stack();
        help.Margin = new Padding(0, 0, 0, 18);
        help.Controls.Add(Theme.Label("Всё готово для игры", 11, true));
        var instructions = Theme.Label("Дождитесь статуса «Работает» на подключённых геймпадах.\nКнопка Options соответствует Start, а Share — Back."); instructions.ForeColor = Theme.Muted;
        help.Controls.Add(instructions); content.Controls.Add(help);
        var actions = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, Margin = Padding.Empty, WrapContents = true };
        toggle.Text = "Остановить"; trayButton.Text = "Свернуть в трей";
        foreach (var b in new[] { toggle, trayButton })
        {
            b.AutoSize = true; b.AutoSizeMode = AutoSizeMode.GrowAndShrink; b.Padding = new Padding(19, 10, 19, 10);
            b.Margin = new Padding(0, 0, 12, 8); b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderColor = Color.FromArgb(75, 94, 119);
            b.BackColor = Color.FromArgb(38, 51, 69); b.ForeColor = Color.White; b.Cursor = Cursors.Hand;
            actions.Controls.Add(b);
        }
        var licenses = new LinkLabel { Text = "Лицензии", AutoSize = true, LinkColor = Theme.Accent, ActiveLinkColor = Color.White, VisitedLinkColor = Theme.Accent, Margin = new Padding(0, 15, 0, 8) };
        licenses.LinkClicked += (_, _) => ShowLicenses(); actions.Controls.Add(licenses);
        content.Controls.Add(actions);
        scroll.Controls.Add(content); Controls.Add(scroll);
        tray.Icon = AppIcon.Value; tray.Text = "Мои геймпады";
        var menu = new ContextMenuStrip(); menu.Items.Add("Открыть", null, (_, _) => ShowWindow()); menu.Items.Add("Выход", null, (_, _) => Close()); tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => ShowWindow();
        trayButton.Click += (_, _) => { tray.Visible = true; Hide(); };
        Resize += (_, _) => { if (WindowState == FormWindowState.Minimized) { tray.Visible = true; Hide(); } };
        toggle.Click += async (_, _) => { if (busy) return; busy = true; toggle.Enabled = installDriver.Enabled = false; if (bridge != null) { var old = bridge; bridge = null; await old.DisposeAsync(); connectionError = null; toggle.Text = "Включить"; } else StartBridge(); toggle.Enabled = installDriver.Enabled = true; busy = false; RefreshCards(); };
        installDriver.Click += async (_, _) => await InstallDriver();
        timer.Tick += (_, _) => RefreshCards();
        Shown += (_, _) => { if (startBridge) { StartBridge(); timer.Start(); } };
        FormClosing += async (_, e) => { if (closing) return; e.Cancel = true; if (busy) return; busy = true; timer.Stop(); toggle.Enabled = false; if (bridge != null) await bridge.DisposeAsync(); tray.Visible = false; tray.Dispose(); closing = true; Close(); };
        RefreshCards(); ResumeLayout(true);
    }
    void ShowWindow() { Show(); WindowState = FormWindowState.Normal; Activate(); }
    void ShowLicenses()
    {
        static string Read(string resource) { using var s = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)!; using var reader = new StreamReader(s); return reader.ReadToEnd(); }
        using var dialog = new Form { Text = "Лицензии · Мои геймпады", Icon = AppIcon.Value, Size = new Size(760, 560), StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false };
        var text = new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font("Consolas", 9), Text = Read("MyGamepads.License.txt") + Environment.NewLine + Read("MyGamepads.Notices.txt") };
        dialog.Controls.Add(text); dialog.ShowDialog(this);
    }
    void StartBridge()
    {
        connectionError = null;
        try { bridge = new Bridge(); bridge.Start(); toggle.Text = "Остановить"; }
        catch (Exception e) { bridge = null; connectionError = "Нет подключения к ViGEmBus. Нажмите «Установить драйвер»."; Log.Write(e.ToString()); toggle.Text = "Повторить"; }
    }
    async Task InstallDriver()
    {
        if (busy) return;
        busy = true; toggle.Enabled = installDriver.Enabled = false;
        var old = bridge; bridge = null; connectionError = "Открыт установщик драйвера. Подтвердите запрос Windows.";
        bool resume = old != null;
        try
        {
            if (old != null) await old.DisposeAsync();
            var result = await DriverSetup.InstallAsync();
            Log.Write($"Driver installer exited with {result.ExitCode}");
            if (result.NeedsRestart) { connectionError = "Драйвер установлен. Сохраните работу и перезагрузите Windows."; resume = false; }
            else if (result.Cancelled) { connectionError = "Установка отменена."; }
            else if (result.Success) { resume = true; connectionError = null; }
            else { connectionError = $"Установщик завершился с кодом {result.ExitCode}. Подробнее в журнале."; resume = false; }
        }
        catch (Exception e) { connectionError = $"Установка не выполнена: {e.Message}"; Log.Write(e.ToString()); }
        finally
        {
            driverInfo.Text = DriverSetup.GetStatus().Description;
            if (resume) { var message = connectionError; StartBridge(); if (message != null) driverInfo.Text = message + " " + driverInfo.Text; }
            else toggle.Text = "Повторить";
            toggle.Enabled = installDriver.Enabled = true; busy = false; RefreshCards();
        }
    }
    void RefreshCards()
    {
        var pads = bridge?.Snapshot() ?? [];
        status.Text = bridge == null ? connectionError ?? "Остановлено — виртуальные геймпады отключены" : bridge.Error.Length > 0 ? bridge.Error : pads.Length == 0 ? "Жду ваши геймпады… Подключите USB или Bluetooth." : $"Подключено: {pads.Length}  ·  Работает: {pads.Count(p => p.Reports > 0 && p.Status == "Работает")}";
        ShowPads(pads);
    }
    internal void ShowPads(PadView[] pads)
    {
        int total = Math.Max(2, pads.Length);
        if (cards.Controls.Count != total)
        {
            cards.SuspendLayout();
            foreach (Control c in cards.Controls.Cast<Control>().ToArray()) c.Dispose();
            cards.Controls.Clear(); cards.RowCount = (total + 1) / 2;
            for (int i = 0; i < total; i++) { var c = new PadCard { Margin = new Padding(i % 2 == 0 ? 0 : 8, 0, i % 2 == 0 ? 8 : 0, 12) }; cards.Controls.Add(c, i % 2, i / 2); }
            cards.ResumeLayout(true);
        }
        for (int i = 0; i < total; i++) ((PadCard)cards.Controls[i]).Set(i < pads.Length ? pads[i] : null, i + 1);
    }
}

sealed class PadCard : UserControl
{
    readonly Label title = Theme.Label("", 13, true), transport = Theme.Label("", 9), state = Theme.Label(""), buttons = Theme.Label(""), triggers = Theme.Label("", 9);
    readonly StickDisplay sticks = new();
    readonly ToolTip tip = new();
    public PadCard()
    {
        AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink; Dock = DockStyle.Fill;
        BackColor = Theme.Card; Padding = new Padding(18);
        var body = Theme.Stack(); body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var header = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, ColumnCount = 2, Margin = Padding.Empty };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        transport.ForeColor = Theme.Muted; transport.TextAlign = ContentAlignment.MiddleRight; transport.Margin = new Padding(8, 0, 0, 8);
        header.Controls.Add(title, 0, 0); header.Controls.Add(transport, 1, 0); body.Controls.Add(header);
        state.ForeColor = Theme.Accent; body.Controls.Add(state);
        buttons.ForeColor = Theme.Muted; buttons.Margin = new Padding(0, 8, 0, 8); body.Controls.Add(buttons);
        triggers.ForeColor = Theme.Muted; body.Controls.Add(triggers);
        body.Controls.Add(sticks);
        Controls.Add(body);
    }
    public void Set(PadView? pad, int n)
    {
        title.Text = $"Геймпад {pad?.Slot ?? n}";
        transport.Text = pad?.Transport ?? "—";
        state.Text = pad?.Status ?? "Ожидаю подключения";
        state.ForeColor = pad?.Status == "Работает" ? Theme.Accent : Theme.Muted;
        buttons.Text = pad == null ? "Кнопки: —" : $"Кнопки: {Pressed(pad.State.Buttons)}";
        triggers.Text = pad == null ? "L2: —     R2: —" : $"L2: {pad.State.LT * 100 / 255}%     R2: {pad.State.RT * 100 / 255}%";
        sticks.Set(pad?.State ?? default, pad != null);
        var access = pad == null ? "Геймпад не подключён" : pad.Exclusive ? "Эксклюзивный доступ к устройству" : "Общий доступ к устройству. Если игра обрабатывает ввод дважды, закройте другие программы для геймпадов.";
        if (tip.GetToolTip(transport) != access) tip.SetToolTip(transport, access);
        AccessibleName = $"{title.Text}, {transport.Text}, {state.Text}";
    }
    static string Pressed(ushort b)
    {
        (int Mask, string Name)[] names = [(1, "↑"), (2, "↓"), (4, "←"), (8, "→"), (0x10, "Start"), (0x20, "Back"), (0x40, "L3"), (0x80, "R3"), (0x100, "LB"), (0x200, "RB"), (0x400, "PS"), (0x1000, "A"), (0x2000, "B"), (0x4000, "X"), (0x8000, "Y")];
        var pressed = string.Join(" · ", names.Where(x => (b & x.Mask) != 0).Select(x => x.Name));
        return pressed.Length == 0 ? "не нажаты" : pressed;
    }
    protected override void Dispose(bool disposing) { if (disposing) tip.Dispose(); base.Dispose(disposing); }
}
sealed class StickDisplay : Control
{
    PadState state; bool connected;
    public StickDisplay() { Dock = DockStyle.Top; Height = 65; Margin = Padding.Empty; DoubleBuffered = true; }
    public void Set(PadState s, bool active) { if (state == s && connected == active) return; state = s; connected = active; Invalidate(); }
    protected override void OnDpiChangedAfterParent(EventArgs e) { base.OnDpiChangedAfterParent(e); Height = (int)Math.Round(65 * DeviceDpi / 96f); }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        float scale = DeviceDpi / 96f, radius = 18 * scale, y = 25 * scale;
        using var outline = new Pen(Color.FromArgb(81, 104, 134), scale);
        using var dot = new SolidBrush(connected ? Theme.Accent : Theme.Muted);
        for (int i = 0; i < 2; i++)
        {
            float x = Width * (i == 0 ? .25f : .75f);
            g.DrawEllipse(outline, x - radius, y - radius, 2 * radius, 2 * radius);
            float sx = (i == 0 ? state.LX : state.RX) / 32768f, sy = (i == 0 ? state.LY : state.RY) / 32768f;
            float r = 4 * scale; g.FillEllipse(dot, x + sx * radius * .75f - r, y - sy * radius * .75f - r, 2 * r, 2 * r);
            TextRenderer.DrawText(g, i == 0 ? "Левый стик" : "Правый стик", Font, new Rectangle((int)(x - Width / 4), (int)(47 * scale), Width / 2, (int)(18 * scale)), Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
        }
    }
}
