using System.Collections.Generic;
using System.Runtime.InteropServices.ComTypes;

namespace CyberPaste
{
	internal sealed class FormatEnumerator : IEnumFORMATETC
	{
		private readonly List<FORMATETC> _items;

		private int _pos;

		public FormatEnumerator(List<FORMATETC> items)
		{
			_items = items;
		}

		private FormatEnumerator(List<FORMATETC> items, int pos)
		{
			_items = items;
			_pos = pos;
		}

		public int Next(int celt, FORMATETC[] rgelt, int[] pceltFetched)
		{
			int num = 0;
			while (_pos < _items.Count && num < celt)
			{
				rgelt[num] = _items[_pos];
				_pos++;
				num++;
			}
			if (pceltFetched != null && pceltFetched.Length > 0)
			{
				pceltFetched[0] = num;
			}
			if (num != celt)
			{
				return 1;
			}
			return 0;
		}

		public int Skip(int celt)
		{
			_pos += celt;
			if (_pos > _items.Count)
			{
				return 1;
			}
			return 0;
		}

		public int Reset()
		{
			_pos = 0;
			return 0;
		}

		public void Clone(out IEnumFORMATETC newEnum)
		{
			newEnum = new FormatEnumerator(_items, _pos);
		}
	}
}
