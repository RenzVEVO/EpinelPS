namespace EpinelPS.LobbyServer.LobbyUser;

[GameRequest("/user/scenario/watchalbumlog")]
public class WatchAlbumScenarioLog : LobbyMessage
{
    protected override async Task HandleAsync()
    {
        ReqWatchAlbumScenarioLog req = await ReadData<ReqWatchAlbumScenarioLog>();
        ResWatchAlbumScenarioLog response = new();
        await WriteDataAsync(response);
    }
}
