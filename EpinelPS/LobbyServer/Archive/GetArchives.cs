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
            response.UnlockedArchiveRecordList.AddRange(user.UnlockedArchiveEventQuestIds.Where(id => !user.UnlockedArchiveRecordIds.Contains(id)));
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

        Dictionary<int, int> fallbackEventToManagerMap = new()
        {
            { 130001, 10001 }, // Fool's Day (Shifty)
            { 130002, 10002 }, // First Affection (Marian)
            { 130004, 10004 }, // Liar's End (Syuen)
            { 130005, 10005 }, // Nonsense Red (Red Hood)
            { 130006, 10006 }, // Out of Uniform
            { 130007, 10007 }, // Fool Burst Day (Mecha Shifty)
        };

        foreach (var record in eventQuestRecords)
        {
            if (GameConfig.Root.ArchiveUnlockAll == true || user.UnlockedArchiveEventQuestIds.Contains(record.Id))
            {
                int managerId = 0;
                var managerRecord = GameData.Instance.archiveEventQuestManagerRecords.Values
                    .FirstOrDefault(m => m.EventId == record.RecordMainArchiveEventId);
                if (managerRecord != null)
                {
                    managerId = managerRecord.Id;
                }
                else if (fallbackEventToManagerMap.TryGetValue(record.RecordMainArchiveEventId, out int mappedId))
                {
                    managerId = mappedId;
                }

                if (managerId == 0) continue;

                var mapping = new ResGetArchiveRecord.Types.ArchiveEventQuestData.Types.EventQuestMappingData
                {
                    ArchiveRecordManagerId = record.Id,
                };

                // 1. Quests for this manager (pre-indexed O(1))
                var quests = GameData.Instance.GetArchiveEventQuestsForManager(managerId);

                // 2. Cleared quests
                var clearedQuests = quests
                    .Where(q => user.ClearedArchiveEventQuestIds.Contains(q.Id))
                    .Select(q => q.Id)
                    .ToList();
                mapping.CumulativeArchiveEventQuestIdList.AddRange(clearedQuests);

                // 3. Current quest in chain
                ArchiveEventQuestRecord_Raw? currentQuest = quests.FirstOrDefault();
                while (currentQuest != null && user.ClearedArchiveEventQuestIds.Contains(currentQuest.Id))
                {
                    if (currentQuest.NextQuestId != 0 && currentQuest.ConditionType != Category.End)
                    {
                        currentQuest = quests.FirstOrDefault(q => q.Id == currentQuest.NextQuestId);
                    }
                    else
                    {
                        currentQuest = null; // Reached end of quest chain
                    }
                }

                if (currentQuest != null)
                {
                    mapping.CurrentArchiveEventQuestIdList.Add(currentQuest.Id);
                }


                // 4. Quest list: all quests for this manager
                mapping.EventQuestIdList.AddRange(quests.Select(q => q.Id));
                // 2. Stages for this manager (pre-indexed O(1))
                var stages = GameData.Instance.GetEventQuestStagesForArchiveManager(managerId);

                foreach (var s in stages)
                {
                    bool isCleared = user.ClearedArchiveEventQuestStageIds.Contains(s.Id);
                    var stageState = isCleared
                        ? ResGetArchiveRecord.Types.ArchiveEventQuestData.Types.EventQuestMappingData.Types.StageData.Types.StageState.Cleared
                        : ResGetArchiveRecord.Types.ArchiveEventQuestData.Types.EventQuestMappingData.Types.StageData.Types.StageState.Entered;

                    mapping.EventQuestStageList.Add(new ResGetArchiveRecord.Types.ArchiveEventQuestData.Types.EventQuestMappingData.Types.StageData
                    {
                        StageId = s.Id,
                        State = stageState
                    });

                    if (isCleared)
                    {
                        mapping.CumulativeArchiveEventQuestStageList.Add(new ResGetArchiveRecord.Types.ArchiveEventQuestData.Types.EventQuestMappingData.Types.StageData
                        {
                            StageId = s.Id,
                            State = ResGetArchiveRecord.Types.ArchiveEventQuestData.Types.EventQuestMappingData.Types.StageData.Types.StageState.Cleared
                        });
                    }
                }

                // 5. Current stage if any: only populate when current quest is at a stage clear condition
                if (currentQuest != null)
                {
                    EventQuestStageRecord? activeStage = null;
                    if (currentQuest.ConditionType == Category.EventQuestStageClear)
                    {
                        activeStage = stages.FirstOrDefault(s => s.Id == currentQuest.ConditionValue && !user.ClearedArchiveEventQuestStageIds.Contains(s.Id));
                    }
                    else if (currentQuest.ConditionType == Category.EventQuestStageGroupClear)
                    {
                        activeStage = stages.FirstOrDefault(s => s.GroupId == currentQuest.ConditionValue && !user.ClearedArchiveEventQuestStageIds.Contains(s.Id));
                    }

                    if (activeStage != null)
                    {
                        mapping.CurrentArchiveEventQuestStageList.Add(new ResGetArchiveRecord.Types.ArchiveEventQuestData.Types.EventQuestMappingData.Types.StageData
                        {
                            StageId = activeStage.Id,
                            State = ResGetArchiveRecord.Types.ArchiveEventQuestData.Types.EventQuestMappingData.Types.StageData.Types.StageState.Entered
                        });
                    }
                }
                response.ArchiveEventQuest.EventQuestMappingList.Add(mapping);
            }
        }
        await WriteDataAsync(response);
    }
}
