using EpinelPS.Data;
using EpinelPS.Database;
using EpinelPS.Models;
using EpinelPS.Utils;

namespace EpinelPS.LobbyServer.EventQuest;

[GameRequest("/eventquest/reward")]
[GameRequest("/eventquest/obtain")]
[GameRequest("/eventquest/reward/obtain")]
[GameRequest("/eventquest/obtainreward")]
[GameRequest("/eventquest/obtain-reward")]
public class ObtainEventQuestReward : LobbyMessage
{
    protected override async Task HandleAsync()
    {
        ReqObtainEventQuestReward req = await ReadData<ReqObtainEventQuestReward>();
        User user = GetUser();
        ResObtainEventQuestReward response = new()
        {
            Reward = new NetRewardData()
        };

        user.ReceivedArchiveEventQuestRewardIds ??= [];

        bool changed = false;
        foreach (int questTid in req.EventQuestTidList)
        {
            if (!user.ReceivedArchiveEventQuestRewardIds.Contains(questTid))
            {
                user.ReceivedArchiveEventQuestRewardIds.Add(questTid);
                changed = true;
            }
        }

        if (changed)
        {
            JsonDb.Save();
        }

        await WriteDataAsync(response);
    }
}
