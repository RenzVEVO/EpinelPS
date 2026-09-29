using EpinelPS.Data;
using EpinelPS.Database;
using EpinelPS.Models;

namespace EpinelPS.LobbyServer.EventQuest;

[GameRequest("/eventquest/fin")]
public class FinEventQuest : LobbyMessage
{
    protected override async Task HandleAsync()
    {
        ReqFinEventQuest req = await ReadData<ReqFinEventQuest>();
        User user = GetUser();
        ResFinEventQuest response = new();

        if (req.EventQuestTid != 0)
        {
            bool changed = false;
            if (!user.ClearedArchiveEventQuestIds.Contains(req.EventQuestTid))
            {
                user.ClearedArchiveEventQuestIds.Add(req.EventQuestTid);
                changed = true;
            }

            // Also check if this quest is in a manager chain and ensure preceding quests are marked cleared
            if (GameData.Instance.archiveEventQuestRecords.TryGetValue(req.EventQuestTid, out var targetQuest))
            {
                var managerQuests = GameData.Instance.GetArchiveEventQuestsForManager(targetQuest.EventQuestManagerId);
                foreach (var q in managerQuests)
                {
                    if (q.Id < targetQuest.Id && !user.ClearedArchiveEventQuestIds.Contains(q.Id))
                    {
                        user.ClearedArchiveEventQuestIds.Add(q.Id);
                        changed = true;
                    }
                }
            }

            if (changed)
            {
                JsonDb.Save();
            }
        }

        await WriteDataAsync(response);
    }
}
