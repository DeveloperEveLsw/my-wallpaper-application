using System.Text.Json;

namespace Wallpaper.Seelen.Tests;

public sealed class DesktopCommandProtocolTests
{
    [Theory]
    [InlineData("open")]
    [InlineData("showInExplorer")]
    [InlineData("recycle")]
    public void TryParse_AcceptsBasicItemCommands(string action)
    {
        using var document = JsonDocument.Parse(
            $$"""
            {
              "type": "itemCommand",
              "requestId": "request-1",
              "action": "{{action}}",
              "itemId": "file:WORK/REPORT.TXT"
            }
            """);

        var accepted = DesktopCommandProtocol.TryParse(document.RootElement, out var request);

        Assert.True(accepted);
        Assert.NotNull(request);
        Assert.Equal(action, request.Action);
        Assert.Null(request.DestinationId);
        Assert.Null(request.NewName);
    }

    [Fact]
    public void TryParse_AcceptsRenameAndMoveFields()
    {
        using var renameDocument = JsonDocument.Parse(
            """
            {
              "type": "itemCommand",
              "requestId": "rename_1",
              "action": "rename",
              "itemId": "folder:WORK",
              "newName": "History"
            }
            """);
        using var moveDocument = JsonDocument.Parse(
            """
            {
              "type": "itemCommand",
              "requestId": "move_1",
              "action": "move",
              "itemId": "file:WORK/REPORT.TXT",
              "destinationId": "virtual:loose-files",
              "newName": "report.txt"
            }
            """);

        Assert.True(DesktopCommandProtocol.TryParse(renameDocument.RootElement, out var rename));
        Assert.Equal("History", rename!.NewName);
        Assert.True(DesktopCommandProtocol.TryParse(moveDocument.RootElement, out var move));
        Assert.Equal("virtual:loose-files", move!.DestinationId);
        Assert.Equal("report.txt", move.NewName);
    }

    [Fact]
    public void TryParse_AcceptsPrepareMoveNameForServerSideValidation()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "type": "itemCommand",
              "requestId": "prepare-1",
              "action": "prepareMove",
              "itemId": "file:WORK/REPORT.TXT",
              "destinationId": "folder:ARCHIVE",
              "newName": "ignored-client-name.txt"
            }
            """);

        Assert.True(DesktopCommandProtocol.TryParse(document.RootElement, out var request));
        Assert.NotNull(request);
        Assert.Equal("folder:ARCHIVE", request.DestinationId);
        Assert.Equal("ignored-client-name.txt", request.NewName);
    }

    [Fact]
    public void CommandResult_SerializesToTheItemCommandAckShape()
    {
        var request = new DesktopCommandRequest(
            "request-1",
            DesktopCommandActions.PrepareMove,
            "file:WORK/REPORT.TXT",
            "folder:ARCHIVE",
            null);
        var result = DesktopCommandResult.Success(
            request,
            "report (1).txt",
            hasNameCollision: true);

        var json = JsonSerializer.Serialize(result, JsonSerializerOptions.Web);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("itemCommandAck", root.GetProperty("type").GetString());
        Assert.Equal("request-1", root.GetProperty("requestId").GetString());
        Assert.Equal("prepareMove", root.GetProperty("action").GetString());
        Assert.True(root.GetProperty("accepted").GetBoolean());
        Assert.Equal("report (1).txt", root.GetProperty("proposedName").GetString());
        Assert.True(root.GetProperty("hasNameCollision").GetBoolean());
    }

    [Fact]
    public void ShellMenuTryParse_AcceptsPhysicalCoordinatesAndOwnerWindow()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "type": "prepareShellMenu",
              "requestId": "shell-menu-1",
              "itemId": "file:WORK/REPORT.TXT",
              "screenX": -1440,
              "screenY": 225,
              "ownerWindow": 8192
            }
            """);

        var accepted = DesktopShellMenuProtocol.TryParse(
            document.RootElement,
            out var request);

        Assert.True(accepted);
        Assert.NotNull(request);
        Assert.Equal(-1440, request.ScreenX);
        Assert.Equal(225, request.ScreenY);
        Assert.Equal(8192, request.OwnerWindow);
    }

    [Theory]
    [InlineData(
        """{"type":"prepareShellMenu","requestId":"bad id","itemId":"file:A","screenX":1,"screenY":2,"ownerWindow":3}""")]
    [InlineData(
        """{"type":"prepareShellMenu","requestId":"1","itemId":"file:A","screenX":1.5,"screenY":2,"ownerWindow":3}""")]
    [InlineData(
        """{"type":"prepareShellMenu","requestId":"1","itemId":"file:A","screenX":1,"screenY":2,"ownerWindow":0}""")]
    [InlineData(
        """{"type":"prepareShellMenu","requestId":"1","itemId":"file:A","screenX":1,"screenY":2}""")]
    public void ShellMenuTryParse_RejectsMalformedRequests(string json)
    {
        using var document = JsonDocument.Parse(json);

        Assert.False(DesktopShellMenuProtocol.TryParse(document.RootElement, out _));
    }

    [Fact]
    public void ShellMenuPrepareResult_SerializesWithoutAFileSystemPath()
    {
        var request = new DesktopShellMenuRequest(
            "shell-menu-1",
            "file:WORK/REPORT.TXT",
            100,
            200,
            8192);
        var result = DesktopShellMenuPrepareResult.Success(request, "one-time-ticket");

        var json = JsonSerializer.Serialize(result, JsonSerializerOptions.Web);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("shellMenuPrepareAck", root.GetProperty("type").GetString());
        Assert.Equal("one-time-ticket", root.GetProperty("ticket").GetString());
        Assert.False(root.TryGetProperty("rootPath", out _));
        Assert.False(root.TryGetProperty("relativePath", out _));
    }

    [Theory]
    [InlineData("""{"type":"itemCommand","requestId":"bad id","action":"open","itemId":"file:A"}""")]
    [InlineData("""{"type":"itemCommand","requestId":"1","action":"unknown","itemId":"file:A"}""")]
    [InlineData("""{"type":"itemCommand","requestId":"1","action":"move","itemId":"file:A","newName":"a"}""")]
    [InlineData("""{"type":"itemCommand","requestId":"1","action":"rename","itemId":"file:A"}""")]
    [InlineData("""{"type":"itemCommand","requestId":1,"action":"open","itemId":"file:A"}""")]
    public void TryParse_RejectsMalformedOrIncompleteCommands(string json)
    {
        using var document = JsonDocument.Parse(json);

        Assert.False(DesktopCommandProtocol.TryParse(document.RootElement, out _));
    }
}
