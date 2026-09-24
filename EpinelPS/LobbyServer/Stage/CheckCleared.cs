using EpinelPS.Data;

namespace EpinelPS.LobbyServer.Stage;

[GameRequest("/stage/checkclear")]
public class CheckCleared : LobbyMessage
{
    protected override async Task HandleAsync()
    {
        ReqCheckStageClear req = await ReadData<ReqCheckStageClear>();

        ResCheckStageClear response = new();
        User user = GetUser();

        HashSet<int> clearedStageIds = [];

        foreach (KeyValuePair<string, FieldInfoNew> fields in user.FieldInfoNew)
        {
            foreach (int stageId in fields.Value.CompletedStages)
            {
                if (req.StageIds.Contains(stageId))
                    clearedStageIds.Add(stageId);
            }
        }

        // Also check if any requested stages belong to completed subquests
        foreach (KeyValuePair<int, bool> subQuest in user.SubQuestData)
        {
            if (!subQuest.Value) continue;

            if (GameData.Instance.Subquests.TryGetValue(subQuest.Key, out SubQuestRecord? subQuestRecord))
            {
                if (subQuestRecord.ClearTrigger == Trigger.CampaignGroupClear)
                {
                    foreach (int stageId in req.StageIds)
                    {
                        if (clearedStageIds.Contains(stageId)) continue;
                        CampaignStageRecord? stageData = GameData.Instance.GetStageData(stageId);
                        if (stageData != null && stageData.GroupId == subQuestRecord.ClearConditionId)
                        {
                            clearedStageIds.Add(stageId);
                        }
                    }
                }
                else if (subQuestRecord.ClearTrigger == Trigger.CampaignClear)
                {
                    if (req.StageIds.Contains(subQuestRecord.ClearConditionId))
                    {
                        clearedStageIds.Add(subQuestRecord.ClearConditionId);
                    }
                }
            }
        }

        response.ClearedStageIds.AddRange(clearedStageIds);

        await WriteDataAsync(response);
    }
}
