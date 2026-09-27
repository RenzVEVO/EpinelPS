using EpinelPS.Data;

namespace EpinelPS.LobbyServer.Archive;

[GameRequest("/event-quest/list")]
[GameRequest("/archive/event-quest/list")]
[GameRequest("/event/quest/list")]
public class ListEventQuest : LobbyMessage
{
    protected override async Task HandleAsync()
    {
        ReqListEventQuest req = await ReadData<ReqListEventQuest>();
        ResListEventQuest response = new();

        var quests = GameData.Instance.archiveEventQuestRecords.Values
            .Where(q => q.EventQuestManagerId == req.EventQuestManagerTid)
            .OrderBy(q => q.Id);

        foreach (var q in quests)
        {
            response.EventQuests.Add(new NetEventQuestData
            {
                EventQuestId = q.Id,
                IsReceived = false
            });
        }

        await WriteDataAsync(response);
    }
}

[GameRequest("/event-quest/enterstage")]
[GameRequest("/event-quest/stage/enter")]
[GameRequest("/event/event-quest/enterstage")]
public class EnterEventQuestStage : LobbyMessage
{
    protected override async Task HandleAsync()
    {
        _ = await ReadData<ReqEnterEventQuestStage>();
        ResEnterEventQuestStage response = new();
        await WriteDataAsync(response);
    }
}

[GameRequest("/event-quest/clearstage")]
[GameRequest("/event-quest/stage/clear")]
[GameRequest("/event/event-quest/clearstage")]
public class ClearEventQuestStage : LobbyMessage
{
    protected override async Task HandleAsync()
    {
        _ = await ReadData<ReqClearEventQuestStage>();
        ResClearEventQuestStage response = new();
        await WriteDataAsync(response);
    }
}

[GameRequest("/event-quest/obtainreward")]
[GameRequest("/event-quest/reward/obtain")]
[GameRequest("/event-quest/reward")]
public class ObtainEventQuestReward : LobbyMessage
{
    protected override async Task HandleAsync()
    {
        _ = await ReadData<ReqObtainEventQuestReward>();
        ResObtainEventQuestReward response = new();
        await WriteDataAsync(response);
    }
}

[GameRequest("/event-quest/fin")]
[GameRequest("/event-quest/finish")]
public class FinEventQuest : LobbyMessage
{
    protected override async Task HandleAsync()
    {
        _ = await ReadData<ReqFinEventQuest>();
        ResFinEventQuest response = new();
        await WriteDataAsync(response);
    }
}
