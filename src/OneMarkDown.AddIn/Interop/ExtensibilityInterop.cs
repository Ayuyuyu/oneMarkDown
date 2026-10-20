using System;
using System.Runtime.InteropServices;

namespace OneMarkDown.Interop
{
    /// <summary>
    /// Office 共享加载项接口。声明为 IDispatch，由 Office 通过 DISPID 调用。
    /// 手写声明可避免对 Office PIA（Extensibility.dll）的依赖。
    /// </summary>
    /// <remarks>
    /// 用 Dual 而不用 IDispatch：宿主既可能按 DISPID 调用，也可能按 vtable 调用，
    /// Dual 两条路都满足；方法顺序必须与官方接口一致（OnConnection 到 OnBeginShutdown）。
    /// </remarks>
    [ComImport]
    [Guid("B65AD801-ABAF-11D0-BB8B-00A0C90F2744")]
    [InterfaceType(ComInterfaceType.InterfaceIsDual)]
    public interface IDTExtensibility2
    {
        [DispId(1)]
        void OnConnection(
            [MarshalAs(UnmanagedType.IDispatch)] object application,
            ext_ConnectMode connectMode,
            [MarshalAs(UnmanagedType.IDispatch)] object addInInstance,
            // 官方类型库里是 SAFEARRAY(VARIANT)**，这里显式钉死元素类型，
            // 避免默认封送推断出别的 SAFEARRAY 变体导致调用失败。
            [MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_VARIANT)] ref Array custom);

        [DispId(2)]
        void OnDisconnection(ext_DisconnectMode removeMode, ref Array custom);

        [DispId(3)]
        void OnAddInsUpdate(ref Array custom);

        [DispId(4)]
        void OnStartupComplete(ref Array custom);

        [DispId(5)]
        void OnBeginShutdown(ref Array custom);
    }

    public enum ext_ConnectMode
    {
        ext_cm_AfterStartup = 0,
        ext_cm_Startup = 1,
        ext_cm_External = 2,
        ext_cm_CommandLine = 3,
        ext_cm_Solution = 4,
        ext_cm_UISetup = 5
    }

    public enum ext_DisconnectMode
    {
        ext_dm_HostShutdown = 0,
        ext_dm_UserClosed = 1
    }
}
