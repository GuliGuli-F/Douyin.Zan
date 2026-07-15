using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DouyinLikeTool
{
    public sealed class MainForm : Form
    {
        // ---- Win32 热键 API ----
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const int MOD_ALT = 0x1;
        private const int MOD_CONTROL = 0x2;
        private const int MOD_SHIFT = 0x4;
        private const int HOTKEY_START = 1;
        private const int HOTKEY_END = 2;
        private const int WM_HOTKEY = 0x0312;

        // ---- 热键设置 ----
        private sealed class HotkeySetting
        {
            public Keys Key { get; set; }
            public bool Control { get; set; }
            public bool Alt { get; set; }
            public bool Shift { get; set; }
        }

        private HotkeySetting? _startHotkey;
        private HotkeySetting? _endHotkey;
        private bool _captureActive;
        private TextBox? _captureTarget;

        // ---- 运行状态 ----
        private CancellationTokenSource? _cts;
        private bool _running;

        // ---- 控件 ----
        private NumericUpDown numMin = null!;
        private NumericUpDown numMax = null!;
        private TextBox txtStartHotkey = null!;
        private TextBox txtEndHotkey = null!;
        private Button btnSetStart = null!;
        private Button btnSetEnd = null!;
        private Button btnStart = null!;
        private Button btnStop = null!;
        private Label lblStatus = null!;

        public MainForm()
        {
            InitializeComponent();

            // 默认热键：F6 开始 / F7 结束
            _startHotkey = new HotkeySetting { Key = Keys.F6 };
            _endHotkey = new HotkeySetting { Key = Keys.F7 };
            UpdateHotkeyDisplay();
        }

        private void InitializeComponent()
        {
            Text = "抖音直播点赞工具 - GuLi";
            Width = 430;
            Height = 340;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;

            int left = 20;
            int labelW = 130;
            int fieldX = 160;

            // 最小毫秒
            Controls.Add(new Label { Left = left, Top = 24, Width = labelW, Text = "最小间隔(毫秒):" });
            numMin = new NumericUpDown { Left = fieldX, Top = 20, Width = 100, Minimum = 1, Maximum = 100000, Value = 100, Increment = 10 };
            Controls.Add(numMin);

            // 最大毫秒
            Controls.Add(new Label { Left = left, Top = 59, Width = labelW, Text = "最大间隔(毫秒):" });
            numMax = new NumericUpDown { Left = fieldX, Top = 55, Width = 100, Minimum = 1, Maximum = 100000, Value = 300, Increment = 10 };
            Controls.Add(numMax);

            // 开始热键
            Controls.Add(new Label { Left = left, Top = 99, Width = labelW, Text = "开始热键:" });
            txtStartHotkey = new TextBox { Left = fieldX, Top = 95, Width = 100, ReadOnly = true };
            txtStartHotkey.KeyDown += HotkeyCapture_KeyDown;
            Controls.Add(txtStartHotkey);
            btnSetStart = new Button { Left = fieldX + 110, Top = 93, Width = 70, Height = 24, Text = "设置" };
            btnSetStart.Click += (s, e) => BeginCapture(txtStartHotkey);
            Controls.Add(btnSetStart);

            // 结束热键
            Controls.Add(new Label { Left = left, Top = 134, Width = labelW, Text = "结束热键:" });
            txtEndHotkey = new TextBox { Left = fieldX, Top = 130, Width = 100, ReadOnly = true };
            txtEndHotkey.KeyDown += HotkeyCapture_KeyDown;
            Controls.Add(txtEndHotkey);
            btnSetEnd = new Button { Left = fieldX + 110, Top = 128, Width = 70, Height = 24, Text = "设置" };
            btnSetEnd.Click += (s, e) => BeginCapture(txtEndHotkey);
            Controls.Add(btnSetEnd);

            // 开始 / 停止 按钮
            btnStart = new Button { Left = 70, Top = 178, Width = 120, Height = 32, Text = "开始" };
            btnStart.Click += (s, e) => StartClicking();
            Controls.Add(btnStart);

            btnStop = new Button { Left = 220, Top = 178, Width = 120, Height = 32, Text = "停止" };
            btnStop.Click += (s, e) => StopClicking();
            Controls.Add(btnStop);

            // 状态
            lblStatus = new Label { Left = left, Top = 228, Width = 380, Text = "状态：已停止" };
            Controls.Add(lblStatus);

            // 使用说明
            Controls.Add(new Label
            {
                Left = left,
                Top = 258,
                Width = 390,
                Height = 40,
                Text = "开始后，软件会每隔随机毫秒(最小~最大)向当前活动窗口发送 Z 键。\n请将鼠标焦点置于抖音直播窗口再开始。",
                ForeColor = Color.Gray
            });
        }

        // ---- 热键捕获 ----
        private void BeginCapture(TextBox tb)
        {
            UnregisterHotkeys();
            _captureActive = true;
            _captureTarget = tb;
            tb.Text = "请按下热键...";
            tb.Focus();
        }

        private void HotkeyCapture_KeyDown(object? sender, KeyEventArgs e)
        {
            if (!_captureActive || sender != _captureTarget) return;
            e.SuppressKeyPress = true;
            e.Handled = true;

            // 忽略纯修饰键
            if (IsModifierKey(e.KeyCode)) return;

            var h = new HotkeySetting { Key = e.KeyCode, Control = e.Control, Alt = e.Alt, Shift = e.Shift };
            if (_captureTarget == txtStartHotkey) _startHotkey = h;
            else _endHotkey = h;

            _captureActive = false;
            _captureTarget = null;
            UpdateHotkeyDisplay();
            ReRegisterHotkeys();
            ActiveControl = null;
        }

        private static bool IsModifierKey(Keys k) =>
            k == Keys.ControlKey || k == Keys.LControlKey || k == Keys.RControlKey ||
            k == Keys.Menu || k == Keys.LMenu || k == Keys.RMenu ||
            k == Keys.ShiftKey || k == Keys.LShiftKey || k == Keys.RShiftKey;

        // ---- 热键注册 ----
        private static int ModifiersToInt(HotkeySetting h) =>
            (h.Control ? MOD_CONTROL : 0) | (h.Alt ? MOD_ALT : 0) | (h.Shift ? MOD_SHIFT : 0);

        private void UnregisterHotkeys()
        {
            UnregisterHotKey(Handle, HOTKEY_START);
            UnregisterHotKey(Handle, HOTKEY_END);
        }

        private void ReRegisterHotkeys()
        {
            UnregisterHotkeys();

            if (_startHotkey != null && _endHotkey != null && SameHotkey(_startHotkey, _endHotkey))
            {
                MessageBox.Show("开始热键与结束热键不能相同！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (_startHotkey != null)
                RegisterHotKey(Handle, HOTKEY_START, ModifiersToInt(_startHotkey), (int)_startHotkey.Key);
            if (_endHotkey != null)
                RegisterHotKey(Handle, HOTKEY_END, ModifiersToInt(_endHotkey), (int)_endHotkey.Key);
        }

        private static bool SameHotkey(HotkeySetting a, HotkeySetting b) =>
            a.Key == b.Key && a.Control == b.Control && a.Alt == b.Alt && a.Shift == b.Shift;

        private void UpdateHotkeyDisplay()
        {
            txtStartHotkey.Text = HotkeyToString(_startHotkey);
            txtEndHotkey.Text = HotkeyToString(_endHotkey);
        }

        private static string HotkeyToString(HotkeySetting? h)
        {
            if (h == null) return "(未设置)";
            var parts = new List<string>();
            if (h.Control) parts.Add("Ctrl");
            if (h.Alt) parts.Add("Alt");
            if (h.Shift) parts.Add("Shift");
            parts.Add(h.Key.ToString());
            return string.Join("+", parts);
        }

        // ---- 开始 / 停止 ----
        private void StartClicking()
        {
            if (_running) return;

            int min = (int)numMin.Value;
            int max = (int)numMax.Value;
            if (min > max)
            {
                MessageBox.Show("最小毫秒不能大于最大毫秒，已自动交换。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                (min, max) = (max, min);
                numMin.Value = min;
                numMax.Value = max;
            }

            _cts = new CancellationTokenSource();
            _running = true;
            UpdateStatus();
            _ = Task.Run(async () => await ClickLoop(min, max, _cts.Token));
        }

        private void StopClicking()
        {
            if (!_running) return;
            _cts?.Cancel();
            UpdateStatus();
        }

        private async Task ClickLoop(int min, int max, CancellationToken token)
        {
            var rnd = new Random();
            try
            {
                while (!token.IsCancellationRequested)
                {
                    int delay = rnd.Next(min, max + 1);
                    await Task.Delay(delay, token);
                    if (token.IsCancellationRequested) break;

                    try
                    {
                        Invoke((MethodInvoker)(() => SendKeys.SendWait("z")));
                    }
                    catch (InvalidOperationException)
                    {
                        // 窗口已销毁，忽略
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 正常取消
            }
            finally
            {
                _running = false;
                try { Invoke((MethodInvoker)UpdateStatus); } catch (InvalidOperationException) { }
            }
        }

        private void UpdateStatus()
        {
            if (InvokeRequired) { Invoke((MethodInvoker)UpdateStatus); return; }
            lblStatus.Text = _running ? "状态：运行中（正在发送 Z 键）" : "状态：已停止";
            lblStatus.ForeColor = _running ? Color.Green : Color.Gray;
            btnStart.Enabled = !_running;
            btnStop.Enabled = _running;
        }

        // ---- 窗口消息 ----
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY)
            {
                int id = m.WParam.ToInt32();
                if (id == HOTKEY_START) StartClicking();
                else if (id == HOTKEY_END) StopClicking();
            }
            base.WndProc(ref m);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            ReRegisterHotkeys();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _cts?.Cancel();
            UnregisterHotkeys();
            base.OnFormClosing(e);
        }
    }
}
