using System;
using System.Drawing;
using System.Windows.Forms;

namespace CyberPaste
{
    // v1.4.0:自家的「覆蓋詢問」小框(不是用 Explorer 的,但相同體驗)。
    internal sealed class OverwriteDialog : Form
    {
        public enum Result
        {
            OverwriteAll,
            Skip,
            Cancel
        }

        private Result _result = Result.Cancel;

        private OverwriteDialog()
        {
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            TopMost = true;
            Text = "CyberPaste";
            ClientSize = new Size(400, 130);
            BackColor = Color.FromArgb(245, 242, 236);

            Label msg = new Label
            {
                Left = 16,
                Top = 16,
                Width = 368,
                Height = 44,
                Text = "目標資料夾已有同名檔案。\n要如何處理？",
                Font = new Font("Microsoft JhengHei UI", 9.5f)
            };
            Controls.Add(msg);

            Button bOver = MakeButton("全部覆蓋", 16);
            bOver.Click += delegate { _result = Result.OverwriteAll; Close(); };
            Button bSkip = MakeButton("略過已存在", 148);
            bSkip.Click += delegate { _result = Result.Skip; Close(); };
            Button bCancel = MakeButton("取消", 300);
            bCancel.Width = 84;
            bCancel.Click += delegate { _result = Result.Cancel; Close(); };
            Controls.Add(bOver);
            Controls.Add(bSkip);
            Controls.Add(bCancel);
            AcceptButton = bOver;
            CancelButton = bCancel;
        }

        private static Button MakeButton(string text, int left)
        {
            return new Button
            {
                Left = left,
                Top = 78,
                Width = 124,
                Height = 32,
                Text = text,
                Font = new Font("Microsoft JhengHei UI", 9f),
                FlatStyle = FlatStyle.System
            };
        }

        protected override bool ShowWithoutActivation
        {
            get { return false; }
        }

        // 在 UI 緒呼叫。回傳使用者選擇。
        public static Result Ask()
        {
            using (OverwriteDialog d = new OverwriteDialog())
            {
                d.ShowDialog();
                return d._result;
            }
        }
    }
}
