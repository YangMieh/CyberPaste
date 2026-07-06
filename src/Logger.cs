using System;
using System.IO;
using System.Text;
using System.Threading;

namespace CyberPaste
{
    /// <summary>輕量檔案日誌：寫到 exe 同目錄 CyberPaste.log，方便跨機問題定位。</summary>
    internal static class Logger
    {
        public const string Version = "v1.3.6";
        private static readonly object _lock = new object();
        private static string _path;

        public static void Init()
        {
            try
            {
                _path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CyberPaste.log");
                // 超過 2MB 就清空，避免無限長
                if (File.Exists(_path) && new FileInfo(_path).Length > 2 * 1024 * 1024)
                    File.WriteAllText(_path, "");
                Log("==== CyberPaste " + Version + " 啟動 " + DateTime.Now + " ====");
            }
            catch { }
        }

        public static void Log(string msg)
        {
            if (_path == null) return;
            try
            {
                string line = string.Format("{0:HH:mm:ss.fff} [T{1}] {2}{3}",
                    DateTime.Now, Thread.CurrentThread.ManagedThreadId, msg, Environment.NewLine);
                lock (_lock)
                    File.AppendAllText(_path, line, Encoding.UTF8);
            }
            catch { }
        }
    }
}
