using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ScreenDim
{
    public class OverlayForm : Form
    {
        const int GWL_EXSTYLE = -20;
        const int WS_EX_TRANSPARENT = 0x20;
        const int WS_EX_LAYERED = 0x80000;
        const int WS_EX_NOACTIVATE = 0x08000000;
        const int WS_EX_TOOLWINDOW = 0x80;
        const int WM_NCHITTEST = 0x0084;
        const int HTTRANSPARENT = -1;
        const int HWND_TOPMOST = -1;
        const int HWND_NOTOPMOST = -2;
        const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40;

        private byte _alphaByte;   // 0..255 per-pixel alpha we push via SetLayeredWindowAttributes
        private bool _topmost = true;

        public OverlayForm(int screenIndex)
        {
            var bounds = Screen.AllScreens[screenIndex].Bounds;
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.Manual;
            this.Bounds = bounds;
            this.ShowInTaskbar = false;
            this.BackColor = Color.Black;

            // layered + transparent + no-activate + no-taskbar => pure click-through overlay
            int ex = GetWindowLong(this.Handle, GWL_EXSTYLE);
            ex |= WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
            SetWindowLong(this.Handle, GWL_EXSTYLE, ex);

            SetAlpha(0);
        }

        // Alpha via SetLayeredWindowAttributes (whole-window alpha). Works with
        // WS_EX_TRANSPARENT so clicks fall through to whatever window is below.
        public void SetAlpha(double opacity /*0..1*/)
        {
            _alphaByte = (byte)Math.Round(Math.Max(0, Math.Min(1, opacity)) * 255);
            ApplyLayered();
        }

        private void ApplyLayered()
        {
            if (_alphaByte == 0)
            {
                this.Visible = false;   // fully hidden -> definitely not interactive
                return;
            }
            this.Visible = true;
            // per-window alpha; LWA_ALPHA applies the whole window's opacity
            SetLayeredWindowAttributes(this.Handle, 0, _alphaByte, LWA_ALPHA);
            SetTopmost(_topmost);
        }

        // Toggle topmost. When a system menu (e.g. the tray right-click menu) is
        // shown, we drop topmost so the menu can appear above us; we restore it
        // once the menu closes so the overlay keeps covering fullscreen video.
        public void SetTopmost(bool topmost)
        {
            _topmost = topmost;
            IntPtr after = topmost ? (IntPtr)HWND_TOPMOST : (IntPtr)HWND_NOTOPMOST;
            SetWindowPos(this.Handle, after, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }

        // Click-through: report every hit as transparent so mouse falls through to
        // whatever window is below this overlay.
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_NCHITTEST)
            {
                m.Result = new IntPtr(HTTRANSPARENT);
                return;
            }
            base.WndProc(ref m);
        }

        // ---- layered window P/Invoke ----
        const uint LWA_ALPHA = 0x2;
        [DllImport("user32.dll")] static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint f);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);
    }

    public class Program : ApplicationContext
    {
        private OverlayForm[] _overlays;
        private bool _on;
        private double _alpha;                 // 0..1 overlay opacity
        private NotifyIcon _tray;
        private ToolStripMenuItem _toggleMenu;
        private HotkeyForm _hotkey;            // hidden form that receives WM_HOTKEY
        private ControlPanel _panel;           // the control window
        private bool _allowTopmost = true;     // false while a system menu (tray) is open
        private const double MIN_ALPHA = 0.05;
        private const double MAX_ALPHA = 0.90;
        internal const double STEP = 0.10;

        // tray icon: draw a simple black square with a down-arrow, no external file needed
        private static Bitmap MakeIcon()
        {
            var bmp = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.FillRectangle(Brushes.Black, 2, 4, 12, 10);
                g.FillRectangle(Brushes.White, 4, 7, 8, 3);
            }
            return bmp;
        }

        public Program()
        {
            _alpha = 0.35;
            _on = true;

            int n = Screen.AllScreens.Length;
            _overlays = new OverlayForm[n];
            for (int i = 0; i < n; i++)
            {
                _overlays[i] = new OverlayForm(i);
                _overlays[i].Show();   // must Show() for the overlay to be visible
            }

            // tray
            _tray = new NotifyIcon();
            _tray.Icon = Icon.FromHandle(MakeIcon().GetHicon());
            _tray.Text = "屏幕变暗  (Ctrl+Alt+D)";
            _tray.Visible = true;

            _toggleMenu = new ToolStripMenuItem("开启变暗", null, (s, e) => Toggle());
            var openPanel = new ToolStripMenuItem("打开控制面板", null, (s, e) => ShowPanel());
            var autostart = new ToolStripMenuItem("开机自启", null, (s, e) => ToggleAutostart());
            var quit = new ToolStripMenuItem("退出", null, (s, e) => Exit());

            var menu = new ContextMenuStrip();
            menu.Items.Add(_toggleMenu);
            menu.Items.Add(openPanel);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(autostart);
            menu.Items.Add(quit);
            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick += (s, e) => Toggle();

            // When the tray right-click menu opens, drop the overlay's topmost so
            // the menu can appear above it; restore once the menu closes.
            menu.Opening += (s, e) => SetOverlaysTopmost(false);
            menu.Closed += (s, e) => SetOverlaysTopmost(true);

            // register hotkeys on a hidden form
            _hotkey = new HotkeyForm(this);
            _hotkey.Show();      // minimized, off taskbar, keeps a live message pump for hotkeys
            _hotkey.RegisterAll();

            // keep re-asserting topmost so it beats browser/fullscreen layers,
            // but back off while a system menu (tray right-click) is open
            var t = new System.Windows.Forms.Timer();
            t.Interval = 1500;
            t.Tick += (s, e) => { if (_on && _allowTopmost) SetOverlaysTopmost(true); };
            t.Start();

            Apply();

            // show the control panel on startup
            ShowPanel();
        }

        internal void ShowPanel()
        {
            if (_panel == null)
            {
                _panel = new ControlPanel(this);
                _panel.FormClosed += (s, e) => _panel = null;
            }
            _panel.SyncFrom(_alpha, _on);
            _panel.Show();
            _panel.Activate();
        }

        internal void Toggle() { _on = !_on; Apply(); }
        internal void ChangeAlpha(double d)
        {
            SetAlpha(_alpha + d);
        }
        internal void SetAlpha(double v)
        {
            _alpha = Math.Min(MAX_ALPHA, Math.Max(MIN_ALPHA, v));
            if (_panel != null) _panel.SyncFrom(_alpha, _on);
            Apply();
        }
        internal void SetOn(bool on) { _on = on; if (_panel != null) _panel.SyncFrom(_alpha, _on); Apply(); }

        // Applies the topmost state to every overlay. Used by the tray menu
        // (to let the menu show above the overlay) and by the keep-on-top timer.
        private void SetOverlaysTopmost(bool topmost)
        {
            _allowTopmost = topmost;
            foreach (var o in _overlays)
                o.SetTopmost(topmost);
        }

        private void Apply()
        {
            foreach (var o in _overlays)
                o.SetAlpha(_on ? _alpha : 0.0);
            _toggleMenu.Text = _on ? "关闭变暗" : "开启变暗";
        }

        // ---- hotkey handling via a hidden form ----
        public class HotkeyForm : Form
        {
            const int HOTKEY_TOGGLE = 1, HOTKEY_UP = 2, HOTKEY_DOWN = 3;
            const int WM_HOTKEY = 0x0312;
            const uint MOD_CONTROL = 0x0002, MOD_ALT = 0x0001;
            private Program _p;
            [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h, int id, uint mod, Keys key);
            [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h, int id);
            public HotkeyForm(Program p)
            {
                _p = p;
                this.ShowInTaskbar = false;
                this.WindowState = FormWindowState.Minimized;
            }
            public void RegisterAll()
            {
                RegisterHotKey(this.Handle, HOTKEY_TOGGLE, MOD_CONTROL | MOD_ALT, Keys.D);
                RegisterHotKey(this.Handle, HOTKEY_UP, MOD_CONTROL | MOD_ALT, Keys.Up);
                RegisterHotKey(this.Handle, HOTKEY_DOWN, MOD_CONTROL | MOD_ALT, Keys.Down);
            }
            public void UnregisterAll()
            {
                UnregisterHotKey(this.Handle, HOTKEY_TOGGLE);
                UnregisterHotKey(this.Handle, HOTKEY_UP);
                UnregisterHotKey(this.Handle, HOTKEY_DOWN);
            }
            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WM_HOTKEY)
                {
                    switch (m.WParam.ToInt32())
                    {
                        case HOTKEY_TOGGLE: _p.Toggle(); break;
                        case HOTKEY_UP: _p.ChangeAlpha(+Program.STEP); break;
                        case HOTKEY_DOWN: _p.ChangeAlpha(-Program.STEP); break;
                    }
                }
                base.WndProc(ref m);
            }
        }

        // ---- autostart via registry Run key ----
        internal void ToggleAutostart()
        {
            string path = Application.ExecutablePath;
            using (var rk = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
            {
                bool exists = rk.GetValue("ScreenDim") != null;
                if (exists)
                {
                    rk.DeleteValue("ScreenDim", false);
                    MessageBox.Show("已关闭开机自启。", "屏幕变暗", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    rk.SetValue("ScreenDim", "\"" + path + "\"");
                    MessageBox.Show("已开启开机自启（下次登录自动启动）。\n\n说明：工具启动后蒙版默认开启。\n要临时完全关闭，按 Ctrl+Alt+D 或托盘关闭。", "屏幕变暗", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }

        internal void Exit()
        {
            _hotkey.UnregisterAll();
            _tray.Visible = false;
            _tray.Dispose();
            Application.Exit();
        }

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Program());
        }
    }

    public class ControlPanel : Form
    {
        private Program _app;
        private TrackBar _slider;
        private Label _valueLabel;
        private CheckBox _onCheck;
        private Label _hintLabel;

        public ControlPanel(Program app)
        {
            _app = app;
            this.Text = "屏幕变暗";
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.ClientSize = new Size(400, 280);
            this.TopMost = true;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Font = new Font("Microsoft YaHei UI", 9f);
            this.BackColor = Color.FromArgb(245, 245, 245);

            int y = 14;

            // ---- title ----
            var lblTitle = new Label();
            lblTitle.Text = "屏幕变暗设置";
            lblTitle.Font = new Font("Microsoft YaHei UI", 12f, FontStyle.Bold);
            lblTitle.ForeColor = Color.FromArgb(40, 40, 40);
            lblTitle.Location = new Point(16, y);
            lblTitle.AutoSize = true;

            y += 40;

            // ---- slider row: label left, value right, trackbar below ----
            var lblSlider = new Label();
            lblSlider.Text = "变暗程度";
            lblSlider.Location = new Point(16, y);
            lblSlider.AutoSize = true;

            _valueLabel = new Label();
            _valueLabel.Text = "35%";
            _valueLabel.Font = new Font("Microsoft YaHei UI", 13f, FontStyle.Bold);
            _valueLabel.ForeColor = Color.FromArgb(30, 100, 220);
            _valueLabel.Location = new Point(300, y - 3);
            _valueLabel.TextAlign = ContentAlignment.MiddleRight;
            _valueLabel.Size = new Size(80, 22);
            _valueLabel.AutoSize = false;

            y += 26;
            _slider = new TrackBar();
            _slider.Minimum = 5;
            _slider.Maximum = 90;
            _slider.TickFrequency = 10;
            _slider.SmallChange = 5;
            _slider.LargeChange = 10;
            _slider.Width = 366;
            _slider.Location = new Point(16, y);
            _slider.ValueChanged += (s, e) => {
                _valueLabel.Text = _slider.Value + "%";
                if (_onCheck.Checked) _app.SetAlpha(_slider.Value / 100.0);
            };

            y += _slider.Height + 14;   // use real trackbar height

            // ---- presets ----
            var presets = new[] { new { t = "浅暗", v = 15 }, new { t = "中等", v = 35 }, new { t = "深暗", v = 60 } };
            int px = 16;
            foreach (var p in presets)
            {
                var btn = new Button();
                btn.Text = p.t;
                btn.Width = 80;
                btn.Height = 28;
                btn.Location = new Point(px, y);
                btn.FlatStyle = FlatStyle.Flat;
                btn.FlatAppearance.BorderColor = Color.FromArgb(200, 200, 200);
                btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(220, 230, 250);
                btn.Tag = p.v;
                btn.Click += (s, e2) => { int v = (int)((Button)s).Tag; _slider.Value = v; if (_onCheck.Checked) _app.SetAlpha(v / 100.0); };
                this.Controls.Add(btn);
                px += 94;
            }

            y += 46;

            // ---- on/off + hint ----
            _onCheck = new CheckBox();
            _onCheck.Text = "启用变暗";
            _onCheck.Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold);
            _onCheck.Location = new Point(16, y);
            _onCheck.AutoSize = true;
            _onCheck.CheckedChanged += (s, e) => _app.SetOn(_onCheck.Checked);

            _hintLabel = new Label();
            _hintLabel.Text = "快捷键：Ctrl+Alt+D 开关　Ctrl+Alt+↑↓ 调暗度";
            _hintLabel.ForeColor = Color.Gray;
            _hintLabel.Font = new Font("Microsoft YaHei UI", 8f);
            _hintLabel.Location = new Point(16, y + 24);
            _hintLabel.AutoSize = true;

            y += 56;

            // ---- bottom buttons ----
            var btnAutostart = new Button();
            btnAutostart.Text = "开机自启";
            btnAutostart.Width = 100;
            btnAutostart.Height = 30;
            btnAutostart.Location = new Point(16, y);
            btnAutostart.FlatStyle = FlatStyle.Flat;
            btnAutostart.FlatAppearance.BorderColor = Color.FromArgb(200, 200, 200);
            btnAutostart.Click += (s, e) => _app.ToggleAutostart();

            var btnExit = new Button();
            btnExit.Text = "退出程序";
            btnExit.Width = 100;
            btnExit.Height = 30;
            btnExit.Location = new Point(284, y);
            btnExit.FlatStyle = FlatStyle.Flat;
            btnExit.BackColor = Color.FromArgb(255, 200, 200);
            btnExit.FlatAppearance.BorderColor = Color.FromArgb(220, 150, 150);
            btnExit.Click += (s, e) => _app.Exit();

            // ---- assemble ----
            this.Controls.Add(lblTitle);
            this.Controls.Add(lblSlider);
            this.Controls.Add(_valueLabel);
            this.Controls.Add(_slider);
            this.Controls.Add(_onCheck);
            this.Controls.Add(_hintLabel);
            this.Controls.Add(btnAutostart);
            this.Controls.Add(btnExit);
        }

        public void SyncFrom(double alpha, bool on)
        {
            _slider.Value = (int)Math.Round(alpha * 100);
            _valueLabel.Text = _slider.Value + "%";
            _onCheck.Checked = on;
        }
    }
}