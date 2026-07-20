using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Wallpaper.Core.FileOperations;

namespace Wallpaper.Infrastructure.Windows.FileOperations;

internal static class FileOperationRecycleBin
{
    private const uint FofNoConfirmation = 0x0010;
    private const uint FofAllowUndo = 0x0040;
    private const uint FofNoErrorUi = 0x0400;
    private const uint FofxRecycleOnDelete = 0x00080000;
    private const uint FofxEarlyFailure = 0x00100000;
    private const uint FofxAddUndoRecord = 0x20000000;

    private static readonly Guid FileOperationClassId = new("3ad05575-8857-4850-9277-11b85bdb8e09");
    private static readonly Guid FileOperationInterfaceId = new("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8");
    private static readonly Guid ShellItemInterfaceId = new("43826D1E-E718-42EE-BC55-A1E261C37BFE");

    [SupportedOSPlatform("windows")]
    public static Task RecycleAsync(FileCommandTarget target, CancellationToken cancellationToken) =>
        RunOnStaThreadAsync(() => RecycleCore(target), cancellationToken);

    [SupportedOSPlatform("windows")]
    private static void RecycleCore(FileCommandTarget target)
    {
        var validated = FileCommandTargetValidator.ValidateExisting(target);
        IntPtr operation = IntPtr.Zero;
        IntPtr shellItem = IntPtr.Zero;
        var comInitialized = false;

        try
        {
            var initializeResult = CoInitializeEx(IntPtr.Zero, 0x2);
            ThrowForHResult(initializeResult);
            comInitialized = true;

            var operationClassId = FileOperationClassId;
            var operationInterfaceId = FileOperationInterfaceId;
            ThrowForHResult(CoCreateInstance(
                ref operationClassId,
                IntPtr.Zero,
                0x1,
                ref operationInterfaceId,
                out operation));

            var setOperationFlags = GetVtableDelegate<SetOperationFlagsDelegate>(operation, 5);
            ThrowForHResult(setOperationFlags(
                operation,
                FofNoConfirmation |
                FofAllowUndo |
                FofNoErrorUi |
                FofxRecycleOnDelete |
                FofxEarlyFailure |
                FofxAddUndoRecord));

            var shellItemId = ShellItemInterfaceId;
            ThrowForHResult(SHCreateItemFromParsingName(
                validated.AbsolutePath,
                IntPtr.Zero,
                ref shellItemId,
                out shellItem));

            var deleteItem = GetVtableDelegate<DeleteItemDelegate>(operation, 18);
            var performOperations = GetVtableDelegate<PerformOperationsDelegate>(operation, 21);
            var getAnyOperationsAborted = GetVtableDelegate<GetAnyOperationsAbortedDelegate>(operation, 22);
            ThrowForHResult(deleteItem(operation, shellItem, IntPtr.Zero));
            ThrowForHResult(performOperations(operation));
            ThrowForHResult(getAnyOperationsAborted(operation, out var aborted));

            if (aborted != 0)
            {
                throw new FileCommandException(
                    FileCommandError.RecycleCancelled,
                    "휴지통 이동이 취소되었습니다.");
            }

            if (File.Exists(validated.AbsolutePath) || Directory.Exists(validated.AbsolutePath))
            {
                throw new FileCommandException(
                    FileCommandError.RecycleFailed,
                    "Windows가 항목을 휴지통으로 이동하지 못했습니다.");
            }
        }
        catch (FileCommandException)
        {
            throw;
        }
        catch (COMException exception)
        {
            throw new FileCommandException(
                FileCommandError.RecycleFailed,
                "Windows 휴지통 작업이 실패했습니다.",
                exception);
        }
        finally
        {
            if (shellItem != IntPtr.Zero)
            {
                _ = Marshal.Release(shellItem);
            }

            if (operation != IntPtr.Zero)
            {
                _ = Marshal.Release(operation);
            }

            if (comInitialized)
            {
                CoUninitialize();
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static Task RunOnStaThreadAsync(Action action, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                action();
                completion.TrySetResult();
            }
            catch (OperationCanceledException exception)
            {
                completion.TrySetCanceled(exception.CancellationToken);
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        })
        {
            IsBackground = true,
            Name = "Wallpaper recycle operation",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private static TDelegate GetVtableDelegate<TDelegate>(IntPtr instance, int methodIndex)
        where TDelegate : Delegate
    {
        var vtable = Marshal.ReadIntPtr(instance);
        var method = Marshal.ReadIntPtr(vtable, methodIndex * IntPtr.Size);
        return Marshal.GetDelegateForFunctionPointer<TDelegate>(method);
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
    private static extern int SHCreateItemFromParsingName(
        string path,
        IntPtr bindingContext,
        ref Guid interfaceId,
        out IntPtr shellItem);

    [DllImport("ole32.dll", PreserveSig = true)]
    [SupportedOSPlatform("windows")]
    private static extern int CoInitializeEx(IntPtr reserved, uint coInit);

    [DllImport("ole32.dll", PreserveSig = true)]
    [SupportedOSPlatform("windows")]
    private static extern int CoCreateInstance(
        ref Guid classId,
        IntPtr outerUnknown,
        uint classContext,
        ref Guid interfaceId,
        out IntPtr instance);

    [DllImport("ole32.dll")]
    [SupportedOSPlatform("windows")]
    private static extern void CoUninitialize();

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int SetOperationFlagsDelegate(IntPtr instance, uint operationFlags);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int DeleteItemDelegate(IntPtr instance, IntPtr shellItem, IntPtr progressSink);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int PerformOperationsDelegate(IntPtr instance);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetAnyOperationsAbortedDelegate(IntPtr instance, out int aborted);
}
