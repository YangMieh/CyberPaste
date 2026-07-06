using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace CyberPaste
{
	public sealed class ClipboardService : IDisposable
	{
		private sealed class ListenerWindow : Form
		{
			public event Action ClipboardUpdated;

			public ListenerWindow()
			{
				base.ShowInTaskbar = false;
				base.FormBorderStyle = FormBorderStyle.FixedToolWindow;
				base.StartPosition = FormStartPosition.Manual;
				base.Location = new Point(-2000, -2000);
				base.Size = new Size(1, 1);
			}

			public void CreateHandleNow()
			{
				IntPtr handle = base.Handle;
			}

			protected override void SetVisibleCore(bool value)
			{
				base.SetVisibleCore(false);
			}

			protected override void WndProc(ref Message m)
			{
				if (m.Msg == 797)
				{
					Action clipboardUpdated = this.ClipboardUpdated;
					if (clipboardUpdated != null)
					{
						clipboardUpdated();
					}
				}
				base.WndProc(ref m);
			}
		}

		private const int WM_CLIPBOARDUPDATE = 797;

		private readonly NetworkService _net;

		private readonly ListenerWindow _window;

		private readonly LinkedList<string> _recent = new LinkedList<string>();

		private readonly object _recentLock = new object();

		private DateTime _suppressFilesUntil = DateTime.MinValue;

		private VirtualFileDataObject _liveDataObject;

		public bool Enabled = true;

		public Action<string> OnLog;

		public Control SyncContext
		{
			get
			{
				return _window;
			}
		}

		[DllImport("user32.dll", SetLastError = true)]
		private static extern bool AddClipboardFormatListener(IntPtr hwnd);

		[DllImport("user32.dll", SetLastError = true)]
		private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

		// ── v1.4.0:檔案一律走大宗框。收到檔案 announce→在剪貼簿放一個真實的「隱藏佔位小檔」
		//    (.cyberpaste,內容帶 magic+session)。使用者用 Ctrl+V / 右鍵貼上 / 工具列貼上(三者皆可,
		//    因為是真實檔案)→ Explorer 把佔位檔複製到目標資料夾→ DriveWatcher 監看各本機磁碟抓到它
		//    →讀出 session→對「佔位檔所在的那個資料夾」跑大宗傳輸→完成後關框+刪佔位檔。
		//    完全不碰延遲渲染/GetData→無衝突、無 v1.3.6 亂寫、三種貼上全通。
		private const string PlaceholderExt = ".cyberpaste";

		private const string PlaceholderMagic = "CYBERPASTE-PLACEHOLDER-V1";

		private const string PlaceholderBaseName = "CyberPaste-Receiving";

		private string _placeholderDir;

		private DriveWatcher _driveWatcher;

		// 有大宗傳輸正在進行(以「佔位檔完整路徑」為 key),避免同一次貼上被 Watcher 重複觸發。
		private readonly HashSet<string> _activePaths = new HashSet<string>();

		private readonly object _activeLock = new object();

		private sealed class PendingRecv
		{
			public IPAddress[] SrcIps;

			public List<FileMeta> Metas;

			public long Total;
		}

		private readonly System.Collections.Generic.Dictionary<Guid, PendingRecv> _pending =
			new System.Collections.Generic.Dictionary<Guid, PendingRecv>();

		private readonly object _pendingLock = new object();

		public ClipboardService(NetworkService net)
		{
			_net = net;
			NetworkService net2 = _net;
			Action<string> onText = delegate(string png)
			{
				Post(delegate
				{
					ApplyText(png);
				});
			};
			net2.OnText = onText;
			_net.OnImagePng = delegate(byte[] bytes)
			{
				Post(delegate
				{
					ApplyImage(bytes);
				});
			};
			_net.OnFilesAnnounce = delegate(IPAddress[] ips, Guid sid, List<FileMeta> metas)
			{
				Post(delegate
				{
					ApplyFiles(ips, sid, metas);
				});
			};
			_window = new ListenerWindow();
			_window.ClipboardUpdated += OnClipboardUpdated;
			_window.CreateHandleNow();
			AddClipboardFormatListener(_window.Handle);
			InitPlaceholderAndWatcher();
		}

		private void InitPlaceholderAndWatcher()
		{
			try
			{
				_placeholderDir = Path.Combine(Path.GetTempPath(), "CyberPaste");
				Directory.CreateDirectory(_placeholderDir);
				// 清掉上次殘留的來源佔位檔(貼出去的那些在使用者資料夾,傳完會自刪)
				try
				{
					string[] old = Directory.GetFiles(_placeholderDir, "*" + PlaceholderExt);
					foreach (string f in old)
					{
						try
						{
							File.Delete(f);
						}
						catch
						{
						}
					}
				}
				catch
				{
				}
			}
			catch (Exception ex)
			{
				Log("佔位檔目錄建立失敗: " + ex.Message);
			}
			try
			{
				_driveWatcher = new DriveWatcher("*" + PlaceholderExt);
				_driveWatcher.OnLog = Log;
				_driveWatcher.Created += OnPlaceholderCreated;
				string drives = _driveWatcher.Start();
				Log("磁碟監看啟動,監看槽: " + drives);
			}
			catch (Exception ex)
			{
				Log("磁碟監看啟動失敗: " + ex.Message);
			}
		}

		// DriveWatcher 在背景緒回報「某處出現 .cyberpaste」。丟回 UI 緒處理。
		private void OnPlaceholderCreated(string fullPath)
		{
			// 忽略我們自己在 temp 建立的來源佔位檔(只處理「被貼到使用者資料夾」的複本)
			if (string.IsNullOrEmpty(fullPath))
			{
				return;
			}
			try
			{
				if (!string.IsNullOrEmpty(_placeholderDir) &&
					fullPath.StartsWith(_placeholderDir, StringComparison.OrdinalIgnoreCase))
				{
					return;
				}
			}
			catch
			{
			}
			Log("偵測到佔位檔: " + fullPath);
			Post(delegate
			{
				HandlePastedPlaceholder(fullPath);
			});
		}

		// UI 緒:讀佔位檔認 session→(衝突則問覆蓋)→對佔位檔所在資料夾跑大宗→完成刪佔位檔+關框。
		private void HandlePastedPlaceholder(string fullPath)
		{
			// 去重+搶佔:HashSet.Add 回 false 表示已有人在處理這個路徑(Watcher 可能重複觸發)
			lock (_activeLock)
			{
				if (!_activePaths.Add(fullPath))
				{
					return;
				}
			}
			bool handedOff = false;
			try
			{
				Guid session;
				if (!TryReadPlaceholder(fullPath, out session))
				{
					return; // 不是我們的佔位檔 / 讀不到
				}
				// #7:讀到就立刻刪掉隱藏佔位檔——它的任務(觸發)已完成,不必等傳完,讓它馬上消失。
				TryDeletePlaceholder(fullPath);
				PendingRecv pend;
				lock (_pendingLock)
				{
					_pending.TryGetValue(session, out pend);
				}
				if (pend == null || pend.Metas == null || pend.Metas.Count == 0)
				{
					return; // 過期/未知 session
				}
				string dest;
				try
				{
					dest = Path.GetDirectoryName(fullPath);
				}
				catch
				{
					dest = null;
				}
				if (string.IsNullOrEmpty(dest))
				{
					return;
				}

				// 覆蓋詢問:目的資料夾有沒有同名檔?(找到第一個就停,不必掃完上千個→快)
				bool hasConflict = false;
				try
				{
					foreach (FileMeta m in pend.Metas)
					{
						if (File.Exists(Path.Combine(dest, m.Name)))
						{
							hasConflict = true;
							break;
						}
					}
				}
				catch
				{
				}
				bool skipExisting = false;
				if (hasConflict)
				{
					OverwriteDialog.Result r = OverwriteDialog.Ask();
					if (r == OverwriteDialog.Result.Cancel)
					{
						return; // 佔位檔已刪
					}
					skipExisting = (r == OverwriteDialog.Result.Skip);
				}

				IPAddress[] srcIps = pend.SrcIps;
				List<FileMeta> metas = pend.Metas;
				long total = pend.Total;
				string peer = ((srcIps != null && srcIps.Length > 0) ? srcIps[0].ToString() : "?");
				Log("大宗接收啟動(貼上) → " + dest + (skipExisting ? " [略過已存在]" : "") + " 共 " + metas.Count + " 檔");
				BulkProgressForm form = null;
				try
				{
					form = new BulkProgressForm(peer, total);
					form.Show();
				}
				catch
				{
				}
				BulkProgressForm formRef = form;
				bool skipCopy = skipExisting;
				string destCopy = dest;
				Guid sessionCopy = session;
				handedOff = true;
				ThreadPool.QueueUserWorkItem(delegate
				{
					try
					{
						// onActiveWrite:只在「實際寫檔」時暫停磁碟監看(免得自己寫入洗爆 watcher);
						// 一旦斷線/取消進入重連等待就恢復監看→使用者能立刻貼下一個,不必等重連迴圈跑完(v1.4.3)
						_net.ReceiveBulk(srcIps, sessionCopy, metas, destCopy, skipCopy,
							delegate(bool activeWrite)
							{
								try
								{
									if (_driveWatcher != null)
									{
										if (activeWrite)
										{
											_driveWatcher.Suspend();
										}
										else
										{
											_driveWatcher.Resume();
										}
									}
								}
								catch
								{
								}
							},
							delegate(NetworkService.BulkProgress p)
							{
								if (formRef != null)
								{
									try
									{
										formRef.BeginInvoke((Action)delegate
										{
											formRef.UpdateProgress(p);
										});
									}
									catch
									{
									}
								}
							});
					}
					catch (Exception ex)
					{
						Log("大宗接收失敗: " + ex.Message);
						if (formRef != null)
						{
							try
							{
								formRef.BeginInvoke((Action)delegate
								{
									formRef.MarkFailed(ex.Message);
								});
							}
							catch
							{
							}
						}
					}
					finally
					{
						// 保底:確保監看已恢復(正常情況 ReceiveBulk 的 onActiveWrite(false) 已恢復,這裡再保險一次)
						try
						{
							if (_driveWatcher != null)
							{
								_driveWatcher.Resume();
							}
						}
						catch
						{
						}
						lock (_activeLock)
						{
							_activePaths.Remove(fullPath);
						}
					}
				});
			}
			finally
			{
				if (!handedOff)
				{
					lock (_activeLock)
					{
						_activePaths.Remove(fullPath);
					}
				}
			}
		}

		// 讀佔位檔:驗 magic,取出 session GUID。含小重試(剛被 Explorer 複製過來可能短暫鎖住)。
		private bool TryReadPlaceholder(string path, out Guid session)
		{
			session = Guid.Empty;
			for (int attempt = 0; attempt < 5; attempt++)
			{
				try
				{
					string[] lines = File.ReadAllLines(path, Encoding.UTF8);
					if (lines.Length >= 2 && lines[0] == PlaceholderMagic && Guid.TryParse(lines[1], out session))
					{
						return true;
					}
					return false;
				}
				catch
				{
					try
					{
						Thread.Sleep(80);
					}
					catch
					{
					}
				}
			}
			return false;
		}

		private void TryDeletePlaceholder(string path)
		{
			// 我方一次性佔位小檔(30 bytes 隱藏檔),傳完就地永久刪除(進回收筒只會製造垃圾);
			// 只刪副檔名為 .cyberpaste 者,不碰使用者其他檔。
			try
			{
				if (!string.IsNullOrEmpty(path) &&
					path.EndsWith(PlaceholderExt, StringComparison.OrdinalIgnoreCase) &&
					File.Exists(path))
				{
					File.SetAttributes(path, FileAttributes.Normal);
					File.Delete(path);
				}
			}
			catch
			{
			}
		}

		private void Post(Action a)
		{
			try
			{
				if (_window.IsHandleCreated)
				{
					_window.BeginInvoke(a);
				}
			}
			catch
			{
			}
		}

		private void OnClipboardUpdated()
		{
			if (!Enabled)
			{
				return;
			}
			try
			{
				if (Clipboard.ContainsFileDropList())
				{
					AnnounceLocalFiles();
					return;
				}
				byte[] array = ReadClipboardPng();
				if (array != null)
				{
					string sig = "I:" + Hash(array);
					if (!IsRecent(sig))
					{
						Remember(sig);
						_net.BroadcastImage(array);
						Log("已推播圖片 " + array.Length + " bytes 給 " + PeerCount() + " 台");
					}
				}
				else
				{
					if (!Clipboard.ContainsText())
					{
						return;
					}
					string text = Clipboard.GetText();
					if (!string.IsNullOrEmpty(text))
					{
						string sig2 = "T:" + Hash(Encoding.UTF8.GetBytes(text));
						if (!IsRecent(sig2))
						{
							Remember(sig2);
							_net.BroadcastText(text);
							Log("已推播文字 " + text.Length + " 字給 " + PeerCount() + " 台");
						}
					}
				}
			}
			catch (Exception ex)
			{
				Log("讀剪貼簿失敗: " + ex.Message);
			}
		}

		private void AnnounceLocalFiles()
		{
			StringCollection fileDropList = Clipboard.GetFileDropList();
			List<FileMeta> list = new List<FileMeta>();
			List<string> list2 = new List<string>();
			StringEnumerator enumerator = fileDropList.GetEnumerator();
			try
			{
				while (enumerator.MoveNext())
				{
					string current = enumerator.Current;
					// 跳過我方佔位檔(.cyberpaste)——那是接收觸發用的標記,不是要傳的內容
					if (current != null && current.EndsWith(PlaceholderExt, StringComparison.OrdinalIgnoreCase))
					{
						continue;
					}
					try
					{
						if (Directory.Exists(current))
						{
							string directoryName = Path.GetDirectoryName(current.TrimEnd(Path.DirectorySeparatorChar));
							string[] files = Directory.GetFiles(current, "*", SearchOption.AllDirectories);
							foreach (string text in files)
							{
								string name = text.Substring(directoryName.Length + 1);
								FileInfo fileInfo = new FileInfo(text);
								list.Add(new FileMeta
								{
									Name = name,
									Size = fileInfo.Length
								});
								list2.Add(text);
							}
						}
						else if (File.Exists(current))
						{
							FileInfo fileInfo2 = new FileInfo(current);
							list.Add(new FileMeta
							{
								Name = Path.GetFileName(current),
								Size = fileInfo2.Length
							});
							list2.Add(current);
						}
					}
					catch (Exception ex)
					{
						Log("展開檔案失敗 " + current + ": " + ex.Message);
					}
				}
			}
			finally
			{
				IDisposable disposable = enumerator as IDisposable;
				if (disposable != null)
				{
					disposable.Dispose();
				}
			}
			if (list.Count != 0)
			{
				string sig = "F:" + Hash(Encoding.UTF8.GetBytes(string.Join("|", list2.ToArray())));
				if (!IsRecent(sig))
				{
					Remember(sig);
					Guid session = Guid.NewGuid();
					_net.AnnounceFiles(session, list, list2.ToArray());
					Log("已 announce " + list.Count + " 個檔案給 " + PeerCount() + " 台（等對方貼上才傳）");
				}
			}
		}

		private void ApplyText(string text)
		{
			if (Enabled)
			{
				string sig = "T:" + Hash(Encoding.UTF8.GetBytes(text ?? ""));
				Remember(sig);
				RetryClipboard(delegate
				{
					Clipboard.SetText(text ?? "");
				});
				Log("已套用遠端文字");
			}
		}

		private void ApplyImage(byte[] png)
		{
			if (!Enabled)
			{
				return;
			}
			string sig = "I:" + Hash(png);
			Remember(sig);
			RetryClipboard(delegate
			{
				DataObject dataObject = new DataObject();
				MemoryStream data = new MemoryStream(png);
				dataObject.SetData("PNG", false, data);
				using (Bitmap original = (Bitmap)Image.FromStream(new MemoryStream(png)))
				{
					dataObject.SetImage(new Bitmap(original));
				}
				Clipboard.SetDataObject(dataObject, true);
			});
			Log("已套用遠端圖片");
		}

		// v1.4.0:收到檔案 announce → 記住待收清單(依 session) + 在剪貼簿放一個「隱藏佔位小檔」。
		// 使用者用任何方式貼上(Ctrl+V/右鍵/工具列)→ Explorer 把佔位檔複製到目標資料夾 →
		// DriveWatcher 抓到 → HandlePastedPlaceholder 對那個資料夾跑大宗。文字/圖片仍走一般剪貼簿。
		private void ApplyFiles(IPAddress[] srcIps, Guid session, List<FileMeta> metas)
		{
			if (!Enabled)
			{
				return;
			}
			long total = 0L;
			for (int j = 0; j < metas.Count; j++)
			{
				total += metas[j].Size;
			}
			lock (_pendingLock)
			{
				_pending[session] = new PendingRecv
				{
					SrcIps = srcIps,
					Metas = metas,
					Total = total
				};
				// 只留最近幾批,避免無限長
				if (_pending.Count > 8)
				{
					Guid oldest = Guid.Empty;
					foreach (Guid k in _pending.Keys)
					{
						oldest = k;
						break;
					}
					_pending.Remove(oldest);
				}
			}
			// 建佔位檔(內容:magic + session),放剪貼簿當真實檔案清單
			string placeholder = null;
			try
			{
				if (string.IsNullOrEmpty(_placeholderDir))
				{
					_placeholderDir = Path.Combine(Path.GetTempPath(), "CyberPaste");
					Directory.CreateDirectory(_placeholderDir);
				}
				placeholder = Path.Combine(_placeholderDir, PlaceholderBaseName + PlaceholderExt);
				// 佔位檔用固定檔名:第二次以後要覆寫既有的「隱藏」檔會被拒(access denied)→ 先清屬性再寫。
				// (這正是 v1.4.0「傳完大量檔案後要重開」的真兇)
				try
				{
					if (File.Exists(placeholder))
					{
						File.SetAttributes(placeholder, FileAttributes.Normal);
					}
				}
				catch
				{
				}
				File.WriteAllText(placeholder, PlaceholderMagic + "\r\n" + session.ToString("D") + "\r\n", Encoding.UTF8);
				try
				{
					File.SetAttributes(placeholder, FileAttributes.Hidden);
				}
				catch
				{
				}
			}
			catch (Exception ex)
			{
				Log("建立佔位檔失敗: " + ex.Message);
				return;
			}
			_suppressFilesUntil = DateTime.UtcNow.AddSeconds(2.0);
			string ph = placeholder;
			RetryClipboard(delegate
			{
				DataObject dataObject = new DataObject();
				System.Collections.Specialized.StringCollection sc = new System.Collections.Specialized.StringCollection();
				sc.Add(ph);
				dataObject.SetFileDropList(sc);
				Clipboard.SetDataObject(dataObject, true);
			});
			string text = ((srcIps != null && srcIps.Length > 0) ? srcIps[0].ToString() : "?");
			Log("已備妥 " + metas.Count + " 個遠端檔案(" + total / 1048576L + " MB, 來源 " + text + "),貼到任意資料夾即開始接收");
		}

		private void Remember(string sig)
		{
			lock (_recentLock)
			{
				_recent.AddFirst(sig);
				while (_recent.Count > 24)
				{
					_recent.RemoveLast();
				}
			}
		}

		private bool IsRecent(string sig)
		{
			lock (_recentLock)
			{
				return _recent.Contains(sig);
			}
		}

		private static byte[] ReadClipboardPng()
		{
			try
			{
				if (Clipboard.ContainsData("PNG"))
				{
					object data = Clipboard.GetData("PNG");
					MemoryStream memoryStream = data as MemoryStream;
					if (memoryStream != null)
					{
						return memoryStream.ToArray();
					}
					byte[] array = data as byte[];
					if (array != null)
					{
						return array;
					}
				}
				if (Clipboard.ContainsImage())
				{
					using (Image image = Clipboard.GetImage())
					{
						using (MemoryStream memoryStream2 = new MemoryStream())
						{
							image.Save(memoryStream2, ImageFormat.Png);
							return memoryStream2.ToArray();
						}
					}
				}
			}
			catch
			{
			}
			return null;
		}

		private static string Hash(byte[] data)
		{
			using (SHA1 sHA = SHA1.Create())
			{
				return BitConverter.ToString(sHA.ComputeHash(data));
			}
		}

		private static void RetryClipboard(Action a)
		{
			for (int i = 0; i < 6; i++)
			{
				try
				{
					a();
					return;
				}
				catch (ExternalException)
				{
					Thread.Sleep(60);
				}
			}
			try
			{
				a();
			}
			catch
			{
			}
		}

		private int PeerCount()
		{
			int num = 0;
			foreach (Peer peer in _net.Peers)
			{
				Peer peer2 = peer;
				num++;
			}
			return num;
		}

		private void Log(string m)
		{
			Logger.Log("[CLIP] " + m);
			if (OnLog != null)
			{
				OnLog(m);
			}
		}

		public void Dispose()
		{
			try
			{
				if (_driveWatcher != null)
				{
					_driveWatcher.Dispose();
					_driveWatcher = null;
				}
			}
			catch
			{
			}
			try
			{
				RemoveClipboardFormatListener(_window.Handle);
			}
			catch
			{
			}
			try
			{
				_window.Dispose();
			}
			catch
			{
			}
		}
	}
}
