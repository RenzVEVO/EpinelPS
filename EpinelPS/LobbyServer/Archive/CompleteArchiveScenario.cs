using EpinelPS.Data;
using EpinelPS.Database;

namespace EpinelPS.LobbyServer.Archive;

[GameRequest("/archive/scenario/complete")]
public class CompleteScenario : LobbyMessage
{
    protected override async Task HandleAsync()
    {
        ReqCompleteArchiveScenario req = await ReadData<ReqCompleteArchiveScenario>(); // req has EventId, ScenarioId, DialogType fields
        int evid = req.EventId;
        string scenid = req.ScenarioId;
        int dialtyp = req.DialogType;

        User user = GetUser();

        // Ensure we are working with the user's EventInfo and not CompletedScenarios
        if (!user.EventInfo.TryGetValue(evid, out EventData? evt))
        {
            // Create a new EventData if the event doesn't exist
            evt = new EventData();
            user.EventInfo[evid] = evt;
        }

        // Ensure the CompletedScenarios list is initialized and add the ScenarioId
        if (!evt.CompletedScenarios.Contains(scenid))
        {
            evt.CompletedScenarios.Add(scenid);
        }
        // Also check if this completed scenario corresponds to an archive event quest
        var matchingQuest = GameData.Instance.archiveEventQuestRecords.Values
            .FirstOrDefault(q => q.EndScenarioId == scenid);
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
        // Prepare and send the response
        ResCompleteArchiveScenario response = new();
        await WriteDataAsync(response);
    }
}
