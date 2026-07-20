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
    private const uint GcsVerbW = 0x00000004;
    private const uint MiimState = 0x00000001;
    private const uint MiimId = 0x00000002;
    private const uint MiimSubmenu = 0x00000004;
    private const uint MiimFType = 0x00000100;
    private const uint MiimString = 0x00000040;
    private const uint MiimBitmap = 0x00000080;
    private const uint MftSeparator = 0x00000800;
    private const uint MfsDisabled = 0x00000003;
    private const uint MfsChecked = 0x00000008;
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

                var session = new ShellContextMenuSession(contextMenu, ownerWindow);
                contextMenu = 0;
                return session;
            }
            finally
            {
                ReleaseIfNotZero(contextMenu);
                ReleaseIfNotZero(desktopFolder);
            }
        });
    }

    private static void ShowClassicContextMenu(
        nint contextMenu,
        nint popupMenu,
        nint contextMenu2,
        nint contextMenu3,
        nint ownerWindow,
        int screenX,
        int screenY)
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

            InvokeContextCommand(contextMenu, ownerWindow, selectedCommand, screenX, screenY);
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
        int screenY)
    {
        if (commandId < FirstCommandId || commandId > LastCommandId)
        {
            throw new ShellContextMenuException("선택한 Windows Shell 명령을 찾을 수 없습니다.");
        }

        var commandOffset = commandId - FirstCommandId;
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
        private nint _popupMenu;
        private nint _ownerWindow;
        private bool _disposed;

        public ShellContextMenuSession(nint contextMenu, nint ownerWindow)
        {
            _contextMenu = contextMenu;
            _ownerWindow = ownerWindow;

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

                Entries = ReadMenuEntries(_popupMenu);
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
                _ownerWindow = 0;
                throw;
            }
        }

        public IReadOnlyList<ShellContextMenuEntry> Entries { get; } =
            Array.Empty<ShellContextMenuEntry>();

        public void Invoke(uint commandId, int screenX, int screenY)
        {
            EnsureUsable();
            RunShellAction(() => InvokeContextCommand(
                _contextMenu,
                _ownerWindow,
                commandId,
                screenX,
                screenY));
        }

        public void ShowClassic(int screenX, int screenY)
        {
            EnsureUsable();
            RunShellAction(() => ShowClassicContextMenu(
                _contextMenu,
                _popupMenu,
                _contextMenu2,
                _contextMenu3,
                _ownerWindow,
                screenX,
                screenY));
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

        private IReadOnlyList<ShellContextMenuEntry> ReadMenuEntries(nint menu)
        {
            var count = GetMenuItemCount(menu);
            if (count < 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            var entries = new List<ShellContextMenuEntry>(count);
            for (uint position = 0; position < count; position++)
            {
                var info = GetMenuEntryInfo(menu, position);
                if ((info.Type & MftSeparator) != 0)
                {
                    entries.Add(new ShellContextMenuEntry(
                        ShellContextMenuEntryKind.Separator,
                        0,
                        string.Empty,
                        null,
                        false,
                        false,
                        0,
                        Array.Empty<ShellContextMenuEntry>()));
                    continue;
                }

                var text = RemoveAccessKeyMarkers(info.Text);
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                IReadOnlyList<ShellContextMenuEntry> children = Array.Empty<ShellContextMenuEntry>();
                var kind = ShellContextMenuEntryKind.Command;
                if (info.Submenu != 0)
                {
                    InitializeSubmenu(info.Submenu, position);
                    children = ReadMenuEntries(info.Submenu);
                    kind = ShellContextMenuEntryKind.Submenu;
                }

                entries.Add(new ShellContextMenuEntry(
                    kind,
                    info.CommandId,
                    text,
                    kind == ShellContextMenuEntryKind.Command
                        ? GetCanonicalVerb(info.CommandId)
                        : null,
                    (info.State & MfsDisabled) == 0,
                    (info.State & MfsChecked) != 0,
                    info.BitmapHandle,
                    children));
            }

            return NormalizeSeparators(entries);
        }

        private MenuEntryInfo GetMenuEntryInfo(nint menu, uint position)
        {
            var nativeInfo = new MenuItemInfo
            {
                Size = (uint)Marshal.SizeOf<MenuItemInfo>(),
                Mask = MiimFType | MiimState | MiimId | MiimSubmenu | MiimString | MiimBitmap,
            };

            if (!GetMenuItemInfo(menu, position, true, ref nativeInfo))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            var characterCount = checked((int)nativeInfo.CharacterCount + 1);
            var textBuffer = Marshal.AllocHGlobal(characterCount * sizeof(char));
            try
            {
                nativeInfo.Text = textBuffer;
                nativeInfo.CharacterCount = (uint)characterCount;
                if (!GetMenuItemInfo(menu, position, true, ref nativeInfo))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                return new MenuEntryInfo(
                    nativeInfo.Type,
                    nativeInfo.State,
                    nativeInfo.CommandId,
                    nativeInfo.Submenu,
                    nativeInfo.BitmapItem,
                    Marshal.PtrToStringUni(textBuffer) ?? string.Empty);
            }
            finally
            {
                Marshal.FreeHGlobal(textBuffer);
            }
        }

        private string? GetCanonicalVerb(uint commandId)
        {
            if (commandId < FirstCommandId || commandId > LastCommandId)
            {
                return null;
            }

            const int maximumCharacters = 260;
            var buffer = Marshal.AllocHGlobal(maximumCharacters * sizeof(char));
            try
            {
                Marshal.WriteInt16(buffer, 0);
                var getCommandString = GetVtableDelegate<GetCommandStringDelegate>(_contextMenu, 5);
                var result = getCommandString(
                    _contextMenu,
                    commandId - FirstCommandId,
                    GcsVerbW,
                    0,
                    buffer,
                    maximumCharacters);
                return result >= 0
                    ? NullIfWhiteSpace(Marshal.PtrToStringUni(buffer))
                    : null;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private void InitializeSubmenu(nint submenu, uint position)
        {
            if (_contextMenu3 != 0)
            {
                var handler = GetVtableDelegate<HandleMenuMessage2Delegate>(_contextMenu3, 7);
                _ = handler(_contextMenu3, WmInitMenuPopup, (nuint)submenu, (nint)position, out _);
                return;
            }

            if (_contextMenu2 != 0)
            {
                var handler = GetVtableDelegate<HandleMenuMessageDelegate>(_contextMenu2, 6);
                _ = handler(_contextMenu2, WmInitMenuPopup, (nuint)submenu, (nint)position);
            }
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
            _ownerWindow = 0;

            if (uninitializeCom)
            {
                CoUninitialize();
            }
        }

        private static void RunShellAction(Action action)
        {
            try
            {
                action();
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

        private static IReadOnlyList<ShellContextMenuEntry> NormalizeSeparators(
            IReadOnlyList<ShellContextMenuEntry> entries)
        {
            var normalized = new List<ShellContextMenuEntry>(entries.Count);
            foreach (var entry in entries)
            {
                if (entry.Kind == ShellContextMenuEntryKind.Separator &&
                    (normalized.Count == 0 || normalized[^1].Kind == ShellContextMenuEntryKind.Separator))
                {
                    continue;
                }

                normalized.Add(entry);
            }

            if (normalized.Count > 0 && normalized[^1].Kind == ShellContextMenuEntryKind.Separator)
            {
                normalized.RemoveAt(normalized.Count - 1);
            }

            return normalized;
        }

        private static string RemoveAccessKeyMarkers(string text)
        {
            var builder = new System.Text.StringBuilder(text.Length);
            for (var index = 0; index < text.Length; index++)
            {
                if (text[index] != '&')
                {
                    builder.Append(text[index]);
                    continue;
                }

                if (index + 1 < text.Length && text[index + 1] == '&')
                {
                    builder.Append('&');
                    index++;
                }
            }

            return builder.ToString().Replace("\t", "    ", StringComparison.Ordinal).Trim();
        }

        private static string? NullIfWhiteSpace(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value;

        private readonly record struct MenuEntryInfo(
            uint Type,
            uint State,
            uint CommandId,
            nint Submenu,
            nint BitmapHandle,
            string Text);
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
    private static extern int GetMenuItemCount(nint menu);

    [DllImport("user32.dll", EntryPoint = "GetMenuItemInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    [SupportedOSPlatform("windows")]
    private static extern bool GetMenuItemInfo(
        nint menu,
        uint item,
        [MarshalAs(UnmanagedType.Bool)] bool byPosition,
        ref MenuItemInfo menuItemInfo);

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
    private delegate int GetCommandStringDelegate(
        nint instance,
        nuint commandOffset,
        uint type,
        nint reserved,
        nint name,
        int maximumCharacters);

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

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MenuItemInfo
    {
        public uint Size;
        public uint Mask;
        public uint Type;
        public uint State;
        public uint CommandId;
        public nint Submenu;
        public nint BitmapChecked;
        public nint BitmapUnchecked;
        public nuint ItemData;
        public nint Text;
        public uint CharacterCount;
        public nint BitmapItem;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct NativePoint(int X, int Y);
}
