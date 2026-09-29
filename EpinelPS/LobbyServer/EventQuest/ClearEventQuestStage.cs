using EpinelPS.Database;
using EpinelPS.Models;

namespace EpinelPS.LobbyServer.EventQuest;

[GameRequest("/eventquest/stage/clear")]
[GameRequest("/eventquest/clearstage")]
public class ClearEventQuestStage : LobbyMessage
{
    protected override async Task HandleAsync()
    {
        ReqClearEventQuestStage req = await ReadData<ReqClearEventQuestStage>();
        User user = GetUser();
        ResClearEventQuestStage response = new()
        {
            Reward = new NetRewardData(),
            UserLevelUpReward = new NetRewardData()
        };

        bool changed = false;
        if (req.StageId != 0 && !user.ClearedArchiveEventQuestStageIds.Contains(req.StageId))
        {
            user.ClearedArchiveEventQuestStageIds.Add(req.StageId);
            changed = true;
        }

        if (req.EventQuestId != 0 && !user.ClearedArchiveEventQuestIds.Contains(req.EventQuestId))
        {
            user.ClearedArchiveEventQuestIds.Add(req.EventQuestId);
            changed = true;
        }

        if (changed)
        {
            JsonDb.Save();
        }

        await WriteDataAsync(response);
    }
}
