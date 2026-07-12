using System;
using System.Drawing;
using System.Windows.Forms;

namespace CyberPaste
{
    // v1.3.6 大宗傳輸右下角進度框(米色小視窗,參考 LINE@ 專案的尺寸感)。
    // 大宗模式由我方自己寫檔,沒有 Explorer 原生複製框,故自畫進度。
    internal sealed class BulkProgressForm : Form
    {
        private readonly ProgressBar _bar;
        private readonly Label _title;
        private readonly Label _stats;
        private Timer _closeTimer;

        public BulkProgressForm(string peerLabel, long total)
        {
            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            Text = "CyberPaste 傳輸中";
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            ClientSize = new Size(380, 118);
            BackColor = Color.FromArgb(245, 242, 236);

            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(wa.Right - Width - 16, wa.Bottom - Height - 16);

            _title = new Label
            {
                Left = 14, Top = 12, Width = 352, Height = 20,
                Text = "接收 " + peerLabel + " …",
                Font = new Font("Microsoft JhengHei UI", 9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(40, 40, 40),
                AutoEllipsis = true
            };
            _bar = new ProgressBar
            {
                Left = 14, Top = 40, Width = 352, Height = 18,
                Minimum = 0, Maximum = 1000, Style = ProgressBarStyle.Continuous
            };
            _stats = new Label
            {
                Left = 14, Top = 64, Width = 352, Height = 44,
                Text = FmtSize(0) + " / " + FmtSize(total),
                Font = new Font("Microsoft JhengHei UI", 8.5f),
                ForeColor = Color.FromArgb(70, 70, 70)
            };
            Controls.Add(_title);
            Controls.Add(_bar);
            Controls.Add(_stats);
        }

        public void UpdateProgress(NetworkService.BulkProgress p)
        {
            if (p.Failed) { MarkFailed(p.Error); return; }
            if (p.Reconnecting) { _title.Text = "連線中斷，重連中…"; return; }

            if (p.BytesTotal > 0)
            {
                long v = p.BytesDone * 1000L / p.BytesTotal;
                if (v < 0) v = 0;
                if (v > 1000) v = 1000;
                _bar.Value = (int)v;
            }
            double pct = (p.BytesTotal > 0) ? (p.BytesDone * 100.0 / p.BytesTotal) : 0;
            double remBytes = p.BytesTotal - p.BytesDone;
            double etaSec = (p.Mbps > 0) ? (remBytes * 8.0 / 1000000.0 / p.Mbps) : 0;

            _title.Text = p.Done ? "傳輸完成" : ("接收中：" + Shorten(p.CurrentName));
            _stats.Text = string.Format(
                "{0:0.0}%   ·   {1:0.00} Gbps   ·   剩 {2}\n檔 {3}/{4}   ·   {5} / {6}",
                pct, p.Mbps / 1000.0, p.Done ? "--" : FmtEta(etaSec),
                p.FilesDone, p.FilesTotal, FmtSize(p.BytesDone), FmtSize(p.BytesTotal));

            if (p.Done) MarkDone();
        }

        public void MarkDone()
        {
            _title.Text = "✔ 傳輸完成";
            _bar.Value = _bar.Maximum;
            AutoClose(700); // 完成後快速閃一下就關(使用者要求越快越好)
        }

        public void MarkFailed(string err)
        {
            _title.Text = "✖ 傳輸中斷";
            _stats.Text = "傳輸未完成：" + (err ?? "");
            AutoClose(4000);
        }

        private void AutoClose(int ms)
        {
            if (_closeTimer != null) return;
            _closeTimer = new Timer { Interval = ms };
            _closeTimer.Tick += delegate
            {
                try { _closeTimer.Stop(); } catch { }
                try { Close(); } catch { }
            };
            _closeTimer.Start();
        }

        // 不搶焦點(使用者可能還在打字/操作),但保持置頂
        protected override bool ShowWithoutActivation { get { return true; } }

        private static string Shorten(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            int i = name.LastIndexOf('\\');
            string leaf = (i >= 0) ? name.Substring(i + 1) : name;
            return (leaf.Length > 40) ? ("…" + leaf.Substring(leaf.Length - 39)) : leaf;
        }

        private static string FmtSize(long b)
        {
            if (b >= 1073741824L) return ((double)b / 1073741824.0).ToString("0.00") + " GB";
            if (b >= 1048576L) return ((double)b / 1048576.0).ToString("0.0") + " MB";
            if (b >= 1024L) return ((double)b / 1024.0).ToString("0") + " KB";
            return b + " B";
        }

        private static string FmtEta(double sec)
        {
            if (sec <= 0 || double.IsInfinity(sec) || double.IsNaN(sec)) return "--";
            if (sec >= 3600) return ((int)(sec / 3600)) + " 時 " + ((int)(sec % 3600 / 60)) + " 分";
            if (sec >= 60) return ((int)(sec / 60)) + " 分 " + ((int)(sec % 60)) + " 秒";
            return ((int)sec) + " 秒";
        }
    }
}
