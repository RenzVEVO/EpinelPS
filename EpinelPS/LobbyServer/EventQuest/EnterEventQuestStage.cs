namespace EpinelPS.LobbyServer.EventQuest;

[GameRequest("/eventquest/stage/enter")]
[GameRequest("/eventquest/enterstage")]
public class EnterEventQuestStage : LobbyMessage
{
    protected override async Task HandleAsync()
    {
        _ = await ReadData<ReqEnterEventQuestStage>();
        ResEnterEventQuestStage response = new();
        await WriteDataAsync(response);
    }
}
