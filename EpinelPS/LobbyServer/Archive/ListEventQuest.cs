using EpinelPS.Data;
using EpinelPS.Database;
using EpinelPS.Models;

namespace EpinelPS.LobbyServer.Archive;

[GameRequest("/event-quest/list")]
[GameRequest("/archive/event-quest/list")]
[GameRequest("/event/quest/list")]
public class ListEventQuest : LobbyMessage
{
    protected override async Task HandleAsync()
    {
        ReqListEventQuest req = await ReadData<ReqListEventQuest>();
        User user = GetUser();
        ResListEventQuest response = new();

        var quests = GameData.Instance.archiveEventQuestRecords.Values
            .Where(q => q.EventQuestManagerId == req.EventQuestManagerTid)
            .OrderBy(q => q.Id);

        foreach (var q in quests)
        {
            response.EventQuests.Add(new NetEventQuestData
            {
                EventQuestId = q.Id,
                IsReceived = user.ClearedArchiveEventQuestIds.Contains(q.Id)
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
        ReqClearEventQuestStage req = await ReadData<ReqClearEventQuestStage>();
        User user = GetUser();
        ResClearEventQuestStage response = new();

        if (req.StageId != 0 && !user.ClearedArchiveEventQuestStageIds.Contains(req.StageId))
        {
            user.ClearedArchiveEventQuestStageIds.Add(req.StageId);
            JsonDb.Save();
        }

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
        ReqFinEventQuest req = await ReadData<ReqFinEventQuest>();
        User user = GetUser();
        ResFinEventQuest response = new();

        if (req.EventQuestTid != 0 && !user.ClearedArchiveEventQuestIds.Contains(req.EventQuestTid))
        {
            user.ClearedArchiveEventQuestIds.Add(req.EventQuestTid);
            JsonDb.Save();
        }

        await WriteDataAsync(response);
    }
}
