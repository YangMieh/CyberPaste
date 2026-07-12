using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace CyberPaste
{
	internal sealed class PullStream : IStream
	{
		private readonly VirtualFile _file;

		private Stream _inner;

		private long _position;

		private bool _eof;

		public PullStream(VirtualFile file)
		{
			_file = file;
		}

		private void EnsureOpen()
		{
			if (_inner == null)
			{
				_inner = _file.OpenRead();
			}
		}

		public void Read(byte[] pv, int cb, IntPtr pcbRead)
		{
			EnsureOpen();
			int num = 0;
			while (num < cb && !_eof)
			{
				int num2 = _inner.Read(pv, num, cb - num);
				if (num2 <= 0)
				{
					_eof = true;
					break;
				}
				num += num2;
				_position += num2;
			}
			if (pcbRead != IntPtr.Zero)
			{
				Marshal.WriteInt32(pcbRead, num);
			}
		}

		public void Stat(out System.Runtime.InteropServices.ComTypes.STATSTG pstatstg, int grfStatFlag)
		{
			pstatstg = new System.Runtime.InteropServices.ComTypes.STATSTG
			{
				type = 2,
				cbSize = _file.Length,
				grfMode = 0
			};
		}

		public void Seek(long dlibMove, int dwOrigin, IntPtr plibNewPosition)
		{
			long num;
			switch (dwOrigin)
			{
			case 0:
				num = dlibMove;
				break;
			case 1:
				num = _position + dlibMove;
				break;
			case 2:
				num = _file.Length + dlibMove;
				break;
			default:
				num = _position;
				break;
			}
			if (num != _position && dwOrigin == 2 && dlibMove == 0)
			{
				if (plibNewPosition != IntPtr.Zero)
				{
					Marshal.WriteInt64(plibNewPosition, _file.Length);
				}
			}
			else if (plibNewPosition != IntPtr.Zero)
			{
				Marshal.WriteInt64(plibNewPosition, _position);
			}
		}

		public void CopyTo(IStream pstm, long cb, IntPtr pcbRead, IntPtr pcbWritten)
		{
			byte[] array = new byte[81920];
			long num = 0L;
			long num2 = 0L;
			long num3 = cb;
			while (num3 > 0)
			{
				int cb2 = (int)Math.Min(array.Length, num3);
				IntPtr intPtr = Marshal.AllocCoTaskMem(4);
				try
				{
					Read(array, cb2, intPtr);
					int num4 = Marshal.ReadInt32(intPtr);
					if (num4 == 0)
					{
						break;
					}
					num += num4;
					IntPtr intPtr2 = Marshal.AllocCoTaskMem(4);
					try
					{
						pstm.Write(array, num4, intPtr2);
						num2 += Marshal.ReadInt32(intPtr2);
					}
					finally
					{
						Marshal.FreeCoTaskMem(intPtr2);
					}
					num3 -= num4;
					continue;
				}
				finally
				{
					Marshal.FreeCoTaskMem(intPtr);
				}
			}
			if (pcbRead != IntPtr.Zero)
			{
				Marshal.WriteInt64(pcbRead, num);
			}
			if (pcbWritten != IntPtr.Zero)
			{
				Marshal.WriteInt64(pcbWritten, num2);
			}
		}

		public void Write(byte[] pv, int cb, IntPtr pcbWritten)
		{
			Marshal.ThrowExceptionForHR(-2147286928);
		}

		public void SetSize(long libNewSize)
		{
			Marshal.ThrowExceptionForHR(-2147467263);
		}

		public void Commit(int grfCommitFlags)
		{
		}

		public void Revert()
		{
		}

		public void LockRegion(long libOffset, long cb, int dwLockType)
		{
			Marshal.ThrowExceptionForHR(-2147467263);
		}

		public void UnlockRegion(long libOffset, long cb, int dwLockType)
		{
			Marshal.ThrowExceptionForHR(-2147467263);
		}

		public void Clone(out IStream ppstm)
		{
			ppstm = null;
			Marshal.ThrowExceptionForHR(-2147467263);
		}
	}
}
