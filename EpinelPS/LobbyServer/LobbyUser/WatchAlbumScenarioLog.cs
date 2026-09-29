using EpinelPS.Data;
using EpinelPS.Database;
using EpinelPS.Utils;

namespace EpinelPS.LobbyServer.LobbyUser;

[GameRequest("/user/scenario/watchalbumlog")]
public class WatchAlbumScenarioLog : LobbyMessage
{
    protected override async Task HandleAsync()
    {
        ReqWatchAlbumScenarioLog req = await ReadData<ReqWatchAlbumScenarioLog>();
        Logging.WriteLine($"[WatchAlbumScenarioLog] User watched album resource {req.AlbumResourceId} (type: {req.ScenarioDataType})", LogType.Info);
        User user = GetUser();
        if (GameData.Instance.albumResourceRecords.TryGetValue(req.AlbumResourceId, out var albumRec) && !string.IsNullOrEmpty(albumRec.ScenarioGroupId))
        {
            var matchingQuest = GameData.Instance.archiveEventQuestRecords.Values
                .FirstOrDefault(q => q.EndScenarioId == albumRec.ScenarioGroupId);
            if (matchingQuest != null)
            {
                bool changed = false;
                if (!user.ClearedArchiveEventQuestIds.Contains(matchingQuest.Id))
                {
                    user.ClearedArchiveEventQuestIds.Add(matchingQuest.Id);
                    changed = true;
                }

                var allManagerQuests = GameData.Instance.archiveEventQuestRecords.Values
                    .Where(q => q.EventQuestManagerId == matchingQuest.EventQuestManagerId)
                    .OrderBy(q => q.Id)
                    .ToList();
                var q1 = allManagerQuests.FirstOrDefault();
                if (q1 != null && matchingQuest.Id == q1.NextQuestId && !user.ClearedArchiveEventQuestIds.Contains(q1.Id))
                {
                    user.ClearedArchiveEventQuestIds.Add(q1.Id);
                    changed = true;
                }

                if (changed)
                {
                    JsonDb.Save();
                }
            }
        }

        ResWatchAlbumScenarioLog response = new();
        await WriteDataAsync(response);
    }
}
