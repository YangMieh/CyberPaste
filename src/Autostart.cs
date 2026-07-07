using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Windows.Forms;

namespace CyberPaste
{
    // 開機自動啟動。
    // 因本程式需系統管理員(manifest requireAdministrator),不能用登錄檔 Run 鍵——那會在每次開機
    // 跳 UAC 或直接失敗。正解=「工作排程器:登入觸發 + 最高權限(HighestAvailable) + InteractiveToken」,
    // 開機時靜默以管理員啟動、不跳 UAC(與 CyberPaste-Backup 排程同套路)。
    // 建立/刪除用內建 schtasks.exe;以 XML 定義避免 /TR 命令列引號地獄。
    // 查詢是否啟用=直接看 Tasks 資料夾有沒有該檔(免開 process,選單開啟才順)。
    internal static class Autostart
    {
        public const string TaskName = "CyberPaste-Autostart";

        // 工作排程器把每個工作存成 %SystemRoot%\System32\Tasks\<工作名> 一個檔;存在即代表已啟用。
        private static string TaskFilePath
        {
            get
            {
                string sys = Environment.GetFolderPath(Environment.SpecialFolder.System);
                return Path.Combine(Path.Combine(sys, "Tasks"), TaskName);
            }
        }

        public static bool IsEnabled()
        {
            try { return File.Exists(TaskFilePath); }
            catch { return false; }
        }

        public static bool Enable()
        {
            try
            {
                string exe = Application.ExecutablePath;
                string user = WindowsIdentity.GetCurrent().Name; // DOMAIN\User
                string xml = BuildXml(exe, user);
                // schtasks /XML 要 UTF-16;寫到暫存檔再匯入,匯入後刪掉。
                string tmp = Path.Combine(Path.GetTempPath(), "CyberPaste-autostart.xml");
                File.WriteAllText(tmp, xml, Encoding.Unicode);
                int rc = RunSchtasks("/Create /TN \"" + TaskName + "\" /XML \"" + tmp + "\" /F");
                try { File.Delete(tmp); } catch { }
                return rc == 0;
            }
            catch { return false; }
        }

        public static bool Disable()
        {
            try { return RunSchtasks("/Delete /TN \"" + TaskName + "\" /F") == 0; }
            catch { return false; }
        }

        private static int RunSchtasks(string args)
        {
            var psi = new ProcessStartInfo("schtasks.exe", args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using (var p = Process.Start(psi))
            {
                p.StandardOutput.ReadToEnd();
                p.StandardError.ReadToEnd();
                p.WaitForExit();
                return p.ExitCode;
            }
        }

        private static string BuildXml(string exePath, string user)
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n");
            sb.Append("<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\r\n");
            sb.Append("  <RegistrationInfo><Description>CyberPaste 開機自動啟動 / auto-start at logon</Description></RegistrationInfo>\r\n");
            sb.Append("  <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>" + Esc(user) + "</UserId></LogonTrigger></Triggers>\r\n");
            sb.Append("  <Principals><Principal id=\"Author\"><UserId>" + Esc(user) + "</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>\r\n");
            sb.Append("  <Settings>");
            sb.Append("<MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>");
            sb.Append("<DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>");
            sb.Append("<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>");
            sb.Append("<AllowHardTerminate>false</AllowHardTerminate>");
            sb.Append("<StartWhenAvailable>true</StartWhenAvailable>");
            sb.Append("<RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>");
            sb.Append("<AllowStartOnDemand>true</AllowStartOnDemand>");
            sb.Append("<Enabled>true</Enabled>");
            sb.Append("<Hidden>false</Hidden>");
            sb.Append("<ExecutionTimeLimit>PT0S</ExecutionTimeLimit>");
            sb.Append("</Settings>\r\n");
            sb.Append("  <Actions Context=\"Author\"><Exec><Command>" + Esc(exePath) + "</Command></Exec></Actions>\r\n");
            sb.Append("</Task>\r\n");
            return sb.ToString();
        }

        private static string Esc(string s)
        {
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }
    }
}
