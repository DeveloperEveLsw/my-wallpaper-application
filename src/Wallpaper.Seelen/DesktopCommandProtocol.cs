using System.Text.Json;

namespace Wallpaper.Seelen;

public static class DesktopCommandActions
{
    public const string Open = "open";
    public const string ShowInExplorer = "showInExplorer";
    public const string Rename = "rename";
    public const string Recycle = "recycle";
    public const string PrepareMove = "prepareMove";
    public const string Move = "move";

    public static bool IsSupported(string action) => action is
        Open or
        ShowInExplorer or
        Rename or
        Recycle or
        PrepareMove or
        Move;
}

public sealed record DesktopCommandRequest(
    string RequestId,
    string Action,
    string ItemId,
    string? DestinationId,
    string? NewName);

public sealed record DesktopCommandResult(
    string Type,
    string RequestId,
    string Action,
    string ItemId,
    string? DestinationId,
    bool Accepted,
    string? Code,
    string? Message,
    string? ProposedName,
    bool HasNameCollision)
{
    public const string MessageType = "itemCommandAck";

    public static DesktopCommandResult Success(
        DesktopCommandRequest request,
        string? proposedName = null,
        bool hasNameCollision = false,
        string? code = null,
        string? message = null) =>
        new(
            MessageType,
            request.RequestId,
            request.Action,
            request.ItemId,
            request.DestinationId,
            true,
            code,
            message,
            proposedName,
            hasNameCollision);

    public static DesktopCommandResult Failure(
        DesktopCommandRequest request,
        string code,
        string message,
        string? proposedName = null,
        bool hasNameCollision = false) =>
        new(
            MessageType,
            request.RequestId,
            request.Action,
            request.ItemId,
            request.DestinationId,
            false,
            code,
            message,
            proposedName,
            hasNameCollision);
}

public static class DesktopCommandProtocol
{
    private const int MaximumRequestIdLength = 128;
    private const int MaximumItemIdLength = 4096;
    private const int MaximumNameLengthOnWire = 1024;

    public static bool TryParse(JsonElement root, out DesktopCommandRequest? request)
    {
        request = null;
        if (root.ValueKind != JsonValueKind.Object
            || !TryGetString(root, "type", 32, allowEmpty: false, out var type)
            || type != "itemCommand"
            || !TryGetString(
                root,
                "requestId",
                MaximumRequestIdLength,
                allowEmpty: false,
                out var requestId)
            || !requestId.All(IsRequestIdCharacter)
            || !TryGetString(root, "action", 32, allowEmpty: false, out var action)
            || !DesktopCommandActions.IsSupported(action)
            || !TryGetString(
                root,
                "itemId",
                MaximumItemIdLength,
                allowEmpty: false,
                out var itemId))
        {
            return false;
        }

        string? destinationId = null;
        string? newName = null;
        if (action is DesktopCommandActions.PrepareMove or DesktopCommandActions.Move)
        {
            if (!TryGetString(
                    root,
                    "destinationId",
                    MaximumItemIdLength,
                    allowEmpty: false,
                    out destinationId))
            {
                return false;
            }
        }

        if (action is DesktopCommandActions.Rename or DesktopCommandActions.Move)
        {
            if (!TryGetString(
                    root,
                    "newName",
                    MaximumNameLengthOnWire,
                    allowEmpty: true,
                    out newName))
            {
                return false;
            }
        }
        else if (action == DesktopCommandActions.PrepareMove
            && root.TryGetProperty("newName", out _)
            && !TryGetString(
                root,
                "newName",
                MaximumNameLengthOnWire,
                allowEmpty: true,
                out newName))
        {
            return false;
        }

        request = new DesktopCommandRequest(requestId, action, itemId, destinationId, newName);
        return true;
    }

    private static bool TryGetString(
        JsonElement root,
        string propertyName,
        int maximumLength,
        bool allowEmpty,
        out string value)
    {
        value = string.Empty;
        if (!root.TryGetProperty(propertyName, out var element)
            || element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var candidate = element.GetString();
        if (candidate is null
            || candidate.Length > maximumLength
            || (!allowEmpty && string.IsNullOrWhiteSpace(candidate)))
        {
            return false;
        }

        value = candidate;
        return true;
    }

    private static bool IsRequestIdCharacter(char character) =>
        char.IsAsciiLetterOrDigit(character) || character is '-' or '_';
}
