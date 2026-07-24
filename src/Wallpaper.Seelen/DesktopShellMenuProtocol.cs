using System.Text.Json;
using Wallpaper.Core.FileOperations;

namespace Wallpaper.Seelen;

public sealed record DesktopShellMenuRequest(
    string RequestId,
    string ItemId,
    int ScreenX,
    int ScreenY,
    long OwnerWindow);

public sealed record DesktopShellMenuPrepareResult(
    string Type,
    string RequestId,
    bool Accepted,
    string? Code,
    string? Message,
    string? Ticket)
{
    public const string MessageType = "shellMenuPrepareAck";

    public static DesktopShellMenuPrepareResult Success(
        DesktopShellMenuRequest request,
        string ticket) =>
        new(MessageType, request.RequestId, true, null, null, ticket);

    public static DesktopShellMenuPrepareResult Failure(
        DesktopShellMenuRequest request,
        string code,
        string message) =>
        new(MessageType, request.RequestId, false, code, message, null);
}

public sealed record DesktopShellMenuTargetResult(
    bool Accepted,
    string? Code,
    string? Message,
    FileCommandTarget? Target)
{
    public static DesktopShellMenuTargetResult Success(FileCommandTarget target) =>
        new(true, null, null, target);

    public static DesktopShellMenuTargetResult Failure(string code, string message) =>
        new(false, code, message, null);
}

public static class DesktopShellMenuProtocol
{
    private const int MaximumRequestIdLength = 128;
    private const int MaximumItemIdLength = 4096;

    public static bool TryParse(JsonElement root, out DesktopShellMenuRequest? request)
    {
        request = null;
        if (root.ValueKind != JsonValueKind.Object
            || !TryGetString(root, "type", 32, out var type)
            || type != "prepareShellMenu"
            || !TryGetString(root, "requestId", MaximumRequestIdLength, out var requestId)
            || !requestId.All(IsRequestIdCharacter)
            || !TryGetString(root, "itemId", MaximumItemIdLength, out var itemId)
            || !root.TryGetProperty("screenX", out var screenXElement)
            || !screenXElement.TryGetInt32(out var screenX)
            || !root.TryGetProperty("screenY", out var screenYElement)
            || !screenYElement.TryGetInt32(out var screenY)
            || !root.TryGetProperty("ownerWindow", out var ownerWindowElement)
            || !ownerWindowElement.TryGetInt64(out var ownerWindow)
            || ownerWindow <= 0)
        {
            return false;
        }

        request = new DesktopShellMenuRequest(
            requestId,
            itemId,
            screenX,
            screenY,
            ownerWindow);
        return true;
    }

    private static bool TryGetString(
        JsonElement root,
        string propertyName,
        int maximumLength,
        out string value)
    {
        value = string.Empty;
        if (!root.TryGetProperty(propertyName, out var element)
            || element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var candidate = element.GetString();
        if (string.IsNullOrWhiteSpace(candidate) || candidate.Length > maximumLength)
        {
            return false;
        }

        value = candidate;
        return true;
    }

    private static bool IsRequestIdCharacter(char character) =>
        char.IsAsciiLetterOrDigit(character) || character is '-' or '_';
}
