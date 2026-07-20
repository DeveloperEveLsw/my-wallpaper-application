using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Wallpaper.Core.FileOperations;

namespace Wallpaper.Infrastructure.Windows.Shell;

[SupportedOSPlatform("windows")]
public sealed class WindowsShellContextMenuService : IShellContextMenuService
{
    private const uint CoInitApartmentThreaded = 0x2;
    private const uint CmfNormal = 0x00000000;
    private const uint CmicMaskUnicode = 0x00004000;
    private const uint CmicMaskPtInvoke = 0x20000000;
    private const uint TpmRightButton = 0x0002;
    private const uint TpmReturnCommand = 0x0100;
    private const int SwShowNormal = 1;
    private const uint FirstCommandId = 1;
    private const uint LastCommandId = 0x7fff;
    private const int WmNull = 0x0000;
    private const int WmDrawItem = 0x002b;
    private const int WmMeasureItem = 0x002c;
    private const int WmInitMenuPopup = 0x0117;
    private const int WmMenuChar = 0x0120;
    private const nuint SubclassId = 0x57414c4c;

    private static readonly Guid ShellFolderInterfaceId = new("000214E6-0000-0000-C000-000000000046");
    private static readonly Guid ContextMenuInterfaceId = new("000214E4-0000-0000-C000-000000000046");
    private static readonly Guid ContextMenu2InterfaceId = new("000214F4-0000-0000-C000-000000000046");
    private static readonly Guid ContextMenu3InterfaceId = new("BCFCE0A0-EC17-11D0-8D10-00A0C90F2719");

    public void ShowItemContextMenu(
        FileCommandTarget target,
        nint ownerWindow,
        int screenX,
        int screenY)
    {
        ArgumentNullException.ThrowIfNull(target);
        var validated = FileCommandTargetValidator.ValidateExisting(target);
        EnsureWindowsAndOwner(ownerWindow);
        RunWithComInitialized(() =>
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

                ShowContextMenu(contextMenu, ownerWindow, screenX, screenY);
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

    public void ShowDesktopContextMenu(
        nint ownerWindow,
        int screenX,
        int screenY)
    {
        EnsureWindowsAndOwner(ownerWindow);

        RunWithComInitialized(() =>
        {
            nint desktopFolder = 0;
            nint contextMenu = 0;

            try
            {
                ThrowForHResult(SHGetDesktopFolder(out desktopFolder));
                var contextMenuId = ContextMenuInterfaceId;
                var createViewObject = GetVtableDelegate<CreateViewObjectDelegate>(desktopFolder, 8);
                ThrowForHResult(createViewObject(
                    desktopFolder,
                    ownerWindow,
                    ref contextMenuId,
                    out contextMenu));

                ShowContextMenu(contextMenu, ownerWindow, screenX, screenY);
            }
            finally
            {
                ReleaseIfNotZero(contextMenu);
                ReleaseIfNotZero(desktopFolder);
            }
        });
    }

    [SupportedOSPlatform("windows")]
    private static void ShowContextMenu(
        nint contextMenu,
        nint ownerWindow,
        int screenX,
        int screenY)
    {
        nint popupMenu = 0;
        nint contextMenu2 = 0;
        nint contextMenu3 = 0;
        SubclassProc? subclassProc = null;
        var subclassInstalled = false;

        try
        {
            popupMenu = CreatePopupMenu();
            if (popupMenu == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            var queryContextMenu = GetVtableDelegate<QueryContextMenuDelegate>(contextMenu, 3);
            ThrowForHResult(queryContextMenu(
                contextMenu,
                popupMenu,
                0,
                FirstCommandId,
                LastCommandId,
                CmfNormal));

            TryQueryInterface(contextMenu, ContextMenu3InterfaceId, out contextMenu3);
            if (contextMenu3 == 0)
            {
                TryQueryInterface(contextMenu, ContextMenu2InterfaceId, out contextMenu2);
            }

            subclassProc = CreateSubclassProc(contextMenu2, contextMenu3);
            if (!SetWindowSubclass(ownerWindow, subclassProc, SubclassId, 0))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            subclassInstalled = true;
            _ = SetForegroundWindow(ownerWindow);
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

                return;
            }

            var commandOffset = selectedCommand - FirstCommandId;
            var invoke = new CommandInvokeInfo
            {
                Size = (uint)Marshal.SizeOf<CommandInvokeInfo>(),
                Mask = CmicMaskUnicode | CmicMaskPtInvoke,
                OwnerWindow = ownerWindow,
                Verb = (nint)commandOffset,
                Show = SwShowNormal,
                VerbUnicode = (nint)commandOffset,
                InvokePoint = new NativePoint(screenX, screenY),
            };

            var invokeCommand = GetVtableDelegate<InvokeCommandDelegate>(contextMenu, 4);
            ThrowForHResult(invokeCommand(contextMenu, ref invoke));
        }
        finally
        {
            if (subclassInstalled && subclassProc is not null)
            {
                _ = RemoveWindowSubclass(ownerWindow, subclassProc, SubclassId);
            }

            _ = PostMessage(ownerWindow, WmNull, 0, 0);
            ReleaseIfNotZero(contextMenu3);
            ReleaseIfNotZero(contextMenu2);
            if (popupMenu != 0)
            {
                _ = DestroyMenu(popupMenu);
            }
        }
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

    private static void RunWithComInitialized(Action action)
    {
        try
        {
            var initializeResult = CoInitializeEx(0, CoInitApartmentThreaded);
            ThrowForHResult(initializeResult);
            try
            {
                action();
            }
            finally
            {
                CoUninitialize();
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
}
