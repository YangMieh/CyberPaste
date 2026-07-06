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

		private void ApplyFiles(IPAddress[] srcIps, Guid session, List<FileMeta> metas)
		{
			if (!Enabled)
			{
				return;
			}
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
			RetryClipboard(delegate
			{
				_liveDataObject = new VirtualFileDataObject(files);
				int num = NativeMethods.OleSetClipboard(_liveDataObject);
				if (num != 0)
				{
					Marshal.ThrowExceptionForHR(num);
				}
			});
			string text = ((srcIps != null && srcIps.Length > 0) ? srcIps[0].ToString() : "?");
			Log("已備妥 " + metas.Count + " 個遠端檔案(優先走 " + text + "),可在此機 Ctrl+V 貼出");
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
