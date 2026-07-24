using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Wallpaper.Core.FileOperations;

namespace Wallpaper.Infrastructure.Windows.Shell;

[SupportedOSPlatform("windows")]
public sealed class WindowsShellContextMenuService : IShellContextMenuService
{
    private const uint CoInitApartmentThreaded = 0x2;
    private const uint ClsCtxLocalServer = 0x4;
    private const uint CmfNormal = 0x00000000;
    private const uint CmicMaskNoAsync = 0x00000100;
    private const uint CmicMaskUnicode = 0x00004000;
    private const uint CmicMaskPtInvoke = 0x20000000;
    private const uint TpmRightButton = 0x0002;
    private const uint TpmReturnCommand = 0x0100;
    private const int SwShowNormal = 1;
    private const uint FirstCommandId = 1;
    private const uint LastCommandId = 0x7fff;
    private const uint SvgioBackground = 0x00000000;
    private const int SwcDesktop = 0x8;
    private const int SwfoNeedDispatch = 0x1;
    private const int WmNull = 0x0000;
    private const int WmDrawItem = 0x002b;
    private const int WmMeasureItem = 0x002c;
    private const int WmInitMenuPopup = 0x0117;
    private const int WmMenuChar = 0x0120;
    private const uint GaRoot = 2;
    private const nuint SubclassId = 0x57414c4c;

    private static readonly Guid ShellFolderInterfaceId = new("000214E6-0000-0000-C000-000000000046");
    private static readonly Guid ShellViewInterfaceId = new("000214E3-0000-0000-C000-000000000046");
    private static readonly Guid ShellBrowserInterfaceId = new("000214E2-0000-0000-C000-000000000046");
    private static readonly Guid ContextMenuInterfaceId = new("000214E4-0000-0000-C000-000000000046");
    private static readonly Guid ContextMenu2InterfaceId = new("000214F4-0000-0000-C000-000000000046");
    private static readonly Guid ContextMenu3InterfaceId = new("BCFCE0A0-EC17-11D0-8D10-00A0C90F2719");
    private static readonly Guid ShellWindowsClassId = new("9BA05972-F6A8-11CF-A442-00A0C90A8F39");
    private static readonly Guid ShellWindowsInterfaceId = new("85CB6900-4D95-11CF-960C-0080C7F4EE85");
    private static readonly Guid ServiceProviderInterfaceId = new("6D5140C1-7436-11CE-8034-00AA006009FA");
    private static readonly Guid TopLevelBrowserServiceId = new("4C96BE40-915C-11CF-99D3-00AA004AE837");

    public IShellContextMenuSession CreateItemContextMenu(
        FileCommandTarget target,
        nint ownerWindow)
    {
        ArgumentNullException.ThrowIfNull(target);
        var validated = FileCommandTargetValidator.ValidateExisting(target);
        EnsureWindowsAndOwner(ownerWindow);
        return CreateWithComInitialized(() =>
        {
            nint absolutePidl = 0;
            nint parentFolder = 0;
            nint childPidlArray = 0;
            nint contextMenu = 0;

            try
            {
                ThrowForHResult(SHParseDisplayName(
                    validated.AbsolutePath,
                    0,
                    out absolutePidl,
                    0,
                    out _));

                var shellFolderId = ShellFolderInterfaceId;
                ThrowForHResult(SHBindToParent(
                    absolutePidl,
                    ref shellFolderId,
                    out parentFolder,
                    out var childPidl));

                childPidlArray = Marshal.AllocCoTaskMem(nint.Size);
                Marshal.WriteIntPtr(childPidlArray, childPidl);

                var contextMenuId = ContextMenuInterfaceId;
                var getUiObjectOf = GetVtableDelegate<GetUiObjectOfDelegate>(parentFolder, 10);
                ThrowForHResult(getUiObjectOf(
                    parentFolder,
                    ownerWindow,
                    1,
                    childPidlArray,
                    ref contextMenuId,
                    0,
                    out contextMenu));

                var session = new ShellContextMenuSession(contextMenu, ownerWindow);
                contextMenu = 0;
                return session;
            }
            finally
            {
                ReleaseIfNotZero(contextMenu);
                if (childPidlArray != 0)
                {
                    Marshal.FreeCoTaskMem(childPidlArray);
                }

                ReleaseIfNotZero(parentFolder);
                if (absolutePidl != 0)
                {
                    CoTaskMemFree(absolutePidl);
                }
            }
        });
    }

    public IShellContextMenuSession CreateDesktopContextMenu(nint ownerWindow)
    {
        EnsureWindowsAndOwner(ownerWindow);

        return CreateWithComInitialized(() =>
        {
            nint contextMenu = 0;
            nint contextMenuOwner = 0;

            try
            {
                if (!TryCreateExplorerDesktopBackgroundContextMenu(
                        out contextMenu,
                        out contextMenuOwner))
                {
                    CreateDefaultDesktopBackgroundContextMenu(
                        ownerWindow,
                        out contextMenu,
                        out contextMenuOwner);
                }

                var session = new ShellContextMenuSession(
                    contextMenu,
                    ownerWindow,
                    contextMenuOwner);
                contextMenu = 0;
                contextMenuOwner = 0;
                return session;
            }
            finally
            {
                ReleaseIfNotZero(contextMenu);
                ReleaseIfNotZero(contextMenuOwner);
            }
        });
    }

    private static bool TryCreateExplorerDesktopBackgroundContextMenu(
        out nint contextMenu,
        out nint contextMenuOwner)
    {
        nint shellWindows = 0;
        nint desktopDispatch = 0;
        nint serviceProvider = 0;
        nint shellBrowser = 0;
        nint desktopView = 0;
        nint desktopContextMenu = 0;
        contextMenu = 0;
        contextMenuOwner = 0;

        try
        {
            var shellWindowsClassId = ShellWindowsClassId;
            var shellWindowsInterfaceId = ShellWindowsInterfaceId;
            if (CoCreateInstance(
                    ref shellWindowsClassId,
                    0,
                    ClsCtxLocalServer,
                    ref shellWindowsInterfaceId,
                    out shellWindows) < 0 ||
                shellWindows == 0)
            {
                return false;
            }

            var emptyLocation = default(NativeVariant);
            var emptyRoot = default(NativeVariant);
            var findWindow = GetVtableDelegate<FindWindowShellDelegate>(shellWindows, 15);
            if (findWindow(
                    shellWindows,
                    ref emptyLocation,
                    ref emptyRoot,
                    SwcDesktop,
                    out _,
                    SwfoNeedDispatch,
                    out desktopDispatch) < 0 ||
                desktopDispatch == 0)
            {
                return false;
            }

            TryQueryInterface(desktopDispatch, ServiceProviderInterfaceId, out serviceProvider);
            if (serviceProvider == 0)
            {
                return false;
            }

            var topLevelBrowserServiceId = TopLevelBrowserServiceId;
            var shellBrowserInterfaceId = ShellBrowserInterfaceId;
            var queryService = GetVtableDelegate<QueryServiceDelegate>(serviceProvider, 3);
            if (queryService(
                    serviceProvider,
                    ref topLevelBrowserServiceId,
                    ref shellBrowserInterfaceId,
                    out shellBrowser) < 0 ||
                shellBrowser == 0)
            {
                return false;
            }

            var queryActiveShellView = GetVtableDelegate<QueryActiveShellViewDelegate>(shellBrowser, 15);
            if (queryActiveShellView(shellBrowser, out desktopView) < 0 || desktopView == 0)
            {
                return false;
            }

            var contextMenuId = ContextMenuInterfaceId;
            var getItemObject = GetVtableDelegate<GetItemObjectDelegate>(desktopView, 15);
            if (getItemObject(
                    desktopView,
                    SvgioBackground,
                    ref contextMenuId,
                    out desktopContextMenu) < 0 ||
                desktopContextMenu == 0)
            {
                return false;
            }

            contextMenu = desktopContextMenu;
            desktopContextMenu = 0;
            contextMenuOwner = desktopView;
            desktopView = 0;
            return true;
        }
        catch (Exception exception) when (
            exception is COMException or InvalidOperationException)
        {
            return false;
        }
        finally
        {
            ReleaseIfNotZero(desktopContextMenu);
            ReleaseIfNotZero(desktopView);
            ReleaseIfNotZero(shellBrowser);
            ReleaseIfNotZero(serviceProvider);
            ReleaseIfNotZero(desktopDispatch);
            ReleaseIfNotZero(shellWindows);
        }
    }

    private static void CreateDefaultDesktopBackgroundContextMenu(
        nint ownerWindow,
        out nint contextMenu,
        out nint contextMenuOwner)
    {
        nint desktopFolder = 0;
        nint desktopView = 0;
        nint desktopContextMenu = 0;

        try
        {
            ThrowForHResult(SHGetDesktopFolder(out desktopFolder));
            var createViewObject = GetVtableDelegate<CreateViewObjectDelegate>(desktopFolder, 8);
            var shellViewId = ShellViewInterfaceId;
            ThrowForHResult(createViewObject(
                desktopFolder,
                ownerWindow,
                ref shellViewId,
                out desktopView));

            var contextMenuId = ContextMenuInterfaceId;
            var getItemObject = GetVtableDelegate<GetItemObjectDelegate>(desktopView, 15);
            ThrowForHResult(getItemObject(
                desktopView,
                SvgioBackground,
                ref contextMenuId,
                out desktopContextMenu));

            contextMenu = desktopContextMenu;
            desktopContextMenu = 0;
            contextMenuOwner = desktopView;
            desktopView = 0;
        }
        finally
        {
            ReleaseIfNotZero(desktopContextMenu);
            ReleaseIfNotZero(desktopView);
            ReleaseIfNotZero(desktopFolder);
        }
    }

    private static bool ShowNativeContextMenu(
        nint contextMenu,
        nint popupMenu,
        nint contextMenu2,
        nint contextMenu3,
        nint ownerWindow,
        int screenX,
        int screenY,
        ShellContextMenuShowOptions options)
    {
        SubclassProc? subclassProc = null;
        var subclassInstalled = false;

        try
        {
            subclassProc = CreateSubclassProc(contextMenu2, contextMenu3);
            if (!SetWindowSubclass(ownerWindow, subclassProc, SubclassId, 0))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            subclassInstalled = true;
            var foregroundWindow = GetAncestor(ownerWindow, GaRoot);
            if (foregroundWindow == 0)
            {
                foregroundWindow = ownerWindow;
            }

            if (GetForegroundWindow() != foregroundWindow &&
                !SetForegroundWindow(foregroundWindow) &&
                GetForegroundWindow() != foregroundWindow)
            {
                throw new InvalidOperationException(
                    "Windows Shell 메뉴의 foreground 창을 설정하지 못했습니다.");
            }

            SetLastError(0);
            var selectedCommand = TrackPopupMenuEx(
                popupMenu,
                TpmRightButton | TpmReturnCommand,
                screenX,
                screenY,
                ownerWindow,
                0);

            if (selectedCommand == 0)
            {
                var error = Marshal.GetLastWin32Error();
                if (error != 0)
                {
                    throw new Win32Exception(error);
                }

                return false;
            }

            InvokeContextCommand(
                contextMenu,
                ownerWindow,
                selectedCommand,
                screenX,
                screenY,
                options);
            return true;
        }
        finally
        {
            if (subclassInstalled && subclassProc is not null)
            {
                _ = RemoveWindowSubclass(ownerWindow, subclassProc, SubclassId);
            }

            _ = PostMessage(ownerWindow, WmNull, 0, 0);
        }
    }

    private static void InvokeContextCommand(
        nint contextMenu,
        nint ownerWindow,
        uint commandId,
        int screenX,
        int screenY,
        ShellContextMenuShowOptions options)
    {
        if (commandId < FirstCommandId || commandId > LastCommandId)
        {
            throw new ShellContextMenuException("선택한 Windows Shell 명령을 찾을 수 없습니다.");
        }

        var commandOffset = commandId - FirstCommandId;
        var invoke = new CommandInvokeInfo
        {
            Size = (uint)Marshal.SizeOf<CommandInvokeInfo>(),
            Mask = CreateCommandInvokeMask(options),
            OwnerWindow = ownerWindow,
            Verb = (nint)commandOffset,
            Show = SwShowNormal,
            VerbUnicode = (nint)commandOffset,
            InvokePoint = new NativePoint(screenX, screenY),
        };

        var invokeCommand = GetVtableDelegate<InvokeCommandDelegate>(contextMenu, 4);
        ThrowForHResult(invokeCommand(contextMenu, ref invoke));
    }

    internal static uint CreateCommandInvokeMask(ShellContextMenuShowOptions options)
    {
        const ShellContextMenuShowOptions supportedOptions =
            ShellContextMenuShowOptions.RequestSynchronousCommand;
        if ((options & ~supportedOptions) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }

        var mask = CmicMaskUnicode | CmicMaskPtInvoke;
        if ((options & ShellContextMenuShowOptions.RequestSynchronousCommand) != 0)
        {
            mask |= CmicMaskNoAsync;
        }

        return mask;
    }

    private static SubclassProc CreateSubclassProc(nint contextMenu2, nint contextMenu3)
    {
        HandleMenuMessage2Delegate? handleMenuMessage2 = contextMenu3 == 0
            ? null
            : GetVtableDelegate<HandleMenuMessage2Delegate>(contextMenu3, 7);
        HandleMenuMessageDelegate? handleMenuMessage = contextMenu2 == 0
            ? null
            : GetVtableDelegate<HandleMenuMessageDelegate>(contextMenu2, 6);

        return (window, message, wParam, lParam, _, _) =>
        {
            if (!IsShellMenuMessage(message))
            {
                return DefSubclassProc(window, message, wParam, lParam);
            }

            if (handleMenuMessage2 is not null)
            {
                nint result = 0;
                if (handleMenuMessage2(contextMenu3, message, wParam, lParam, out result) >= 0)
                {
                    return result;
                }
            }

            if (handleMenuMessage is not null &&
                handleMenuMessage(contextMenu2, message, wParam, lParam) >= 0)
            {
                return 0;
            }

            return DefSubclassProc(window, message, wParam, lParam);
        };
    }

    private static bool IsShellMenuMessage(uint message) => message is
        WmInitMenuPopup or WmDrawItem or WmMeasureItem or WmMenuChar;

    private static IShellContextMenuSession CreateWithComInitialized(
        Func<ShellContextMenuSession> createSession)
    {
        try
        {
            var initializeResult = CoInitializeEx(0, CoInitApartmentThreaded);
            ThrowForHResult(initializeResult);
            try
            {
                return createSession();
            }
            catch
            {
                CoUninitialize();
                throw;
            }
        }
        catch (FileCommandException)
        {
            throw;
        }
        catch (ShellContextMenuException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is COMException or Win32Exception or InvalidOperationException)
        {
            throw new ShellContextMenuException(
                "Windows Shell 메뉴를 표시하거나 명령을 실행하지 못했습니다.",
                exception);
        }
    }

    private sealed class ShellContextMenuSession : IShellContextMenuSession
    {
        private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
        private nint _contextMenu;
        private nint _contextMenu2;
        private nint _contextMenu3;
        private nint _contextMenuOwner;
        private nint _popupMenu;
        private nint _ownerWindow;
        private bool _disposed;

        public ShellContextMenuSession(
            nint contextMenu,
            nint ownerWindow,
            nint contextMenuOwner = 0)
        {
            _contextMenu = contextMenu;
            _ownerWindow = ownerWindow;
            _contextMenuOwner = contextMenuOwner;

            try
            {
                _popupMenu = CreatePopupMenu();
                if (_popupMenu == 0)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                var queryContextMenu = GetVtableDelegate<QueryContextMenuDelegate>(_contextMenu, 3);
                ThrowForHResult(queryContextMenu(
                    _contextMenu,
                    _popupMenu,
                    0,
                    FirstCommandId,
                    LastCommandId,
                    CmfNormal));

                TryQueryInterface(_contextMenu, ContextMenu3InterfaceId, out _contextMenu3);
                if (_contextMenu3 == 0)
                {
                    TryQueryInterface(_contextMenu, ContextMenu2InterfaceId, out _contextMenu2);
                }

                NativeMenuItemCount = GetMenuItemCount(_popupMenu);
                if (NativeMenuItemCount < 0)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
            }
            catch
            {
                if (_popupMenu != 0)
                {
                    _ = DestroyMenu(_popupMenu);
                    _popupMenu = 0;
                }

                ReleaseIfNotZero(_contextMenu3);
                _contextMenu3 = 0;
                ReleaseIfNotZero(_contextMenu2);
                _contextMenu2 = 0;
                _contextMenu = 0;
                _contextMenuOwner = 0;
                _ownerWindow = 0;
                throw;
            }
        }

        public int NativeMenuItemCount { get; }

        public bool Show(
            int screenX,
            int screenY,
            ShellContextMenuShowOptions options = ShellContextMenuShowOptions.None)
        {
            EnsureUsable();
            return RunShellAction(() =>
            {
                using var hostLifetime = ShellCommandHostLifetime.Create(options);
                try
                {
                    return ShowNativeContextMenu(
                        _contextMenu,
                        _popupMenu,
                        _contextMenu2,
                        _contextMenu3,
                        _ownerWindow,
                        screenX,
                        screenY,
                        options);
                }
                finally
                {
                    hostLifetime?.CompleteAndWait();
                }
            });
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            EnsureOwnerThread();
            _disposed = true;
            ReleaseNativeResources(uninitializeCom: true);
        }

        private void EnsureUsable()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            EnsureOwnerThread();
        }

        private void EnsureOwnerThread()
        {
            if (Environment.CurrentManagedThreadId != _ownerThreadId)
            {
                throw new InvalidOperationException(
                    "Windows Shell 메뉴 세션은 생성한 UI 스레드에서 사용해야 합니다.");
            }
        }

        private void ReleaseNativeResources(bool uninitializeCom)
        {
            if (_popupMenu != 0)
            {
                _ = DestroyMenu(_popupMenu);
                _popupMenu = 0;
            }

            ReleaseIfNotZero(_contextMenu3);
            _contextMenu3 = 0;
            ReleaseIfNotZero(_contextMenu2);
            _contextMenu2 = 0;
            ReleaseIfNotZero(_contextMenu);
            _contextMenu = 0;
            ReleaseIfNotZero(_contextMenuOwner);
            _contextMenuOwner = 0;
            _ownerWindow = 0;

            if (uninitializeCom)
            {
                CoUninitialize();
            }
        }

        private static T RunShellAction<T>(Func<T> action)
        {
            try
            {
                return action();
            }
            catch (ShellContextMenuException)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is COMException or Win32Exception or InvalidOperationException)
            {
                throw new ShellContextMenuException(
                    "Windows Shell 메뉴 명령을 실행하지 못했습니다.",
                    exception);
            }
        }

    }

    private static void EnsureWindowsAndOwner(nint ownerWindow)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new ShellContextMenuException("Windows Shell 메뉴는 Windows에서만 사용할 수 있습니다.");
        }

        if (ownerWindow == 0 || !IsWindow(ownerWindow))
        {
            throw new ShellContextMenuException("Windows Shell 메뉴의 소유 창을 찾을 수 없습니다.");
        }
    }

    private static void TryQueryInterface(nint instance, Guid interfaceId, out nint result)
    {
        var id = interfaceId;
        if (Marshal.QueryInterface(instance, in id, out result) < 0)
        {
            result = 0;
        }
    }

    private static TDelegate GetVtableDelegate<TDelegate>(nint instance, int methodIndex)
        where TDelegate : Delegate
    {
        var vtable = Marshal.ReadIntPtr(instance);
        var method = Marshal.ReadIntPtr(vtable, methodIndex * nint.Size);
        return Marshal.GetDelegateForFunctionPointer<TDelegate>(method);
    }

    private static void ReleaseIfNotZero(nint instance)
    {
        if (instance != 0)
        {
            _ = Marshal.Release(instance);
        }
    }

    private static void ThrowForHResult(int result)
    {
        if (result < 0)
        {
            Marshal.ThrowExceptionForHR(result);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    [SupportedOSPlatform("windows")]
    private static extern int SHParseDisplayName(
        string name,
        nint bindingContext,
        out nint itemIdList,
        uint attributesIn,
        out uint attributesOut);

    [DllImport("shell32.dll", PreserveSig = true)]
    [SupportedOSPlatform("windows")]
    private static extern int SHBindToParent(
        nint itemIdList,
        ref Guid interfaceId,
        out nint parent,
        out nint childItemId);

    [DllImport("shell32.dll", PreserveSig = true)]
    [SupportedOSPlatform("windows")]
    private static extern int SHGetDesktopFolder(out nint desktopFolder);

    [DllImport("ole32.dll", PreserveSig = true)]
    [SupportedOSPlatform("windows")]
    private static extern int CoInitializeEx(nint reserved, uint coInit);

    [DllImport("ole32.dll", PreserveSig = true)]
    [SupportedOSPlatform("windows")]
    private static extern int CoCreateInstance(
        ref Guid classId,
        nint outer,
        uint classContext,
        ref Guid interfaceId,
        out nint instance);

    [DllImport("ole32.dll")]
    [SupportedOSPlatform("windows")]
    private static extern void CoUninitialize();

    [DllImport("ole32.dll")]
    [SupportedOSPlatform("windows")]
    private static extern void CoTaskMemFree(nint memory);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    [SupportedOSPlatform("windows")]
    private static extern bool IsWindow(nint window);

    [DllImport("user32.dll", SetLastError = true)]
    [SupportedOSPlatform("windows")]
    private static extern nint CreatePopupMenu();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    [SupportedOSPlatform("windows")]
    private static extern bool DestroyMenu(nint menu);

    [DllImport("user32.dll", SetLastError = true)]
    [SupportedOSPlatform("windows")]
    private static extern int GetMenuItemCount(nint menu);

    [DllImport("user32.dll", SetLastError = true)]
    [SupportedOSPlatform("windows")]
    private static extern uint TrackPopupMenuEx(
        nint menu,
        uint flags,
        int screenX,
        int screenY,
        nint ownerWindow,
        nint parameters);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    [SupportedOSPlatform("windows")]
    private static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll")]
    [SupportedOSPlatform("windows")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    [SupportedOSPlatform("windows")]
    private static extern nint GetAncestor(nint window, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    [SupportedOSPlatform("windows")]
    private static extern bool PostMessage(nint window, uint message, nuint wParam, nint lParam);

    [DllImport("kernel32.dll")]
    [SupportedOSPlatform("windows")]
    private static extern void SetLastError(uint errorCode);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    [SupportedOSPlatform("windows")]
    private static extern bool SetWindowSubclass(
        nint window,
        SubclassProc callback,
        nuint subclassId,
        nuint referenceData);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    [SupportedOSPlatform("windows")]
    private static extern bool RemoveWindowSubclass(
        nint window,
        SubclassProc callback,
        nuint subclassId);

    [DllImport("comctl32.dll")]
    [SupportedOSPlatform("windows")]
    private static extern nint DefSubclassProc(
        nint window,
        uint message,
        nuint wParam,
        nint lParam);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int CreateViewObjectDelegate(
        nint instance,
        nint ownerWindow,
        ref Guid interfaceId,
        out nint result);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetUiObjectOfDelegate(
        nint instance,
        nint ownerWindow,
        uint itemCount,
        nint childItemIds,
        ref Guid interfaceId,
        nint reserved,
        out nint result);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetItemObjectDelegate(
        nint instance,
        uint item,
        ref Guid interfaceId,
        out nint result);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int FindWindowShellDelegate(
        nint instance,
        ref NativeVariant location,
        ref NativeVariant locationRoot,
        int shellWindowType,
        out int windowHandle,
        int options,
        out nint dispatch);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int QueryServiceDelegate(
        nint instance,
        ref Guid serviceId,
        ref Guid interfaceId,
        out nint result);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int QueryActiveShellViewDelegate(nint instance, out nint shellView);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int QueryContextMenuDelegate(
        nint instance,
        nint menu,
        uint index,
        uint firstCommandId,
        uint lastCommandId,
        uint flags);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int InvokeCommandDelegate(nint instance, ref CommandInvokeInfo invokeInfo);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int HandleMenuMessageDelegate(
        nint instance,
        uint message,
        nuint wParam,
        nint lParam);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int HandleMenuMessage2Delegate(
        nint instance,
        uint message,
        nuint wParam,
        nint lParam,
        out nint result);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint SubclassProc(
        nint window,
        uint message,
        nuint wParam,
        nint lParam,
        nuint subclassId,
        nuint referenceData);

    [StructLayout(LayoutKind.Sequential)]
    private struct CommandInvokeInfo
    {
        public uint Size;
        public uint Mask;
        public nint OwnerWindow;
        public nint Verb;
        public nint Parameters;
        public nint Directory;
        public int Show;
        public uint HotKey;
        public nint Icon;
        public nint Title;
        public nint VerbUnicode;
        public nint ParametersUnicode;
        public nint DirectoryUnicode;
        public nint TitleUnicode;
        public NativePoint InvokePoint;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct NativePoint(int X, int Y);

    [StructLayout(LayoutKind.Explicit, Size = 16)]
    private struct NativeVariant
    {
        [FieldOffset(0)]
        public ushort Type;

        [FieldOffset(8)]
        public nint Value;
    }
}
