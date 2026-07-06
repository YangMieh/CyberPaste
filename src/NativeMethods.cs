using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace CyberPaste
{
	internal static class NativeMethods
	{
		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 4)]
		public struct FILEDESCRIPTORW
		{
			public uint dwFlags;

			public Guid clsid;

			public int sizelCx;

			public int sizelCy;

			public int pointlX;

			public int pointlY;

			public uint dwFileAttributes;

			public long ftCreationTime;

			public long ftLastAccessTime;

			public long ftLastWriteTime;

			public uint nFileSizeHigh;

			public uint nFileSizeLow;

			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
			public string cFileName;
		}

		public const string CFSTR_FILEDESCRIPTORW = "FileGroupDescriptorW";

		public const string CFSTR_FILECONTENTS = "FileContents";

		public const uint GMEM_MOVEABLE = 2u;

		public const uint GMEM_ZEROINIT = 64u;

		public const int S_OK = 0;

		public const int S_FALSE = 1;

		public const int E_FAIL = -2147467259;

		public const int E_NOTIMPL = -2147467263;

		public const int DV_E_FORMATETC = -2147221404;

		public const int DV_E_TYMED = -2147221399;

		public const int OLE_E_ADVISENOTSUPPORTED = -2147221501;

		public const int STG_E_MEDIUMFULL = -2147286928;

		public const uint FD_ATTRIBUTES = 4u;

		public const uint FD_FILESIZE = 64u;

		public const uint FD_PROGRESSUI = 16384u;

		public const uint FILE_ATTRIBUTE_NORMAL = 128u;

		public const int STGTY_STREAM = 2;

		public const int STGM_READ = 0;

		public const int STREAM_SEEK_SET = 0;

		public const int STREAM_SEEK_CUR = 1;

		public const int STREAM_SEEK_END = 2;

		[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
		public static extern uint RegisterClipboardFormat(string lpszFormat);

		[DllImport("ole32.dll")]
		public static extern int OleSetClipboard(IDataObject pDataObj);

		[DllImport("ole32.dll")]
		public static extern int OleFlushClipboard();

		[DllImport("ole32.dll")]
		public static extern int OleGetClipboard(out IDataObject ppDataObj);

		[DllImport("kernel32.dll")]
		public static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

		[DllImport("kernel32.dll")]
		public static extern IntPtr GlobalLock(IntPtr hMem);

		[DllImport("kernel32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool GlobalUnlock(IntPtr hMem);
	}
}
