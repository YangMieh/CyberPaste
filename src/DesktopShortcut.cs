using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CyberPaste
{
    // 首次執行時在桌面建立捷徑(指向目前 exe);若已有同名捷徑則重指到當前 exe(取代舊工具),
    // 並保留該捷徑原本的檔案屬性(尤其隱藏,見 TryCreate)。
    // 只做一次:用登錄檔旗標 HKCU\Software\CyberPaste\DesktopShortcutCreated 記錄,
    // 之後就算使用者把桌面捷徑刪掉,也不會再自己長回來(尊重使用者刪除的決定)。
    // 用 Windows 內建的 WScript.Shell(反射late-bound,免額外相依)建立 .lnk。
    internal static class DesktopShortcut
    {
        private const string RegKey = "Software\\CyberPaste";
        private const string FlagName = "DesktopShortcutCreated";

        public static void EnsureFirstRun()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RegKey))
                {
                    if (k == null) return;
                    if (k.GetValue(FlagName) != null) return; // 已做過→不再建立(即使捷徑被刪也不重建)
                    // 只有「確實建立/已存在」才記旗標;真的失敗就下次啟動再試,不會永久沒捷徑。
                    if (TryCreate())
                        k.SetValue(FlagName, 1, RegistryValueKind.DWord);
                }
            }
            catch { }
        }

        private static bool TryCreate()
        {
            try
            {
                string exe = Application.ExecutablePath;
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                if (string.IsNullOrEmpty(desktop) || !Directory.Exists(desktop)) return false;

                // 捷徑名稱跟著 exe 檔名走(改名成「小咩複製工具.exe」→ 捷徑就叫「小咩複製工具」)。
                string name = Path.GetFileNameWithoutExtension(exe);
                string lnk = Path.Combine(desktop, name + ".lnk");

                // B ＋ 保留隱藏:一律把同名捷徑重指到當前 exe(取代舊工具),但保留原本的檔案屬性。
                // 對「隱藏的 .lnk」直接覆寫會失敗或把隱藏洗掉→先記住原屬性、暫時拿掉隱藏/系統/唯讀,
                // 寫完再把原屬性(含隱藏)設回去,讓它維持原本的隱形/顯示狀態。
                bool existed = File.Exists(lnk);
                FileAttributes oldAttrs = FileAttributes.Normal;
                if (existed)
                {
                    try
                    {
                        oldAttrs = File.GetAttributes(lnk);
                        File.SetAttributes(lnk, oldAttrs & ~(FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReadOnly));
                    }
                    catch { existed = false; }
                }

                bool saved;
                try
                {
                    Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                    if (shellType == null) return false;
                    object shell = Activator.CreateInstance(shellType);
                    object sc = shellType.InvokeMember("CreateShortcut",
                        BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
                    Type sct = sc.GetType();
                    sct.InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc, new object[] { exe });
                    sct.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, sc, new object[] { Path.GetDirectoryName(exe) });
                    sct.InvokeMember("IconLocation", BindingFlags.SetProperty, null, sc, new object[] { exe + ",0" });
                    sct.InvokeMember("Description", BindingFlags.SetProperty, null, sc, new object[] { "CyberPaste 共享剪貼簿" });
                    sct.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
                    saved = File.Exists(lnk);
                }
                finally
                {
                    // 還原原屬性(保留隱藏);新建的捷徑(原本不存在)就維持一般可見,不動屬性。
                    if (existed)
                    {
                        try { File.SetAttributes(lnk, oldAttrs); } catch { }
                    }
                }
                return saved;
            }
            catch { return false; }
        }
    }
}
