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
    public void CommandResult_SerializesToTheProtocolFourAckShape()
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
