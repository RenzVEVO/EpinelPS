using EpinelPS.Data;
using EpinelPS.Database;
using EpinelPS.Utils;

namespace EpinelPS.LobbyServer.Archive;

[GameRequest("/archive/event-quest/activate")]
[GameRequest("/event-quest/activate")]
[GameRequest("/event/event-quest/activate")]
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
        JsonDb.Save();

        await WriteDataAsync(response);
    }
}
