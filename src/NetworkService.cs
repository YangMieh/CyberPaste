using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace CyberPaste
{
	public sealed class NetworkService : IDisposable
	{
		private struct Served
		{
			public string Path;

			public long Size;
		}

		private sealed class PooledConn
		{
			public TcpClient Client;

			public NetworkStream Ns;

			public IPAddress Ip;
		}

		private sealed class NetworkFileReadStream : Stream
		{
			private const int ReconnectMaxTries = 15;

			private const int ReconnectDelayMs = 500;

			private readonly NetworkService _svc;

			private readonly IPAddress[] _sources;

			private int _srcIdx;

			private readonly Guid _session;

			private readonly int _index;

			private readonly long _fileLen;

			private long _delivered;

			private PooledConn _conn;

			private NetworkStream _ns;

			private bool _done;

			public override bool CanRead
			{
				get
				{
					return true;
				}
			}

			public override bool CanSeek
			{
				get
				{
					return false;
				}
			}

			public override bool CanWrite
			{
				get
				{
					return false;
				}
			}

			public override long Length
			{
				get
				{
					throw new NotSupportedException();
				}
			}

			public override long Position
			{
				get
				{
					throw new NotSupportedException();
				}
				set
				{
					throw new NotSupportedException();
				}
			}

			public NetworkFileReadStream(NetworkService svc, IPAddress[] sources, Guid session, int index)
			{
				_svc = svc;
				_session = session;
				_index = index;
				_sources = ((sources != null && sources.Length > 0) ? sources : new IPAddress[1] { IPAddress.Loopback });
				long num = OpenSegment(0L);
				if (num < 0)
				{
					svc.ReturnConn(_conn, true);
					_conn = null;
					Logger.Log("[NET] 拉檔 #" + index + " 失敗：來源端找不到該檔");
					throw new IOException("來源端找不到該檔（session 已過期或檔案不在）");
				}
				_fileLen = num;
				if (_fileLen == 0)
				{
					_done = true;
				}
				Logger.Log("[NET] 拉檔 #" + index + " 開始 (" + num + " bytes) from " + _sources[_srcIdx]);
			}

			private long OpenSegment(long offset)
			{
				for (int i = 0; i < _sources.Length; i++)
				{
					int num = (_srcIdx + i) % _sources.Length;
					try
					{
						_conn = _svc.RentConn(_sources[num]);
						_ns = _conn.Ns;
						_ns.WriteByte(4);
						_ns.Write(_session.ToByteArray(), 0, 16);
						WriteInt32(_ns, _index);
						WriteInt64(_ns, offset);
						_ns.Flush();
						long result = ReadInt64(_ns);
						if (num != _srcIdx)
						{
							Logger.Log("[NET] 拉檔 #" + _index + " 切換路徑 → " + _sources[num]);
						}
						_srcIdx = num;
						return result;
					}
					catch
					{
						try
						{
							if (_conn != null)
							{
								_conn.Client.Close();
							}
						}
						catch
						{
						}
						_conn = null;
					}
				}
				throw new IOException("所有候選路徑都連不上");
			}

			public override int Read(byte[] buffer, int offset, int count)
			{
				if (_done || _delivered >= _fileLen)
				{
					_done = true;
					return 0;
				}
				int count2 = (int)Math.Min(count, _fileLen - _delivered);
				int num = 0;
				while (true)
				{
					try
					{
						int num2 = _ns.Read(buffer, offset, count2);
						if (num2 > 0)
						{
							_delivered += num2;
							if (_delivered >= _fileLen)
							{
								_done = true;
							}
							return num2;
						}
					}
					catch
					{
					}
					if (_delivered >= _fileLen)
					{
						_done = true;
						return 0;
					}
					try
					{
						if (_conn != null)
						{
							_conn.Client.Close();
						}
					}
					catch
					{
					}
					_conn = null;
					if (num >= 15)
					{
						break;
					}
					try
					{
						Thread.Sleep(500);
					}
					catch
					{
					}
					long num3;
					try
					{
						num3 = OpenSegment(_delivered);
					}
					catch
					{
						goto IL_01e5;
					}
					if (num3 < 0)
					{
						try
						{
							if (_conn != null)
							{
								_conn.Client.Close();
							}
						}
						catch
						{
						}
						_conn = null;
					}
					else
					{
						Logger.Log("[NET] 拉檔 #" + _index + " 斷線續傳 from offset " + _delivered + " (剩 " + num3 + " bytes)");
					}
					goto IL_01e5;
					IL_01e5:
					num++;
				}
				Logger.Log("[NET] 拉檔 #" + _index + " 續傳失敗(重連" + 15 + "次仍不通),已交 " + _delivered + "/" + _fileLen);
				throw new IOException("拉檔中斷且無法續傳");
			}

			protected override void Dispose(bool disposing)
			{
				if (disposing && _conn != null)
				{
					_svc.ReturnConn(_conn, _done && _delivered >= _fileLen);
					_conn = null;
				}
				base.Dispose(disposing);
			}

			public override void Flush()
			{
			}

			public override long Seek(long o, SeekOrigin r)
			{
				throw new NotSupportedException();
			}

			public override void SetLength(long v)
			{
				throw new NotSupportedException();
			}

			public override void Write(byte[] b, int o, int c)
			{
				throw new NotSupportedException();
			}
		}

		public const int UdpPort = 45888;

		public const int TcpPort = 45889;

		private const byte MSG_TEXT = 1;

		private const byte MSG_IMAGE = 2;

		private const byte MSG_FILES_ANNOUNCE = 3;

		private const byte MSG_FILE_GET = 4;

		private const int PoolCapPerPeer = 8;

		private UdpClient _udp;

		private TcpListener _tcp;

		private Timer _beaconTimer;

		private Timer _pruneTimer;

		private Timer _heartbeatTimer;

		private volatile bool _running;

		private readonly string _myName = Environment.MachineName;

		private readonly HashSet<string> _localIps;

		private readonly ConcurrentDictionary<string, Peer> _peers = new ConcurrentDictionary<string, Peer>();

		private readonly ConcurrentDictionary<Guid, Served[]> _served = new ConcurrentDictionary<Guid, Served[]>();

		private readonly object _servedOrderLock = new object();

		private readonly Queue<Guid> _servedOrder = new Queue<Guid>();

		public Action<string> OnText;

		public Action<byte[]> OnImagePng;

		public Action<IPAddress[], Guid, List<FileMeta>> OnFilesAnnounce;

		public Action<string> OnLog;

		private readonly ConcurrentDictionary<string, ConcurrentStack<PooledConn>> _pool = new ConcurrentDictionary<string, ConcurrentStack<PooledConn>>();

		public IEnumerable<Peer> Peers
		{
			get
			{
				return _peers.Values.ToArray();
			}
		}

		public NetworkService()
		{
			_localIps = new HashSet<string>(from a in Dns.GetHostAddresses(Dns.GetHostName())
				where a.AddressFamily == AddressFamily.InterNetwork
				select a.ToString());
			_localIps.Add("127.0.0.1");
		}

		public void Start()
		{
			_running = true;
			_udp = new UdpClient(new IPEndPoint(IPAddress.Any, 45888));
			_udp.EnableBroadcast = true;
			BeginUdpReceive();
			_tcp = new TcpListener(IPAddress.Any, 45889);
			_tcp.Start();
			BeginTcpAccept();
			_beaconTimer = new Timer(delegate
			{
				SendBeacon();
			}, null, 0, 3000);
			_pruneTimer = new Timer(delegate
			{
				Prune();
			}, null, 5000, 5000);
			_heartbeatTimer = new Timer(delegate
			{
				Heartbeat();
			}, null, 30000, 30000);
			Log("已啟動，UDP " + 45888 + " / TCP " + 45889);
		}

		private void SendBeacon()
		{
			try
			{
				byte[] bytes = Encoding.UTF8.GetBytes("CYBERPASTE|" + _myName);
				foreach (IPEndPoint item in BeaconTargets())
				{
					try
					{
						_udp.Send(bytes, bytes.Length, item);
					}
					catch
					{
					}
				}
				byte[] bytes2 = Encoding.UTF8.GetBytes("CYBERREPLY|" + _myName);
				Peer[] array = _peers.Values.ToArray();
				foreach (Peer peer in array)
				{
					try
					{
						_udp.Send(bytes2, bytes2.Length, new IPEndPoint(peer.Ip, 45888));
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

		private static List<IPEndPoint> BeaconTargets()
		{
			List<IPEndPoint> list = new List<IPEndPoint>();
			list.Add(new IPEndPoint(IPAddress.Broadcast, 45888));
			try
			{
				NetworkInterface[] allNetworkInterfaces = NetworkInterface.GetAllNetworkInterfaces();
				foreach (NetworkInterface networkInterface in allNetworkInterfaces)
				{
					if (networkInterface.OperationalStatus != OperationalStatus.Up || networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
					{
						continue;
					}
					foreach (UnicastIPAddressInformation unicastAddress in networkInterface.GetIPProperties().UnicastAddresses)
					{
						if (unicastAddress.Address.AddressFamily != AddressFamily.InterNetwork)
						{
							continue;
						}
						byte[] addressBytes = unicastAddress.Address.GetAddressBytes();
						byte[] addressBytes2;
						try
						{
							addressBytes2 = unicastAddress.IPv4Mask.GetAddressBytes();
						}
						catch
						{
							continue;
						}
						if (addressBytes2 == null || addressBytes2.Length != 4)
						{
							continue;
						}
						bool flag = false;
						byte[] array = new byte[4];
						for (int j = 0; j < 4; j++)
						{
							array[j] = (byte)(addressBytes[j] | (~addressBytes2[j] & 0xFF));
							if (addressBytes2[j] != 0)
							{
								flag = true;
							}
						}
						if (flag)
						{
							list.Add(new IPEndPoint(new IPAddress(array), 45888));
						}
					}
				}
			}
			catch
			{
			}
			return list;
		}

		private void Heartbeat()
		{
			if (_running)
			{
				Logger.Log("[HB] 存活 夥伴=" + _peers.Count);
			}
		}

		private static int NicQuality(NetworkInterface ni)
		{
			string text = (ni.Description + " " + ni.Name).ToLowerInvariant();
			int num = ((ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel || text.Contains("hamachi") || text.Contains("vpn") || text.Contains("radmin") || text.Contains("zerotier") || text.Contains("tailscale") || text.Contains("wireguard")) ? 20 : ((ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet) ? 1000 : ((ni.NetworkInterfaceType != NetworkInterfaceType.Wireless80211) ? 200 : 500)));
			try
			{
				num += (int)Math.Min(300L, ni.Speed / 10000000);
			}
			catch
			{
			}
			return num;
		}

		private static byte[][] LocalIPv4List()
		{
			List<KeyValuePair<int, byte[]>> list = new List<KeyValuePair<int, byte[]>>();
			try
			{
				NetworkInterface[] allNetworkInterfaces = NetworkInterface.GetAllNetworkInterfaces();
				foreach (NetworkInterface networkInterface in allNetworkInterfaces)
				{
					if (networkInterface.OperationalStatus != OperationalStatus.Up || networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
					{
						continue;
					}
					int key = NicQuality(networkInterface);
					foreach (UnicastIPAddressInformation unicastAddress in networkInterface.GetIPProperties().UnicastAddresses)
					{
						if (unicastAddress.Address.AddressFamily == AddressFamily.InterNetwork)
						{
							list.Add(new KeyValuePair<int, byte[]>(key, unicastAddress.Address.GetAddressBytes()));
						}
					}
				}
			}
			catch
			{
			}
			return (from kv in list
				orderby kv.Key descending
				select kv.Value).ToArray();
		}

		public IPAddress[] RankSourceIps(IEnumerable<IPAddress> candidates)
		{
			List<IPAddress> list = new List<IPAddress>();
			HashSet<string> hashSet = new HashSet<string>();
			foreach (IPAddress candidate in candidates)
			{
				if (candidate != null)
				{
					string item = candidate.ToString();
					if (!_localIps.Contains(item) && hashSet.Add(item))
					{
						list.Add(candidate);
					}
				}
			}
			return list.OrderByDescending((IPAddress ip) => ScorePath(ip)).ToArray();
		}

		private static int ScorePath(IPAddress remote)
		{
			int num = 30;
			byte[] addressBytes = remote.GetAddressBytes();
			try
			{
				NetworkInterface[] allNetworkInterfaces = NetworkInterface.GetAllNetworkInterfaces();
				foreach (NetworkInterface networkInterface in allNetworkInterfaces)
				{
					if (networkInterface.OperationalStatus != OperationalStatus.Up || networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
					{
						continue;
					}
					foreach (UnicastIPAddressInformation unicastAddress in networkInterface.GetIPProperties().UnicastAddresses)
					{
						if (unicastAddress.Address.AddressFamily != AddressFamily.InterNetwork)
						{
							continue;
						}
						byte[] addressBytes2 = unicastAddress.Address.GetAddressBytes();
						byte[] addressBytes3;
						try
						{
							addressBytes3 = unicastAddress.IPv4Mask.GetAddressBytes();
						}
						catch
						{
							continue;
						}
						if (addressBytes3 == null || addressBytes3.Length != 4)
						{
							continue;
						}
						bool flag = true;
						for (int j = 0; j < 4; j++)
						{
							if ((addressBytes2[j] & addressBytes3[j]) != (addressBytes[j] & addressBytes3[j]))
							{
								flag = false;
								break;
							}
						}
						if (flag)
						{
							int num2 = NicQuality(networkInterface);
							if (num2 > num)
							{
								num = num2;
							}
						}
					}
				}
			}
			catch
			{
			}
			return num;
		}

		private void BeginUdpReceive()
		{
			if (!_running)
			{
				return;
			}
			try
			{
				_udp.BeginReceive(OnUdp, null);
			}
			catch
			{
			}
		}

		private void OnUdp(IAsyncResult ar)
		{
			try
			{
				IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);
				byte[] bytes = _udp.EndReceive(ar, ref remoteEP);
				string text = Encoding.UTF8.GetString(bytes);
				string text2 = null;
				bool flag = false;
				if (text.StartsWith("CYBERPASTE|"))
				{
					text2 = text.Substring("CYBERPASTE|".Length);
					flag = true;
				}
				else if (text.StartsWith("CYBERREPLY|"))
				{
					text2 = text.Substring("CYBERREPLY|".Length);
				}
				if (text2 == null || _localIps.Contains(remoteEP.Address.ToString()))
				{
					return;
				}
				_peers[remoteEP.Address.ToString()] = new Peer
				{
					Ip = remoteEP.Address,
					Name = text2,
					LastSeen = DateTime.UtcNow
				};
				if (flag)
				{
					try
					{
						byte[] bytes2 = Encoding.UTF8.GetBytes("CYBERREPLY|" + _myName);
						_udp.Send(bytes2, bytes2.Length, remoteEP);
						return;
					}
					catch
					{
						return;
					}
				}
			}
			catch
			{
			}
			finally
			{
				BeginUdpReceive();
			}
		}

		private void Prune()
		{
			List<string> list = (from kv in _peers
				where (DateTime.UtcNow - kv.Value.LastSeen).TotalSeconds > 15.0
				select kv.Key).ToList();
			foreach (string item in list)
			{
				Peer value;
				_peers.TryRemove(item, out value);
			}
		}

		private void BeginTcpAccept()
		{
			if (!_running)
			{
				return;
			}
			try
			{
				_tcp.BeginAcceptTcpClient(OnTcpAccept, null);
			}
			catch
			{
			}
		}

		private void OnTcpAccept(IAsyncResult ar)
		{
			TcpClient client = null;
			try
			{
				client = _tcp.EndAcceptTcpClient(ar);
			}
			catch
			{
			}
			BeginTcpAccept();
			if (client == null)
			{
				return;
			}
			ThreadPool.QueueUserWorkItem(delegate
			{
				try
				{
					HandleConnection(client);
				}
				catch (Exception ex)
				{
					Log("連線處理失敗: " + ex.Message);
				}
				finally
				{
					try
					{
						client.Close();
					}
					catch
					{
					}
				}
			});
		}

		private void HandleConnection(TcpClient client)
		{
			client.NoDelay = true;
			client.SendBufferSize = 1048576;
			client.ReceiveBufferSize = 1048576;
			NetworkStream stream = client.GetStream();
			while (_running)
			{
				int num;
				try
				{
					stream.ReadTimeout = 45000;
					num = stream.ReadByte();
				}
				catch
				{
					break;
				}
				if (num < 0)
				{
					break;
				}
				stream.ReadTimeout = -1;
				switch ((byte)num)
				{
				default:
					return;
				case 1:
				{
					byte[] bytes2 = ReadBlob(stream);
					if (OnText != null)
					{
						OnText(Encoding.UTF8.GetString(bytes2));
					}
					break;
				}
				case 2:
				{
					byte[] obj2 = ReadBlob(stream);
					if (OnImagePng != null)
					{
						OnImagePng(obj2);
					}
					break;
				}
				case 3:
				{
					Guid arg = new Guid(ReadExactly(stream, 16));
					int num2 = ReadInt32(stream);
					List<IPAddress> list = new List<IPAddress>(num2 + 1);
					for (int i = 0; i < num2; i++)
					{
						list.Add(new IPAddress(ReadBlob(stream)));
					}
					IPAddress address = ((IPEndPoint)client.Client.RemoteEndPoint).Address;
					if (!list.Contains(address))
					{
						list.Add(address);
					}
					int num3 = ReadInt32(stream);
					List<FileMeta> list2 = new List<FileMeta>(num3);
					for (int j = 0; j < num3; j++)
					{
						byte[] bytes = ReadBlob(stream);
						long size = ReadInt64(stream);
						list2.Add(new FileMeta
						{
							Name = Encoding.UTF8.GetString(bytes),
							Size = size
						});
					}
					IPAddress[] array = RankSourceIps(list);
					if (array.Length > 0)
					{
						Log("收到 announce,來源候選路徑優先序: " + string.Join(", ", Array.ConvertAll(array, (IPAddress a) => a.ToString())));
					}
					if (OnFilesAnnounce != null)
					{
						OnFilesAnnounce(array, arg, list2);
					}
					break;
				}
				case 4:
				{
					Guid session = new Guid(ReadExactly(stream, 16));
					int index = ReadInt32(stream);
					long offset = ReadInt64(stream);
					ServeFile(stream, session, index, offset);
					break;
				}
				}
			}
		}

		private void ServeFile(NetworkStream ns, Guid session, int index, long offset)
		{
			Served[] value;
			if (!_served.TryGetValue(session, out value) || index < 0 || index >= value.Length)
			{
				WriteInt64(ns, -1L);
				return;
			}
			Served served = value[index];
			if (offset < 0)
			{
				offset = 0L;
			}
			if (offset > served.Size)
			{
				offset = served.Size;
			}
			FileStream fileStream;
			try
			{
				fileStream = new FileStream(served.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1048576);
			}
			catch (Exception ex)
			{
				Log("送檔失敗 " + served.Path + ": " + ex.Message);
				WriteInt64(ns, -1L);
				return;
			}
			using (fileStream)
			{
				long num = served.Size - offset;
				try
				{
					if (offset > 0)
					{
						fileStream.Seek(offset, SeekOrigin.Begin);
					}
				}
				catch
				{
				}
				Log("送檔 #" + index + " " + Path.GetFileName(served.Path) + " (" + num + " bytes" + ((offset > 0) ? (", 續傳自 " + offset) : "") + ")");
				WriteInt64(ns, num);
				byte[] array = new byte[1048576];
				long num2;
				int num3;
				for (num2 = 0L; num2 < num; num2 += num3)
				{
					int count = (int)Math.Min(array.Length, num - num2);
					num3 = fileStream.Read(array, 0, count);
					if (num3 <= 0)
					{
						break;
					}
					ns.Write(array, 0, num3);
				}
				if (num2 < num)
				{
					Array.Clear(array, 0, array.Length);
					int num4;
					for (; num2 < num; num2 += num4)
					{
						num4 = (int)Math.Min(array.Length, num - num2);
						ns.Write(array, 0, num4);
					}
				}
				ns.Flush();
			}
		}

		public void BroadcastText(string text)
		{
			byte[] payload = Encoding.UTF8.GetBytes(text);
			Peer[] array = _peers.Values.ToArray();
			foreach (Peer peer in array)
			{
				SafeSend(peer.Ip, delegate(NetworkStream ns)
				{
					ns.WriteByte(1);
					WriteBlob(ns, payload);
				});
			}
		}

		public void BroadcastImage(byte[] png)
		{
			Peer[] array = _peers.Values.ToArray();
			foreach (Peer peer in array)
			{
				SafeSend(peer.Ip, delegate(NetworkStream ns)
				{
					ns.WriteByte(2);
					WriteBlob(ns, png);
				});
			}
		}

		public void AnnounceFiles(Guid session, List<FileMeta> metas, string[] absPaths)
		{
			Served[] array = new Served[absPaths.Length];
			for (int i = 0; i < absPaths.Length; i++)
			{
				array[i] = new Served
				{
					Path = absPaths[i],
					Size = metas[i].Size
				};
			}
			_served[session] = array;
			lock (_servedOrderLock)
			{
				_servedOrder.Enqueue(session);
				while (_servedOrder.Count > 8)
				{
					Guid key = _servedOrder.Dequeue();
					Served[] value;
					_served.TryRemove(key, out value);
				}
			}
			byte[][] myIps = LocalIPv4List();
			Peer[] array2 = _peers.Values.ToArray();
			foreach (Peer peer in array2)
			{
				SafeSend(peer.Ip, delegate(NetworkStream ns)
				{
					ns.WriteByte(3);
					ns.Write(session.ToByteArray(), 0, 16);
					WriteInt32(ns, myIps.Length);
					byte[][] array3 = myIps;
					foreach (byte[] data in array3)
					{
						WriteBlob(ns, data);
					}
					WriteInt32(ns, metas.Count);
					foreach (FileMeta meta in metas)
					{
						WriteBlob(ns, Encoding.UTF8.GetBytes(meta.Name));
						WriteInt64(ns, meta.Size);
					}
				});
			}
		}

		private PooledConn RentConn(IPAddress ip)
		{
			ConcurrentStack<PooledConn> orAdd = _pool.GetOrAdd(ip.ToString(), (string _) => new ConcurrentStack<PooledConn>());
			PooledConn result;
			while (orAdd.TryPop(out result))
			{
				try
				{
					Socket client = result.Client.Client;
					bool flag = client.Poll(0, SelectMode.SelectRead) && result.Client.Available == 0;
					if (result.Client.Connected && !flag)
					{
						return result;
					}
				}
				catch
				{
				}
				try
				{
					result.Client.Close();
				}
				catch
				{
				}
			}
			TcpClient tcpClient = ConnectWithTimeout(ip, 45889, 3000);
			PooledConn pooledConn = new PooledConn();
			pooledConn.Client = tcpClient;
			pooledConn.Ns = tcpClient.GetStream();
			pooledConn.Ip = ip;
			return pooledConn;
		}

		private static TcpClient ConnectWithTimeout(IPAddress ip, int port, int ms)
		{
			TcpClient tcpClient = new TcpClient();
			tcpClient.SendBufferSize = 1048576;
			tcpClient.ReceiveBufferSize = 1048576;
			IAsyncResult asyncResult = tcpClient.BeginConnect(ip, port, null, null);
			if (!asyncResult.AsyncWaitHandle.WaitOne(ms))
			{
				try
				{
					tcpClient.Close();
				}
				catch
				{
				}
				throw new SocketException(10060);
			}
			tcpClient.EndConnect(asyncResult);
			tcpClient.NoDelay = true;
			return tcpClient;
		}

		private void ReturnConn(PooledConn c, bool reusable)
		{
			if (c == null)
			{
				return;
			}
			if (!reusable || !_running)
			{
				try
				{
					c.Client.Close();
					return;
				}
				catch
				{
					return;
				}
			}
			ConcurrentStack<PooledConn> orAdd = _pool.GetOrAdd(c.Ip.ToString(), (string _) => new ConcurrentStack<PooledConn>());
			if (orAdd.Count >= 8)
			{
				try
				{
					c.Client.Close();
					return;
				}
				catch
				{
					return;
				}
			}
			orAdd.Push(c);
		}

		private void ClosePool()
		{
			foreach (ConcurrentStack<PooledConn> value in _pool.Values)
			{
				PooledConn result;
				while (value.TryPop(out result))
				{
					try
					{
						result.Client.Close();
					}
					catch
					{
					}
				}
			}
		}

		public Stream OpenPull(IPAddress[] sources, Guid session, int index)
		{
			return new NetworkFileReadStream(this, sources, session, index);
		}

		private void SafeSend(IPAddress ip, Action<NetworkStream> write)
		{
			try
			{
				using (TcpClient tcpClient = new TcpClient())
				{
					tcpClient.SendBufferSize = 1048576;
					tcpClient.ReceiveBufferSize = 1048576;
					tcpClient.Connect(ip, 45889);
					tcpClient.NoDelay = true;
					NetworkStream stream = tcpClient.GetStream();
					write(stream);
					stream.Flush();
				}
			}
			catch (Exception ex)
			{
				Log(string.Concat("送 ", ip, " 失敗: ", ex.Message));
			}
		}

		private static void WriteBlob(Stream s, byte[] data)
		{
			WriteInt32(s, data.Length);
			s.Write(data, 0, data.Length);
		}

		private static byte[] ReadBlob(Stream s)
		{
			int count = ReadInt32(s);
			return ReadExactly(s, count);
		}

		private static void WriteInt32(Stream s, int v)
		{
			s.WriteByte((byte)(v >> 24));
			s.WriteByte((byte)(v >> 16));
			s.WriteByte((byte)(v >> 8));
			s.WriteByte((byte)v);
		}

		private static void WriteInt64(Stream s, long v)
		{
			for (int num = 7; num >= 0; num--)
			{
				s.WriteByte((byte)(v >> num * 8));
			}
		}

		private static int ReadInt32(Stream s)
		{
			byte[] array = ReadExactly(s, 4);
			return (array[0] << 24) | (array[1] << 16) | (array[2] << 8) | array[3];
		}

		private static long ReadInt64(Stream s)
		{
			byte[] array = ReadExactly(s, 8);
			long num = 0L;
			for (int i = 0; i < 8; i++)
			{
				num = (num << 8) | array[i];
			}
			return num;
		}

		private static byte[] ReadExactly(Stream s, int count)
		{
			byte[] array = new byte[count];
			int num;
			for (int i = 0; i < count; i += num)
			{
				num = s.Read(array, i, count - i);
				if (num <= 0)
				{
					throw new EndOfStreamException();
				}
			}
			return array;
		}

		private void Log(string msg)
		{
			Logger.Log("[NET] " + msg);
			if (OnLog != null)
			{
				OnLog(msg);
			}
		}

		public void Dispose()
		{
			_running = false;
			try
			{
				if (_beaconTimer != null)
				{
					_beaconTimer.Dispose();
				}
			}
			catch
			{
			}
			try
			{
				if (_pruneTimer != null)
				{
					_pruneTimer.Dispose();
				}
			}
			catch
			{
			}
			try
			{
				if (_heartbeatTimer != null)
				{
					_heartbeatTimer.Dispose();
				}
			}
			catch
			{
			}
			try
			{
				ClosePool();
			}
			catch
			{
			}
			try
			{
				if (_udp != null)
				{
					_udp.Close();
				}
			}
			catch
			{
			}
			try
			{
				if (_tcp != null)
				{
					_tcp.Stop();
				}
			}
			catch
			{
			}
		}
	}
}
