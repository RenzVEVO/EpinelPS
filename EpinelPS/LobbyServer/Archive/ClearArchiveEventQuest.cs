using EpinelPS.Data;
using EpinelPS.Database;
using EpinelPS.Models;
using EpinelPS.Utils;

namespace EpinelPS.LobbyServer.Archive;

[GameRequest("/archive/event-quest/clear")]
public class ClearArchiveEventQuest : LobbyMessage
{
    protected override async Task HandleAsync()
    {
        ReqClearArchiveEventQuest req = await ReadData<ReqClearArchiveEventQuest>();
        User user = GetUser();
        ResClearArchiveEventQuest response = new();

        if (req.ArchiveEventQuestId != 0)
        {
            bool changed = false;
            if (GameData.Instance.archiveEventQuestRecords.TryGetValue(req.ArchiveEventQuestId, out var targetQuest))
            {
                var managerQuests = GameData.Instance.archiveEventQuestRecords.Values
                    .Where(q => q.EventQuestManagerId == targetQuest.EventQuestManagerId)
                    .OrderBy(q => q.Id)
                    .ToList();

                // Backfill all prerequisite quests in the chain up to targetQuest
                var curr = managerQuests.FirstOrDefault();
                while (curr != null)
                {
                    if (!user.ClearedArchiveEventQuestIds.Contains(curr.Id))
                    {
                        user.ClearedArchiveEventQuestIds.Add(curr.Id);
                        changed = true;
                    }

                    if (curr.Id == targetQuest.Id) break;
                    curr = curr.NextQuestId != 0 ? managerQuests.FirstOrDefault(q => q.Id == curr.NextQuestId) : null;
                }
            }
            else if (!user.ClearedArchiveEventQuestIds.Contains(req.ArchiveEventQuestId))
            {
                user.ClearedArchiveEventQuestIds.Add(req.ArchiveEventQuestId);
                changed = true;
            }

            if (changed)
            {
                JsonDb.Save();
            }
        }

        await WriteDataAsync(response);
    }
}

[GameRequest("/archive/event-quest/reset")]
public class ResetArchiveEventQuest : LobbyMessage
{
    protected override async Task HandleAsync()
    {
        ReqResetArchiveEventQuest req = await ReadData<ReqResetArchiveEventQuest>();
        User user = GetUser();
        ResResetArchiveEventQuest response = new();

        var arm = GameData.Instance.archiveRecordManagerTable.GetValueOrDefault(req.ArchiveRecordManagerId);
        if (arm != null)
        {
            int managerId = 0;
            var aeqm = GameData.Instance.archiveEventQuestManagerRecords.Values
                .FirstOrDefault(m => m.EventId == arm.RecordMainArchiveEventId);
            if (aeqm != null)
            {
                managerId = aeqm.Id;
            }
            else
            {
                Dictionary<int, int> map = new()
                {
                    { 130001, 10001 },
                    { 130002, 10002 },
                    { 130004, 10004 },
                    { 130005, 10005 },
                    { 130006, 10006 },
                    { 130007, 10007 },
                };
                map.TryGetValue(arm.RecordMainArchiveEventId, out managerId);
            }

            if (managerId != 0)
            {
                var quests = GameData.Instance.archiveEventQuestRecords.Values
                    .Where(q => q.EventQuestManagerId == managerId)
                    .Select(q => q.Id)
                    .ToHashSet();
                var stages = GameData.Instance.eventQuestStageRecords.Values
                    .Where(s => s.ArchiveEventQuestManagerId == managerId)
                    .Select(s => s.Id)
                    .ToHashSet();

                user.ClearedArchiveEventQuestIds.RemoveAll(id => quests.Contains(id));
                user.ClearedArchiveEventQuestStageIds.RemoveAll(id => stages.Contains(id));
                JsonDb.Save();
            }
        }

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
        ReqAcquireArchiveEventQuestReward req = await ReadData<ReqAcquireArchiveEventQuestReward>();
        User user = GetUser();
        ResAcquireArchiveEventQuestReward response = new();

        var arm = GameData.Instance.archiveRecordManagerTable.GetValueOrDefault(req.ArchiveRecordManagerId);
        if (arm != null && arm.EventQuestClearRewardId > 0)
        {
            response.Reward = RewardUtils.RegisterRewardsForUser(user, arm.EventQuestClearRewardId);
            JsonDb.Save();
        }

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
        ReqClearArchiveEventQuestStage req = await ReadData<ReqClearArchiveEventQuestStage>();
        User user = GetUser();
        ResClearArchiveEventQuestStage response = new();

        if (req.EventQuestStageId != 0 && !user.ClearedArchiveEventQuestStageIds.Contains(req.EventQuestStageId))
        {
            user.ClearedArchiveEventQuestStageIds.Add(req.EventQuestStageId);
            JsonDb.Save();
        }

        await WriteDataAsync(response);
    }
}
