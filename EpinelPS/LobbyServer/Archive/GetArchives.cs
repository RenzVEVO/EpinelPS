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

        foreach (var record in eventQuestRecords)
        {
            if (GameConfig.Root.ArchiveUnlockAll == true || user.UnlockedArchiveEventQuestIds.Contains(record.Id))
            {
                int managerId = ArchiveEventQuestHelper.ResolveManagerId(record.Id);
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
                var currentQuest = ArchiveEventQuestHelper.GetCurrentActiveQuest(user, managerId);
                if (currentQuest != null)
                {
                    mapping.CurrentArchiveEventQuestIdList.Add(currentQuest.Id);
                }

                // 4. Unlocked quests in client list: strictly only cleared quests plus the active current quest
                mapping.EventQuestIdList.AddRange(clearedQuests);
                if (currentQuest != null && !mapping.EventQuestIdList.Contains(currentQuest.Id))
                {
                    mapping.EventQuestIdList.Add(currentQuest.Id);
                }

                // 5. Stages for this manager (pre-indexed O(1))
                var stages = GameData.Instance.GetEventQuestStagesForArchiveManager(managerId);

                foreach (var s in stages)
                {
                    bool isCleared = user.ClearedArchiveEventQuestStageIds.Contains(s.Id);
                    bool isSpawned = isCleared || s.SpawnConditionEventQuestId == 0 ||
                        user.ClearedArchiveEventQuestIds.Contains(s.SpawnConditionEventQuestId) ||
                        (currentQuest != null && currentQuest.Id >= s.SpawnConditionEventQuestId);

                    if (isCleared)
                    {
                        mapping.CumulativeArchiveEventQuestStageList.Add(new ResGetArchiveRecord.Types.ArchiveEventQuestData.Types.EventQuestMappingData.Types.StageData
                        {
                            StageId = s.Id,
                            State = ResGetArchiveRecord.Types.ArchiveEventQuestData.Types.EventQuestMappingData.Types.StageData.Types.StageState.Cleared
                        });
                    }

                    if (isSpawned)
                    {
                        var stageState = isCleared
                            ? ResGetArchiveRecord.Types.ArchiveEventQuestData.Types.EventQuestMappingData.Types.StageData.Types.StageState.Cleared
                            : ResGetArchiveRecord.Types.ArchiveEventQuestData.Types.EventQuestMappingData.Types.StageData.Types.StageState.Entered;

                        mapping.EventQuestStageList.Add(new ResGetArchiveRecord.Types.ArchiveEventQuestData.Types.EventQuestMappingData.Types.StageData
                        {
                            StageId = s.Id,
                            State = stageState
                        });
                    }
                }

                // 6. Current stage(s): populate when current quest is at a stage clear condition
                if (currentQuest != null)
                {
                    if (currentQuest.ConditionType == Category.EventQuestStageClear)
                    {
                        var activeStage = stages.FirstOrDefault(s => s.Id == currentQuest.ConditionValue && !user.ClearedArchiveEventQuestStageIds.Contains(s.Id));
                        if (activeStage != null)
                        {
                            mapping.CurrentArchiveEventQuestStageList.Add(new ResGetArchiveRecord.Types.ArchiveEventQuestData.Types.EventQuestMappingData.Types.StageData
                            {
                                StageId = activeStage.Id,
                                State = ResGetArchiveRecord.Types.ArchiveEventQuestData.Types.EventQuestMappingData.Types.StageData.Types.StageState.Entered
                            });
                        }
                    }
                    else if (currentQuest.ConditionType == Category.EventQuestStageGroupClear)
                    {
                        var activeStages = stages.Where(s => s.GroupId == currentQuest.ConditionValue && !user.ClearedArchiveEventQuestStageIds.Contains(s.Id));
                        foreach (var s in activeStages)
                        {
                            mapping.CurrentArchiveEventQuestStageList.Add(new ResGetArchiveRecord.Types.ArchiveEventQuestData.Types.EventQuestMappingData.Types.StageData
                            {
                                StageId = s.Id,
                                State = ResGetArchiveRecord.Types.ArchiveEventQuestData.Types.EventQuestMappingData.Types.StageData.Types.StageState.Entered
                            });
                        }
                    }
                }
                response.ArchiveEventQuest.EventQuestMappingList.Add(mapping);
            }
        }
        await WriteDataAsync(response);
    }
}
