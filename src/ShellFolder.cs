using System;
using System.Collections;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace CyberPaste
{
    // v1.3.6 大宗模式用:在使用者「按下 Ctrl+V 的那一刻」找出他正貼上的那個
    // 檔案總管資料夾本機路徑,好讓我方自己高速寫檔(跳過 Explorer 複製引擎)。
    // 用 Shell.Application 晚繫結(反射)列舉開啟中的檔案總管視窗,配對前景視窗 HWND。
    // 抓不到(例如貼進非檔案總管的地方)就回 null,呼叫端會退回原本逐檔延遲渲染,永遠可用。
    internal static class ShellFolder
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        public static string GetForegroundPasteFolder()
        {
            try
            {
                IntPtr fg = GetForegroundWindow();
                if (fg == IntPtr.Zero) return null;

                StringBuilder sb = new StringBuilder(256);
                GetClassName(fg, sb, sb.Capacity);
                string cls = sb.ToString();

                // 貼到桌面
                if (cls == "Progman" || cls == "WorkerW")
                    return Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

                // 只認檔案總管資料夾視窗;其餘(聊天框、遊戲啟動器自訂欄位…)一律回 null 走退回
                if (cls != "CabinetWClass" && cls != "ExploreWClass")
                    return null;

                Type shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType == null) return null;
                object shell = Activator.CreateInstance(shellType);
                try
                {
                    object windows = shellType.InvokeMember("Windows", BindingFlags.InvokeMethod, null, shell, null);
                    IEnumerable list = windows as IEnumerable;
                    if (list == null) return null;
                    foreach (object w in list)
                    {
                        if (w == null) continue;
                        try
                        {
                            object hwndObj = w.GetType().InvokeMember("HWND", BindingFlags.GetProperty, null, w, null);
                            long hwnd = Convert.ToInt64(hwndObj);
                            if (hwnd != fg.ToInt64()) continue;

                            object urlObj = w.GetType().InvokeMember("LocationURL", BindingFlags.GetProperty, null, w, null);
                            string url = urlObj as string;
                            if (string.IsNullOrEmpty(url)) return null;
                            Uri uri;
                            if (Uri.TryCreate(url, UriKind.Absolute, out uri) && uri.IsFile)
                                return uri.LocalPath;
                            return null;
                        }
                        finally
                        {
                            if (w != null && Marshal.IsComObject(w)) Marshal.ReleaseComObject(w);
                        }
                    }
                }
                finally
                {
                    if (shell != null && Marshal.IsComObject(shell)) Marshal.ReleaseComObject(shell);
                }
            }
            catch (Exception ex)
            {
                Logger.Log("[CLIP] 偵測貼上資料夾失敗: " + ex.Message);
            }
            return null;
        }
    }
}
