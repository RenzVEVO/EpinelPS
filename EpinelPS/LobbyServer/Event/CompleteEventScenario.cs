﻿using EpinelPS.Data;
using EpinelPS.Database;

namespace EpinelPS.LobbyServer.Event;

[GameRequest("/event/scenario/complete")]
public class CompleteEventScenario : LobbyMessage
{
    protected override async Task HandleAsync()
    {
        ReqSetEventScenarioComplete req = await ReadData<ReqSetEventScenarioComplete>();
        User user = GetUser();

        if (user.EventInfo.TryGetValue(req.EventId, out EventData? evt))
        {
            if (!evt.CompletedScenarios.Contains(req.ScenarioId))
            {
                evt.CompletedScenarios.Add(req.ScenarioId);
            }
        }
        else
        {
            evt = new();
            evt.CompletedScenarios.Add(req.ScenarioId);
            user.EventInfo.Add(req.EventId, evt);
        }
        // Also check if this completed scenario corresponds to an archive event quest
        var matchingQuest = GameData.Instance.archiveEventQuestRecords.Values
            .FirstOrDefault(q => q.EndScenarioId == req.ScenarioId);
        if (matchingQuest != null)
        {
            if (!user.ClearedArchiveEventQuestIds.Contains(matchingQuest.Id))
            {
                user.ClearedArchiveEventQuestIds.Add(matchingQuest.Id);
            }

            // If this quest has a prerequisite intro quest (e.g. Quest 1 before Quest 2), ensure it is marked cleared as well
            var allManagerQuests = GameData.Instance.archiveEventQuestRecords.Values
                .Where(q => q.EventQuestManagerId == matchingQuest.EventQuestManagerId)
                .OrderBy(q => q.Id)
                .ToList();
            var q1 = allManagerQuests.FirstOrDefault();
            if (q1 != null && matchingQuest.Id == q1.NextQuestId && !user.ClearedArchiveEventQuestIds.Contains(q1.Id))
            {
                user.ClearedArchiveEventQuestIds.Add(q1.Id);
            }
        }

        JsonDb.Save();
        ResSetEventScenarioComplete response = new();

        // TODO reward

        await WriteDataAsync(response);
    }
}
