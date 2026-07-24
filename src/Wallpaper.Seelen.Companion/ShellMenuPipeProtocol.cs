namespace Wallpaper.Seelen.Companion;

internal sealed record ShellMenuPipeRedeemRequest(string Ticket);

internal sealed record ShellMenuPipeLaunchResponse(
    bool Accepted,
    string? Code,
    string? Message,
    string? RequestId,
    string? RootPath,
    string? RelativePath,
    string? Kind,
    int ScreenX,
    int ScreenY,
    long OwnerWindow);

internal sealed record ShellMenuPipeCompletionRequest(
    string RequestId,
    bool Succeeded,
    bool CommandInvoked,
    string? Code,
    string? Message);
