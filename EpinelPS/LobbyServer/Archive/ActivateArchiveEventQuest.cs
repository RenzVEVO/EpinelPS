using EpinelPS.Data;
using EpinelPS.Database;
using EpinelPS.Utils;

namespace EpinelPS.LobbyServer.Archive;

[GameRequest("/archive/event-quest/activate")]
public class ActivateArchiveEventQuest : LobbyMessage
{
    protected override async Task HandleAsync()
    {
        ReqActivateArchiveEventQuest req = await ReadData<ReqActivateArchiveEventQuest>();
        User user = GetUser();
        ResActivateArchiveEventQuest response = new();

        ArchiveRecordManagerRecord record = GameData.Instance.archiveRecordManagerTable.GetValueOrDefault(req.ArchiveRecordManagerId)
            ?? throw new BadHttpRequestException($"Unknown archive event quest record {req.ArchiveRecordManagerId}", 400);

        if (GameConfig.Root.ArchiveUnlockAll != true && !user.UnlockedArchiveEventQuestIds.Contains(record.Id))
        {
            throw new BadHttpRequestException($"Archive event quest record {req.ArchiveRecordManagerId} is not unlocked", 400);
        }

        user.ActivatedArchiveEventQuestId = record.Id;

        // If Quest 1 has not been cleared yet, ensure any corrupted downstream quests from previous buggy sessions are purged
        Dictionary<int, int> fallbackMap = new()
        {
            { 130001, 10001 },
            { 130002, 10002 },
            { 130004, 10004 },
            { 130005, 10005 },
            { 130006, 10006 },
            { 130007, 10007 },
        };
        int managerId = 0;
        var managerRecord = GameData.Instance.archiveEventQuestManagerRecords.Values
            .FirstOrDefault(m => m.EventId == record.RecordMainArchiveEventId);
        if (managerRecord != null) managerId = managerRecord.Id;
        else fallbackMap.TryGetValue(record.RecordMainArchiveEventId, out managerId);

        if (managerId != 0)
        {
            var quests = GameData.Instance.archiveEventQuestRecords.Values
                .Where(q => q.EventQuestManagerId == managerId)
                .OrderBy(q => q.Id)
                .ToList();
            var q1 = quests.FirstOrDefault();
            if (q1 != null && !user.ClearedArchiveEventQuestIds.Contains(q1.Id))
            {
                user.ClearedArchiveEventQuestIds.RemoveAll(id => quests.Any(q => q.Id == id));
                user.ClearedArchiveEventQuestStageIds.RemoveAll(id => GameData.Instance.eventQuestStageRecords.Values.Any(s => s.ArchiveEventQuestManagerId == managerId && s.Id == id));
            }
        }

        JsonDb.Save();

        await WriteDataAsync(response);
    }
}
