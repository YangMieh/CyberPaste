using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace CyberPaste
{
    internal static class Program
    {
        [DllImport("ole32.dll")]
        private static extern int CoInitializeSecurity(
            IntPtr pSecDesc, int cAuthSvc, IntPtr asAuthSvc, IntPtr pReserved1,
            uint dwAuthnLevel, uint dwImpLevel, IntPtr pAuthList,
            uint dwCapabilities, IntPtr pReserved3);

        private const uint RPC_C_AUTHN_LEVEL_NONE = 1;
        private const uint RPC_C_IMP_LEVEL_IMPERSONATE = 3;
        private const uint EOAC_NONE = 0;

        [STAThread]
        static void Main()
        {
            Logger.Init();

            // 全域例外攔截：收檔端在「拉大包」時若崩潰或凍結，這裡會留下 [FATAL] 堆疊，
            // 讓我們不必猜。UI 執行緒例外改由 ThreadException 記錄後續存活，不再讓程式直接消失。
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                var ex = e.ExceptionObject as Exception;
                Logger.Log("[FATAL] 未處理例外(即將結束=" + e.IsTerminating + "): " +
                           (ex != null ? ex.ToString() : "" + e.ExceptionObject));
            };
            try { Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException); } catch { }
            Application.ThreadException += (s, e) =>
                Logger.Log("[FATAL] UI執行緒例外(已攔下續跑): " + e.Exception);

            // 提權(高完整性)後，讓一般權限的檔案總管仍能透過 OLE 剪貼簿向我們索取
            // 延遲渲染的檔案內容：把本行程 COM 安全層級降到不需驗證。失敗就忽略。
            try
            {
                Application.OleRequired(); // 確保此 STA 已 OLE 初始化
                CoInitializeSecurity(IntPtr.Zero, -1, IntPtr.Zero, IntPtr.Zero,
                    RPC_C_AUTHN_LEVEL_NONE, RPC_C_IMP_LEVEL_IMPERSONATE, IntPtr.Zero,
                    EOAC_NONE, IntPtr.Zero);
            }
            catch { }

            bool createdNew;
            using (var mutex = new Mutex(true, "CyberPaste_SingleInstance_45889", out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show("CyberPaste 已經在執行中（系統匣有圖示）。", "CyberPaste",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayContext());
            }
        }
    }

    internal sealed class TrayContext : ApplicationContext
    {
        private readonly NotifyIcon _tray;
        private readonly NetworkService _net;
        private readonly ClipboardService _clip;
        private readonly Icon _iconOn;
        private readonly Icon _iconOff;
        private string _lastLog = "";

        // ── 選單顯示開關（不刪除功能，只控制要不要出現在系統匣選單）──
        // 多數人停用同步時是直接關程式，故「停用/啟用同步」選單項與雙擊切換暫時隱藏。
        // 要恢復改成 true 即可，Enabled 邏輯與圖示切換全都保留著。
        private const bool ShowToggleItem = false;
        // 「狀態：…」那一列（最近一筆 log）暫時隱藏，改成 true 可再顯示。
        private const bool ShowStatusLine = false;

        public TrayContext()
        {
            _iconOn = MakeIcon(true);
            _iconOff = MakeIcon(false);

            _net = new NetworkService();
            _clip = new ClipboardService(_net);

            _net.OnLog = Log;
            _clip.OnLog = Log;

            _tray = new NotifyIcon
            {
                Icon = _iconOn,
                Visible = true,
                Text = "CyberPaste（執行中）"
            };
            _tray.ContextMenuStrip = BuildMenu();
            if (ShowToggleItem)
                _tray.DoubleClick += (s, e) => ToggleEnabled();

            _net.Start();
            ShowBalloon("CyberPaste 已啟動", "同網域自動互通文字 / 圖片 / 檔案。");
        }

        private ContextMenuStrip BuildMenu()
        {
            var menu = new ContextMenuStrip();
            menu.Opening += (s, e) => RebuildMenu(menu);
            RebuildMenu(menu);
            return menu;
        }

        private void RebuildMenu(ContextMenuStrip menu)
        {
            menu.Items.Clear();

            var header = new ToolStripMenuItem(_clip.Enabled ? "● 同步：開啟中" : "○ 同步：已停用")
            { Enabled = false };
            menu.Items.Add(header);

            // 「同步：開啟中」正下方，一句不能按的署名。前置空心圓與上一行對齊。
            menu.Items.Add(new ToolStripMenuItem("○ 製作者：小咩") { Enabled = false });

            menu.Items.Add(new ToolStripSeparator());

            if (ShowToggleItem)
            {
                var toggle = new ToolStripMenuItem(_clip.Enabled ? "停用同步" : "啟用同步");
                toggle.Click += (s, e) => ToggleEnabled();
                menu.Items.Add(toggle);
            }

            var peers = _net.Peers.OrderBy(p => p.Name).ToArray();
            var peersItem = new ToolStripMenuItem("目前夥伴：" + peers.Length + " 台");
            if (peers.Length == 0)
                peersItem.DropDownItems.Add(new ToolStripMenuItem("（尚未發現其他電腦）") { Enabled = false });
            else
                foreach (var p in peers)
                    peersItem.DropDownItems.Add(new ToolStripMenuItem(p.Name + "  (" + p.Ip + ")") { Enabled = false });
            menu.Items.Add(peersItem);

            if (ShowStatusLine && !string.IsNullOrEmpty(_lastLog))
            {
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add(new ToolStripMenuItem("狀態：" + _lastLog) { Enabled = false });
            }

            menu.Items.Add(new ToolStripSeparator());
            var exit = new ToolStripMenuItem("結束");
            exit.Click += (s, e) => ExitApp();
            menu.Items.Add(exit);
        }

        private void ToggleEnabled()
        {
            _clip.Enabled = !_clip.Enabled;
            _tray.Icon = _clip.Enabled ? _iconOn : _iconOff;
            _tray.Text = _clip.Enabled ? "CyberPaste（執行中）" : "CyberPaste（已停用）";
            ShowBalloon("CyberPaste", _clip.Enabled ? "同步已開啟，通道打開。" : "同步已停用，通道關閉。");
        }

        private void Log(string msg)
        {
            _lastLog = msg;
            if (_tray != null)
            {
                string t = "CyberPaste：" + msg;
                _tray.Text = t.Length > 63 ? t.Substring(0, 60) + "…" : t;
            }
        }

        private void ShowBalloon(string title, string text)
        {
            try { _tray.BalloonTipTitle = title; _tray.BalloonTipText = text; _tray.ShowBalloonTip(2500); }
            catch { }
        }

        private void ExitApp()
        {
            try { _tray.Visible = false; } catch { }
            try { _clip.Dispose(); } catch { }
            try { _net.Dispose(); } catch { }
            ExitThread();
        }

        // 系統匣/工具列圖示:讀內嵌的 app.ico(與 exe 圖示同一張新 copy.png 產生的多尺寸 ico),
        // 讓系統匣圖示與 exe 圖示一致。(v1.4.4;舊版是用程式碼畫的藍色兩張紙 → 移到 MakeIconDrawn 當後備)
        private static Icon MakeIcon(bool on)
        {
            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                using (var s = asm.GetManifestResourceStream("app.ico"))
                {
                    if (s != null)
                    {
                        var full = new Icon(s); // 含多尺寸,系統匣依 DPI 自動挑
                        if (on) return full;
                        // 停用態(灰):目前 ShowToggleItem=false 不會顯示,保留灰階以備日後重啟切換
                        using (var bmp = full.ToBitmap())
                        using (var gray = ToGrayscale(bmp))
                        {
                            IntPtr gh = gray.GetHicon();
                            var gi = (Icon)Icon.FromHandle(gh).Clone();
                            full.Dispose();
                            return gi;
                        }
                    }
                }
            }
            catch { }
            return MakeIconDrawn(on); // 後備:讀不到內嵌 ico 才用舊畫法
        }

        private static Bitmap ToGrayscale(Bitmap src)
        {
            var dst = new Bitmap(src.Width, src.Height);
            using (var g = Graphics.FromImage(dst))
            {
                var cm = new ColorMatrix(new float[][]
                {
                    new float[] { 0.3f, 0.3f, 0.3f, 0, 0 },
                    new float[] { 0.59f, 0.59f, 0.59f, 0, 0 },
                    new float[] { 0.11f, 0.11f, 0.11f, 0, 0 },
                    new float[] { 0, 0, 0, 1, 0 },
                    new float[] { 0, 0, 0, 0, 1 }
                });
                using (var ia = new ImageAttributes())
                {
                    ia.SetColorMatrix(cm);
                    g.DrawImage(src, new Rectangle(0, 0, src.Width, src.Height),
                        0, 0, src.Width, src.Height, GraphicsUnit.Pixel, ia);
                }
            }
            return dst;
        }

        // 後備:讀不到內嵌 app.ico 時,用程式碼畫一個「複製」風格圖示（兩張疊起來的紙）。on=藍 / off=灰。
        private static Icon MakeIconDrawn(bool on)
        {
            using (var bmp = new Bitmap(32, 32))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                Color main = on ? Color.FromArgb(40, 120, 215) : Color.FromArgb(130, 130, 130);
                Color back = on ? Color.FromArgb(150, 190, 240) : Color.FromArgb(180, 180, 180);

                using (var bb = new SolidBrush(back))
                    g.FillRectangle(bb, 6, 4, 16, 20);
                using (var pen = new Pen(main, 2))
                    g.DrawRectangle(pen, 6, 4, 16, 20);

                using (var fb = new SolidBrush(Color.White))
                    g.FillRectangle(fb, 11, 9, 16, 20);
                using (var pen = new Pen(main, 2))
                    g.DrawRectangle(pen, 11, 9, 16, 20);
                using (var lp = new Pen(main, 1.5f))
                {
                    g.DrawLine(lp, 14, 15, 24, 15);
                    g.DrawLine(lp, 14, 19, 24, 19);
                    g.DrawLine(lp, 14, 23, 21, 23);
                }
                IntPtr h = bmp.GetHicon();
                return (Icon)Icon.FromHandle(h).Clone();
            }
        }
    }
}
