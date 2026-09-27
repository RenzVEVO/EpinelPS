using EpinelPS.Data;
using EpinelPS.Database;

namespace EpinelPS.LobbyServer.Archive;

[GameRequest("/archive/event-quest/clear")]
public class ClearArchiveEventQuest : LobbyMessage
{
    protected override async Task HandleAsync()
    {
        _ = await ReadData<ReqClearArchiveEventQuest>();
        ResClearArchiveEventQuest response = new();
        await WriteDataAsync(response);
    }
}

[GameRequest("/archive/event-quest/reset")]
public class ResetArchiveEventQuest : LobbyMessage
{
    protected override async Task HandleAsync()
    {
        _ = await ReadData<ReqResetArchiveEventQuest>();
        ResResetArchiveEventQuest response = new();
        await WriteDataAsync(response);
    }
}

[GameRequest("/archive/event-quest/reward/acquire")]
[GameRequest("/archive/event-quest/acquire-reward")]
[GameRequest("/archive/event-quest/reward/obtain")]
public class AcquireArchiveEventQuestReward : LobbyMessage
{
    protected override async Task HandleAsync()
    {
        _ = await ReadData<ReqAcquireArchiveEventQuestReward>();
        ResAcquireArchiveEventQuestReward response = new();
        await WriteDataAsync(response);
    }
}

[GameRequest("/archive/event-quest/stage/enter")]
[GameRequest("/archive/event-quest/enterstage")]
public class EnterArchiveEventQuestStage : LobbyMessage
{
    protected override async Task HandleAsync()
    {
        _ = await ReadData<ReqEnterArchiveEventQuestStage>();
        ResEnterArchiveEventQuestStage response = new();
        await WriteDataAsync(response);
    }
}

[GameRequest("/archive/event-quest/stage/clear")]
[GameRequest("/archive/event-quest/clearstage")]
public class ClearArchiveEventQuestStage : LobbyMessage
{
    protected override async Task HandleAsync()
    {
        _ = await ReadData<ReqClearArchiveEventQuestStage>();
        ResClearArchiveEventQuestStage response = new();
        await WriteDataAsync(response);
    }
}
