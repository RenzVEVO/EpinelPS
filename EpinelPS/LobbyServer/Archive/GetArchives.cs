using EpinelPS.Data;
using EpinelPS.Utils;

namespace EpinelPS.LobbyServer.Archive;

[GameRequest("/archive/get")]
public class GetArchives : LobbyMessage
{
    protected override async Task HandleAsync()
    {
        _ = await ReadData<ReqGetArchiveRecord>();

        ResGetArchiveRecord response = new();
        List<ArchiveRecordManagerRecord> records = [.. GameData.Instance.archiveRecordManagerTable.Values];
        List<int> allIds = [.. records.Select(record => record.Id)];

        // This is the archive catalog, not the user's unlock state.
        response.ArchiveRecordManagerList.AddRange(allIds);

        User user = GetUser();
        if (GameConfig.Root.ArchiveUnlockAll == true)
        {
            response.UnlockedArchiveRecordList.AddRange(allIds);
        }
        else
        {
            response.UnlockedArchiveRecordList.AddRange(user.UnlockedArchiveRecordIds);
        }

        List<ArchiveRecordManagerRecord> eventQuestRecords = [.. records
            .Where(record => record.RecordType == ArchiveRecordType.EventQuest)];

        response.ArchiveEventQuest = new()
        {
            ActivatedArchiveRecordManagerId = user.ActivatedArchiveEventQuestId
        };

        if (GameConfig.Root.ArchiveUnlockAll == true)
        {
            response.ArchiveEventQuest.UnlockedArchiveRecordManagerEventQuestIdList
                .AddRange(eventQuestRecords.Select(record => record.Id));
        }
        else
        {
            response.ArchiveEventQuest.UnlockedArchiveRecordManagerEventQuestIdList
                .AddRange(user.UnlockedArchiveEventQuestIds);
        }

        foreach (var record in eventQuestRecords)
        {
            if (GameConfig.Root.ArchiveUnlockAll == true || user.UnlockedArchiveEventQuestIds.Contains(record.Id))
            {
                var mapping = new ResGetArchiveRecord.Types.ArchiveEventQuestData.Types.EventQuestMappingData
                {
                    ArchiveRecordManagerId = record.Id,
                };

                int managerId = record.Id / 100;
                var quests = GameData.Instance.archiveEventQuestRecords.Values
                    .Where(q => q.EventQuestManagerId == managerId)
                    .OrderBy(q => q.Id)
                    .ToList();

                mapping.EventQuestIdList.AddRange(quests.Select(q => q.Id));
                if (quests.Count > 0)
                {
                    mapping.CurrentArchiveEventQuestIdList.Add(quests.First().Id);
                }

                response.ArchiveEventQuest.EventQuestMappingList.Add(mapping);
            }
        }
        await WriteDataAsync(response);
    }
}
