using System.Runtime.InteropServices;

namespace DlpForge.App.Services;

/// <summary>Shows overall download progress on the app's taskbar button (ITaskbarList3).</summary>
public sealed class TaskbarProgress
{
    private readonly IntPtr _hwnd;
    private readonly ITaskbarList3? _taskbar;

    public TaskbarProgress(IntPtr hwnd)
    {
        _hwnd = hwnd;
        try
        {
            _taskbar = (ITaskbarList3)new TaskbarInstance();
            _taskbar.HrInit();
        }
        catch (COMException)
        {
            _taskbar = null;
        }
    }

    public void Update(int activeCount, double percent)
    {
        if (_taskbar is null || _hwnd == IntPtr.Zero) return;
        try
        {
            if (activeCount == 0)
            {
                _taskbar.SetProgressState(_hwnd, TBPFLAG.TBPF_NOPROGRESS);
            }
            else
            {
                _taskbar.SetProgressState(_hwnd, TBPFLAG.TBPF_NORMAL);
                _taskbar.SetProgressValue(_hwnd, (ulong)Math.Clamp(percent * 10, 0, 1000), 1000);
            }
        }
        catch (COMException)
        {
            // Explorer restarting; the next update will try again.
        }
    }

    private enum TBPFLAG
    {
        TBPF_NOPROGRESS = 0,
        TBPF_INDETERMINATE = 0x1,
        TBPF_NORMAL = 0x2,
        TBPF_ERROR = 0x4,
        TBPF_PAUSED = 0x8,
    }

    [ComImport]
    [Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList3
    {
        // ITaskbarList
        void HrInit();
        void AddTab(IntPtr hwnd);
        void DeleteTab(IntPtr hwnd);
        void ActivateTab(IntPtr hwnd);
        void SetActiveAlt(IntPtr hwnd);
        // ITaskbarList2
        void MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fFullscreen);
        // ITaskbarList3
        void SetProgressValue(IntPtr hwnd, ulong ullCompleted, ulong ullTotal);
        void SetProgressState(IntPtr hwnd, TBPFLAG tbpFlags);
    }

    [ComImport]
    [Guid("56FDF344-FD6D-11d0-958A-006097C9A090")]
    [ClassInterface(ClassInterfaceType.None)]
    private class TaskbarInstance;
}
