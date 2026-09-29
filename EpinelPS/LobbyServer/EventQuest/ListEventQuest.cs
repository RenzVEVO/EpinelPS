using EpinelPS.Data;
using EpinelPS.Models;

namespace EpinelPS.LobbyServer.EventQuest;

[GameRequest("/eventquest/list")]
public class ListEventQuest : LobbyMessage
{
    private static readonly Dictionary<int, int> FallbackEventToManagerMap = new()
    {
        { 130001, 10001 }, // Fool's Day (Shifty)
        { 130002, 10002 }, // First Affection (Marian)
        { 130004, 10004 }, // Liar's End (Syuen)
        { 130005, 10005 }, // Nonsense Red (Red Hood)
        { 130006, 10006 }, // Out of Uniform
        { 130007, 10007 }, // Fool Burst Day (Mecha Shifty)
    };

    protected override async Task HandleAsync()
    {
        ReqListEventQuest req = await ReadData<ReqListEventQuest>();
        User user = GetUser();
        ResListEventQuest response = new();

        int requestedManagerTid = req.EventQuestManagerTid;
        int managerId = 0;

        if (requestedManagerTid != 0)
        {
            if (GameData.Instance.archiveEventQuestManagerRecords.ContainsKey(requestedManagerTid))
            {
                managerId = requestedManagerTid;
            }
            else if (GameData.Instance.archiveRecordManagerTable.TryGetValue(requestedManagerTid, out var arm))
            {
                var managerRec = GameData.Instance.archiveEventQuestManagerRecords.Values
                    .FirstOrDefault(m => m.EventId == arm.RecordMainArchiveEventId);
                if (managerRec != null) managerId = managerRec.Id;
                else if (FallbackEventToManagerMap.TryGetValue(arm.RecordMainArchiveEventId, out int mapped)) managerId = mapped;
            }
            else if (FallbackEventToManagerMap.TryGetValue(requestedManagerTid, out int mappedId))
            {
                managerId = mappedId;
            }
            else
            {
                managerId = requestedManagerTid;
            }
        }
        else if (user.ActivatedArchiveEventQuestId != 0)
        {
            if (GameData.Instance.archiveRecordManagerTable.TryGetValue(user.ActivatedArchiveEventQuestId, out var arm))
            {
                var managerRec = GameData.Instance.archiveEventQuestManagerRecords.Values
                    .FirstOrDefault(m => m.EventId == arm.RecordMainArchiveEventId);
                if (managerRec != null) managerId = managerRec.Id;
                else if (FallbackEventToManagerMap.TryGetValue(arm.RecordMainArchiveEventId, out int mapped)) managerId = mapped;
            }
            else if (FallbackEventToManagerMap.TryGetValue(user.ActivatedArchiveEventQuestId, out int mappedId))
            {
                managerId = mappedId;
            }
            else
            {
                managerId = user.ActivatedArchiveEventQuestId;
            }
        }

        user.ReceivedArchiveEventQuestRewardIds ??= [];

        if (managerId != 0)
        {
            var quests = GameData.Instance.GetArchiveEventQuestsForManager(managerId);
            foreach (var q in quests)
            {
                if (user.ClearedArchiveEventQuestIds.Contains(q.Id))
                {
                    bool isReceived = user.ReceivedArchiveEventQuestRewardIds.Contains(q.Id);
                    response.EventQuests.Add(new NetEventQuestData
                    {
                        EventQuestId = q.Id,
                        IsReceived = isReceived
                    });
                }
            }
        }
        else
        {
            foreach (int qId in user.ClearedArchiveEventQuestIds)
            {
                bool isReceived = user.ReceivedArchiveEventQuestRewardIds.Contains(qId);
                response.EventQuests.Add(new NetEventQuestData
                {
                    EventQuestId = qId,
                    IsReceived = isReceived
                });
            }
        }

        await WriteDataAsync(response);
    }
}
