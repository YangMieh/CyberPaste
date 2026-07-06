using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace CyberPaste
{
	[ComImport]
	[Guid("3D8B0590-F691-11d2-8EA9-006097DF5BD4")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	public interface IDataObjectAsyncCapability
	{
		void SetAsyncMode([MarshalAs(UnmanagedType.Bool)] bool fDoOpAsync);

		void GetAsyncMode([MarshalAs(UnmanagedType.Bool)] out bool pfIsOpAsync);

		void StartOperation(IBindCtx pbcReserved);

		void InOperation([MarshalAs(UnmanagedType.Bool)] out bool pfInAsyncOp);

		void EndOperation(int hResult, IBindCtx pbcReserved, uint dwEffects);
	}
}
