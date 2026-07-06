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

		// ── v1.3.8 大宗:用「真正的 Ctrl+V 鍵」當觸發(WH_KEYBOARD_LL),而非 GetData(GetData 會被
		//    shell 剪貼簿預覽誤觸→就是 v1.3.6 那個 0.066 秒自動亂寫的元兇)。hook 只記時間戳,極輕量。
		private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

		[DllImport("user32.dll", SetLastError = true)]
		private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

		[DllImport("user32.dll", SetLastError = true)]
		private static extern bool UnhookWindowsHookEx(IntPtr hhk);

		[DllImport("user32.dll")]
		private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

		[DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
		private static extern IntPtr GetModuleHandle(string lpModuleName);

		[DllImport("user32.dll")]
		private static extern short GetAsyncKeyState(int vKey);

		private const int WH_KEYBOARD_LL = 13;

		private const int WM_KEYDOWN = 256;

		private const int WM_SYSKEYDOWN = 260;

		// 使用者最近一次真的按 Ctrl+V 的時間戳(TickCount);大宗接管只在這之後很短的窗內才允許。
		private volatile int _armedTick = -100000;

		// 有大宗傳輸正在進行,避免同一份剪貼簿被重複開第二條。
		private volatile bool _bulkActive;

		private IntPtr _hookHandle = IntPtr.Zero;

		private LowLevelKeyboardProc _hookProc; // 保存參考避免被 GC

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
			InstallKeyboardHook();
		}

		private void InstallKeyboardHook()
		{
			try
			{
				_hookProc = KeyboardProc;
				using (System.Diagnostics.Process p = System.Diagnostics.Process.GetCurrentProcess())
				using (System.Diagnostics.ProcessModule m = p.MainModule)
				{
					_hookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc, GetModuleHandle(m.ModuleName), 0u);
				}
				if (_hookHandle == IntPtr.Zero)
				{
					Log("鍵盤 hook 安裝失敗(大宗接管將無法觸發,不影響一般貼上)");
				}
			}
			catch (Exception ex)
			{
				Log("鍵盤 hook 安裝例外: " + ex.Message);
			}
		}

		// 極輕量:只在 Ctrl+V 按下時記時間戳,絕不在此做任何重活(LL hook 要快回)。
		private IntPtr KeyboardProc(int nCode, IntPtr wParam, IntPtr lParam)
		{
			try
			{
				if (nCode >= 0)
				{
					int msg = wParam.ToInt32();
					if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
					{
						int vk = Marshal.ReadInt32(lParam);
						if (vk == 0x56 && (GetAsyncKeyState(0x11) & 0x8000) != 0)
						{
							_armedTick = Environment.TickCount;
						}
					}
				}
			}
			catch
			{
			}
			return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
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

		// v1.3.8 大宗模式門檻:總量 ≥ 500MB 就走大宗高速循序傳輸,否則維持逐檔延遲渲染。
		private const long BulkThresholdBytes = 524288000L;

		// v1.3.8:大宗改用「真正的 Ctrl+V 鍵盤 hook」當觸發(見 KeyboardProc/_armedTick),
		// 只有使用者確實按了 Ctrl+V(近 _armedWindowMs 毫秒內)+前景是檔案總管資料夾,才接管。
		// 這解決了 v1.3.6 靠 GetData 觸發被 shell 預覽誤觸→自動亂寫的致命缺陷。
		private const bool EnableBulk = true;

		private const int _armedWindowMs = 1200;

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
			bool bulk = EnableBulk && total >= BulkThresholdBytes;

			List<VirtualFile> files = new List<VirtualFile>(metas.Count);
			for (int i = 0; i < metas.Count; i++)
			{
				int index = i;
				FileMeta fileMeta = metas[i];
				files.Add(new VirtualFile
				{
					Name = fileMeta.Name,
					Length = fileMeta.Size,
					OpenRead = () => _net.OpenPull(srcIps, session, index)
				});
			}
			_suppressFilesUntil = DateTime.UtcNow.AddSeconds(2.0);

			Func<bool> onBulk = null;
			if (bulk)
			{
				long totalCopy = total;
				onBulk = () => TryStartBulk(srcIps, session, metas, totalCopy);
			}

			RetryClipboard(delegate
			{
				_liveDataObject = new VirtualFileDataObject(files, onBulk);
				int num = NativeMethods.OleSetClipboard(_liveDataObject);
				if (num != 0)
				{
					Marshal.ThrowExceptionForHR(num);
				}
			});
			string text = ((srcIps != null && srcIps.Length > 0) ? srcIps[0].ToString() : "?");
			Log("已備妥 " + metas.Count + " 個遠端檔案(優先走 " + text + (onBulk != null ? ", 大宗模式" : "") + "),可在此機 Ctrl+V 貼出");
		}

		// 貼上時 Explorer 呼叫 GetData→這裡。回 true=我方接管大宗(給 Explorer 空清單、自己高速寫檔);
		// 回 false=交還 Explorer 走逐檔延遲渲染(貼哪到哪)。
		// ★安全閘:必須是「使用者剛真的按了 Ctrl+V」(_armedTick 在窗內)才接管;shell 剪貼簿預覽的
		//   GetData 沒有前置 Ctrl+V→armed 過期→回 false,絕不會像 v1.3.6 那樣自動亂寫。
		private bool TryStartBulk(IPAddress[] srcIps, Guid session, List<FileMeta> metas, long total)
		{
			// 閘 1:近期真的有 Ctrl+V?(非真實貼上一律不接管)
			int since = Environment.TickCount - _armedTick;
			if (since < 0 || since > _armedWindowMs)
			{
				return false;
			}
			// 閘 2:已有大宗在傳→給空清單避免 Explorer 逐檔重複寫,但不重開第二條
			if (_bulkActive)
			{
				return true;
			}
			// 閘 3:抓得到「你正貼上的那個檔案總管資料夾」?抓不到(貼進非檔案總管)→退回逐檔
			string dest;
			try
			{
				dest = ShellFolder.GetForegroundPasteFolder();
			}
			catch
			{
				dest = null;
			}
			if (string.IsNullOrEmpty(dest))
			{
				Log("大宗模式:抓不到貼上資料夾,退回逐檔延遲渲染");
				return false;
			}
			_armedTick = -100000; // 消費掉,避免同一次貼上被再次判定
			_bulkActive = true;
			string peer = ((srcIps != null && srcIps.Length > 0) ? srcIps[0].ToString() : "?");
			string destCopy = dest;
			Log("大宗接收啟動(Ctrl+V) → " + dest);
			// 延後到 UI 訊息佇列建框+開背景傳輸(不在 COM callback 裡硬做,修 v1.3.6 進度框卡死)
			Post(delegate
			{
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
				ThreadPool.QueueUserWorkItem(delegate
				{
					try
					{
						_net.ReceiveBulk(srcIps, session, metas, destCopy, delegate(NetworkService.BulkProgress p)
						{
							if (formRef == null)
							{
								return;
							}
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
						});
					}
					catch (Exception ex)
					{
						Log("大宗接收失敗: " + ex.Message);
						try
						{
							if (formRef != null)
							{
								formRef.BeginInvoke((Action)delegate
								{
									formRef.MarkFailed(ex.Message);
								});
							}
						}
						catch
						{
						}
					}
					finally
					{
						_bulkActive = false;
					}
				});
			});
			return true;
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
				if (_hookHandle != IntPtr.Zero)
				{
					UnhookWindowsHookEx(_hookHandle);
					_hookHandle = IntPtr.Zero;
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
