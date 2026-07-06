using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace CyberPaste
{
	public sealed class VirtualFileDataObject : IDataObject, IDataObjectAsyncCapability
	{
		private static readonly short CF_FILEDESCRIPTORW = (short)NativeMethods.RegisterClipboardFormat("FileGroupDescriptorW");

		private static readonly short CF_FILECONTENTS = (short)NativeMethods.RegisterClipboardFormat("FileContents");

		private readonly List<VirtualFile> _files;

		private bool _inAsyncOp;

		public VirtualFileDataObject(List<VirtualFile> files)
		{
			_files = files;
		}

		public void GetData(ref FORMATETC format, out STGMEDIUM medium)
		{
			medium = default(STGMEDIUM);
			if (format.cfFormat == CF_FILEDESCRIPTORW && (format.tymed & TYMED.TYMED_HGLOBAL) == TYMED.TYMED_HGLOBAL)
			{
				medium.tymed = TYMED.TYMED_HGLOBAL;
				medium.unionmember = BuildFileGroupDescriptor();
				medium.pUnkForRelease = null;
			}
			else if (format.cfFormat == CF_FILECONTENTS && (format.tymed & TYMED.TYMED_ISTREAM) == TYMED.TYMED_ISTREAM)
			{
				int lindex = format.lindex;
				if (lindex < 0 || lindex >= _files.Count)
				{
					Marshal.ThrowExceptionForHR(-2147221404);
				}
				PullStream o = new PullStream(_files[lindex]);
				medium.tymed = TYMED.TYMED_ISTREAM;
				medium.unionmember = Marshal.GetComInterfaceForObject(o, typeof(IStream));
				medium.pUnkForRelease = null;
			}
			else
			{
				Marshal.ThrowExceptionForHR(-2147221404);
			}
		}

		public void GetDataHere(ref FORMATETC format, ref STGMEDIUM medium)
		{
			Marshal.ThrowExceptionForHR(-2147221404);
		}

		public int QueryGetData(ref FORMATETC format)
		{
			if (format.cfFormat == CF_FILEDESCRIPTORW && (format.tymed & TYMED.TYMED_HGLOBAL) == TYMED.TYMED_HGLOBAL)
			{
				return 0;
			}
			if (format.cfFormat == CF_FILECONTENTS && (format.tymed & TYMED.TYMED_ISTREAM) == TYMED.TYMED_ISTREAM)
			{
				return 0;
			}
			return -2147221404;
		}

		public int GetCanonicalFormatEtc(ref FORMATETC formatIn, out FORMATETC formatOut)
		{
			formatOut = formatIn;
			formatOut.ptd = IntPtr.Zero;
			return -2147221404;
		}

		public void SetData(ref FORMATETC formatIn, ref STGMEDIUM medium, bool release)
		{
			Marshal.ThrowExceptionForHR(-2147467263);
		}

		public IEnumFORMATETC EnumFormatEtc(DATADIR direction)
		{
			if (direction != DATADIR.DATADIR_GET)
			{
				throw new NotImplementedException();
			}
			List<FORMATETC> list = new List<FORMATETC>();
			list.Add(new FORMATETC
			{
				cfFormat = CF_FILEDESCRIPTORW,
				ptd = IntPtr.Zero,
				dwAspect = DVASPECT.DVASPECT_CONTENT,
				lindex = -1,
				tymed = TYMED.TYMED_HGLOBAL
			});
			List<FORMATETC> list2 = list;
			for (int i = 0; i < _files.Count; i++)
			{
				list2.Add(new FORMATETC
				{
					cfFormat = CF_FILECONTENTS,
					ptd = IntPtr.Zero,
					dwAspect = DVASPECT.DVASPECT_CONTENT,
					lindex = i,
					tymed = TYMED.TYMED_ISTREAM
				});
			}
			return new FormatEnumerator(list2);
		}

		public int DAdvise(ref FORMATETC pFormatetc, ADVF advf, IAdviseSink adviseSink, out int connection)
		{
			connection = 0;
			return -2147221501;
		}

		public void DUnadvise(int connection)
		{
			Marshal.ThrowExceptionForHR(-2147221501);
		}

		public int EnumDAdvise(out IEnumSTATDATA enumAdvise)
		{
			enumAdvise = null;
			return -2147221501;
		}

		public void SetAsyncMode(bool fDoOpAsync)
		{
		}

		public void GetAsyncMode(out bool pfIsOpAsync)
		{
			pfIsOpAsync = true;
		}

		public void StartOperation(IBindCtx pbcReserved)
		{
			_inAsyncOp = true;
		}

		public void InOperation(out bool pfInAsyncOp)
		{
			pfInAsyncOp = _inAsyncOp;
		}

		public void EndOperation(int hResult, IBindCtx pbcReserved, uint dwEffects)
		{
			_inAsyncOp = false;
		}

		private IntPtr BuildFileGroupDescriptor()
		{
			int num = Marshal.SizeOf(typeof(NativeMethods.FILEDESCRIPTORW));
			int num2 = 4 + num * _files.Count;
			IntPtr intPtr = NativeMethods.GlobalAlloc(66u, (UIntPtr)(ulong)num2);
			if (intPtr == IntPtr.Zero)
			{
				Marshal.ThrowExceptionForHR(-2147467259);
			}
			IntPtr intPtr2 = NativeMethods.GlobalLock(intPtr);
			try
			{
				Marshal.WriteInt32(intPtr2, _files.Count);
				IntPtr ptr = intPtr2 + 4;
				foreach (VirtualFile file in _files)
				{
					NativeMethods.FILEDESCRIPTORW structure = new NativeMethods.FILEDESCRIPTORW
					{
						dwFlags = 16452u,
						dwFileAttributes = 128u,
						nFileSizeHigh = (uint)((file.Length >> 32) & 0xFFFFFFFFu),
						nFileSizeLow = (uint)(file.Length & 0xFFFFFFFFu),
						cFileName = file.Name
					};
					Marshal.StructureToPtr(structure, ptr, false);
					ptr += num;
				}
			}
			finally
			{
				NativeMethods.GlobalUnlock(intPtr);
			}
			return intPtr;
		}
	}
}
