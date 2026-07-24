using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Wallpaper.Infrastructure.Windows.Shell;

[SupportedOSPlatform("windows")]
internal sealed class ShellCommandHostLifetime : IDisposable
{
    private const uint ReferencePollMilliseconds = 50;
    private const uint MwmoInputAvailable = 0x0004;
    private const uint PmRemove = 0x0001;
    private const uint QsAllInput = 0x04ff;
    private const uint WaitFailed = 0xffffffff;
    private const uint WmQuit = 0x0012;

    private nint _referenceCount;
    private nint _reference;
    private bool _processReferencePublished;
    private bool _threadReferencePublished;
    private bool _completed;

    private ShellCommandHostLifetime()
    {
        _referenceCount = Marshal.AllocHGlobal(sizeof(int));
        Marshal.WriteInt32(_referenceCount, 0);

        try
        {
            ThrowForHResult(SHCreateThreadRef(_referenceCount, out _reference));
            ThrowForHResult(SHSetThreadRef(_reference));
            _threadReferencePublished = true;
            SetProcessReference(_reference);
            _processReferencePublished = true;
        }
        catch
        {
            ClearPublishedReferences();
            ReleaseOwnerReference();
            FreeReferenceCountIfReleased();
            throw;
        }
    }

    internal int ReferenceCount =>
        _referenceCount == 0 ? 0 : Marshal.ReadInt32(_referenceCount);

    public static ShellCommandHostLifetime? Create(
        ShellContextMenuShowOptions options)
    {
        return (options & ShellContextMenuShowOptions.RequestSynchronousCommand) == 0
            ? null
            : new ShellCommandHostLifetime();
    }

    public void CompleteAndWait()
    {
        if (_completed)
        {
            return;
        }

        ClearPublishedReferences();
        ReleaseOwnerReference();
        PumpMessagesUntilReleased();
        FreeReferenceCountIfReleased();
        _completed = true;
    }

    public void Dispose()
    {
        if (_completed)
        {
            return;
        }

        CompleteAndWait();
    }

    private void ClearPublishedReferences()
    {
        if (_processReferencePublished)
        {
            SetProcessReference(0);
            _processReferencePublished = false;
        }

        if (_threadReferencePublished)
        {
            ThrowForHResult(SHSetThreadRef(0));
            _threadReferencePublished = false;
        }
    }

    private void ReleaseOwnerReference()
    {
        if (_reference == 0)
        {
            return;
        }

        _ = Marshal.Release(_reference);
        _reference = 0;
    }

    private void PumpMessagesUntilReleased()
    {
        var repostQuit = false;
        var quitCode = 0;

        try
        {
            while (ReferenceCount > 0)
            {
                var waitResult = MsgWaitForMultipleObjectsEx(
                    0,
                    0,
                    ReferencePollMilliseconds,
                    QsAllInput,
                    MwmoInputAvailable);
                if (waitResult == WaitFailed)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                while (PeekMessage(out var message, 0, 0, 0, PmRemove))
                {
                    if (message.Message == WmQuit)
                    {
                        repostQuit = true;
                        quitCode = unchecked((int)message.WParam);
                        continue;
                    }

                    _ = TranslateMessage(in message);
                    _ = DispatchMessage(in message);
                }
            }
        }
        finally
        {
            if (repostQuit)
            {
                PostQuitMessage(quitCode);
            }
        }
    }

    private void FreeReferenceCountIfReleased()
    {
        if (_referenceCount == 0 || Marshal.ReadInt32(_referenceCount) != 0)
        {
            return;
        }

        Marshal.FreeHGlobal(_referenceCount);
        _referenceCount = 0;
    }

    private static void ThrowForHResult(int result)
    {
        if (result < 0)
        {
            Marshal.ThrowExceptionForHR(result);
        }
    }

    [DllImport("shlwapi.dll", PreserveSig = true)]
    private static extern int SHCreateThreadRef(
        nint referenceCount,
        out nint reference);

    [DllImport("shlwapi.dll", PreserveSig = true)]
    private static extern int SHSetThreadRef(nint reference);

    [DllImport("api-ms-win-shcore-thread-l1-1-0.dll")]
    private static extern void SetProcessReference(nint reference);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint MsgWaitForMultipleObjectsEx(
        uint count,
        nint handles,
        uint milliseconds,
        uint wakeMask,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessage(
        out NativeMessage message,
        nint window,
        uint minimumMessage,
        uint maximumMessage,
        uint removeMessage);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(in NativeMessage message);

    [DllImport("user32.dll", EntryPoint = "DispatchMessageW")]
    private static extern nint DispatchMessage(in NativeMessage message);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int exitCode);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativeMessage
    {
        public readonly nint Window;
        public readonly uint Message;
        public readonly nuint WParam;
        public readonly nint LParam;
        public readonly uint Time;
        public readonly NativePoint Point;
        public readonly uint Private;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct NativePoint(int X, int Y);
}
