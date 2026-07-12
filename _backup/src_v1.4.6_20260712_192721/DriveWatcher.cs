using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CyberPaste
{
    // v1.4.0:對「本機所有固定/抽取式磁碟」掛 FileSystemWatcher,監看指定副檔名(*.cyberpaste)被建立
    // (=使用者把佔位檔貼進某資料夾)。網路磁碟機/光碟機略過。回呼在背景緒。
    // v1.4.2:
    //   - Suspend()/Resume():大宗寫檔期間暫停監看,避免「我方自己寫的上千個檔」把 watcher 內部緩衝
    //     洗爆(overflow)導致之後偵測不到→就是「傳完大檔第二次貼不上」的真兇。寫完 Resume 重建乾淨監看。
    //   - Error(緩衝溢位)自動重建監看,自我修復。
    internal sealed class DriveWatcher : IDisposable
    {
        private readonly string _filter;
        private readonly List<FileSystemWatcher> _watchers = new List<FileSystemWatcher>();
        private readonly List<string> _roots = new List<string>();
        private readonly object _lock = new object();
        private int _suspendCount;
        private bool _disposed;

        public event Action<string> Created; // 被建立的檔案完整路徑
        public Action<string> OnLog;

        public DriveWatcher(string filter)
        {
            _filter = filter;
        }

        // 回傳實際監看到的磁碟字母(給 log)。
        public string Start()
        {
            StringBuilder watched = new StringBuilder();
            DriveInfo[] drives;
            try
            {
                drives = DriveInfo.GetDrives();
            }
            catch
            {
                return "";
            }
            lock (_lock)
            {
                _roots.Clear();
                foreach (DriveInfo d in drives)
                {
                    try
                    {
                        if (!d.IsReady)
                        {
                            continue;
                        }
                        // 只監看本機固定碟 + USB 抽取式;網路磁碟機、光碟機略過
                        if (d.DriveType != DriveType.Fixed && d.DriveType != DriveType.Removable)
                        {
                            continue;
                        }
                        _roots.Add(d.RootDirectory.FullName);
                        if (watched.Length > 0)
                        {
                            watched.Append(", ");
                        }
                        watched.Append(d.Name);
                    }
                    catch
                    {
                    }
                }
                BuildLocked();
            }
            return watched.ToString();
        }

        private void BuildLocked()
        {
            TearDownLocked();
            if (_disposed)
            {
                return;
            }
            foreach (string root in _roots)
            {
                try
                {
                    FileSystemWatcher w = new FileSystemWatcher(root, _filter);
                    w.IncludeSubdirectories = true;
                    w.NotifyFilter = NotifyFilters.FileName;
                    w.InternalBufferSize = 65536;
                    w.Created += OnCreated;
                    w.Error += OnError;
                    w.EnableRaisingEvents = true;
                    _watchers.Add(w);
                }
                catch
                {
                    // 個別磁碟掛不上就跳過
                }
            }
        }

        private void TearDownLocked()
        {
            foreach (FileSystemWatcher w in _watchers)
            {
                try
                {
                    w.EnableRaisingEvents = false;
                    w.Created -= OnCreated;
                    w.Error -= OnError;
                    w.Dispose();
                }
                catch
                {
                }
            }
            _watchers.Clear();
        }

        private void OnCreated(object sender, FileSystemEventArgs e)
        {
            Action<string> h = Created;
            if (h != null)
            {
                try
                {
                    h(e.FullPath);
                }
                catch
                {
                }
            }
        }

        private void OnError(object sender, ErrorEventArgs e)
        {
            Log("磁碟監看緩衝溢位/錯誤,自動重建監看");
            lock (_lock)
            {
                if (!_disposed && _suspendCount == 0)
                {
                    BuildLocked();
                }
            }
        }

        // 大宗寫檔前後包一對:寫檔期間拆掉監看(不被自己寫的大量檔洗爆),寫完重建乾淨監看。
        public void Suspend()
        {
            lock (_lock)
            {
                _suspendCount++;
                if (_suspendCount == 1)
                {
                    TearDownLocked();
                }
            }
        }

        public void Resume()
        {
            lock (_lock)
            {
                if (_suspendCount > 0)
                {
                    _suspendCount--;
                }
                if (_suspendCount == 0 && !_disposed)
                {
                    BuildLocked();
                }
            }
        }

        private void Log(string m)
        {
            Action<string> h = OnLog;
            if (h != null)
            {
                try
                {
                    h(m);
                }
                catch
                {
                }
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                _disposed = true;
                TearDownLocked();
            }
        }
    }
}
