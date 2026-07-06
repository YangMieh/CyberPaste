using System;
using System.IO;

namespace CyberPaste
{
	public sealed class VirtualFile
	{
		public string Name;

		public long Length;

		public Func<Stream> OpenRead;
	}
}
