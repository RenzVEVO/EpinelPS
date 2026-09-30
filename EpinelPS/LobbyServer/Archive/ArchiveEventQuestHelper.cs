using EpinelPS.Data;
using EpinelPS.Database;
using EpinelPS.Models;
using EpinelPS.Utils;

namespace EpinelPS.LobbyServer.Archive;

public static class ArchiveEventQuestHelper
{
    private static readonly Dictionary<int, int> FallbackEventToManagerMap = new()
    {
        { 130001, 10001 }, // Fool's Day (Shifty)
        { 130002, 10002 }, // First Affection (Marian)
        { 130004, 10004 }, // Liar's End (Syuen)
        { 130005, 10005 }, // Nonsense Red (Red Hood)
        { 130006, 10006 }, // Out of Uniform
        { 130007, 10007 }, // Fool Burst Day (Mecha Shifty)
    };

    /// <summary>
    /// Resolves the EventQuestManagerId (e.g. 10001, 10002... 10007) given an ArchiveRecordManagerId
    /// or EventId.
    /// </summary>
    public static int ResolveManagerId(int id)
    {
        if (id == 0) return 0;

        if (GameData.Instance.archiveEventQuestManagerRecords.ContainsKey(id))
        {
            return id;
        }

        if (GameData.Instance.archiveRecordManagerTable.TryGetValue(id, out var arm))
        {
            var managerRec = GameData.Instance.archiveEventQuestManagerRecords.Values
                .FirstOrDefault(m => m.EventId == arm.RecordMainArchiveEventId);
            if (managerRec != null) return managerRec.Id;
            if (FallbackEventToManagerMap.TryGetValue(arm.RecordMainArchiveEventId, out int mapped)) return mapped;
        }

        if (FallbackEventToManagerMap.TryGetValue(id, out int mappedDirect))
        {
            return mappedDirect;
        }

        return id;
    }

    /// <summary>
    /// Gets the current active uncleared quest in sequential order for a manager.
    /// </summary>
    public static ArchiveEventQuestRecord_Raw? GetCurrentActiveQuest(User user, int managerId)
    {
        if (managerId == 0) return null;

        var quests = GameData.Instance.GetArchiveEventQuestsForManager(managerId);
        ArchiveEventQuestRecord_Raw? current = quests.FirstOrDefault();
        while (current != null && user.ClearedArchiveEventQuestIds.Contains(current.Id))
        {
            if (current.NextQuestId != 0 && current.ConditionType != Category.End)
            {
                current = quests.FirstOrDefault(q => q.Id == current.NextQuestId);
            }
            else
            {
                current = null;
            }
        }
        return current;
    }

    /// <summary>
    /// Handles progression when a stage battle is cleared.
    /// Records stage completion and marks the active quest cleared if and only if
    /// its condition (single stage or all stages in group) is fully satisfied.
    /// </summary>
    public static void OnStageCleared(User user, int stageId)
    {
        if (stageId == 0) return;
        bool changed = false;

        if (!user.ClearedArchiveEventQuestStageIds.Contains(stageId))
        {
            user.ClearedArchiveEventQuestStageIds.Add(stageId);
            changed = true;
        }

        if (user.ActivatedArchiveEventQuestId != 0)
        {
            int managerId = ResolveManagerId(user.ActivatedArchiveEventQuestId);
            if (managerId != 0)
            {
                var currentQuest = GetCurrentActiveQuest(user, managerId);
                if (currentQuest != null)
                {
                    if (currentQuest.ConditionType == Category.EventQuestStageClear && currentQuest.ConditionValue == stageId)
                    {
                        if (!user.ClearedArchiveEventQuestIds.Contains(currentQuest.Id))
                        {
                            user.ClearedArchiveEventQuestIds.Add(currentQuest.Id);
                            changed = true;
                        }
                    }
                    else if (currentQuest.ConditionType == Category.EventQuestStageGroupClear)
                    {
                        var groupStages = GameData.Instance.eventQuestStageRecords.Values
                            .Where(s => s.GroupId == currentQuest.ConditionValue)
                            .ToList();

                        if (groupStages.Count > 0 && groupStages.All(s => user.ClearedArchiveEventQuestStageIds.Contains(s.Id)))
                        {
                            if (!user.ClearedArchiveEventQuestIds.Contains(currentQuest.Id))
                            {
                                user.ClearedArchiveEventQuestIds.Add(currentQuest.Id);
                                changed = true;
                            }
                        }
                    }
                }
            }
        }

        if (changed)
        {
            JsonDb.Save();
        }
    }

    /// <summary>
    /// Authoritatively records a quest clear claim while maintaining chain integrity.
    /// If the client claims the next quest in an intro sequence or skips prior steps,
    /// synchronizes preceding quests to eliminate softlocks.
    /// </summary>
    public static void OnArchiveQuestCleared(User user, int questId)
    {
        if (questId == 0) return;
        bool changed = false;

        if (GameData.Instance.archiveEventQuestRecords.TryGetValue(questId, out var targetQuest))
        {
            var managerQuests = GameData.Instance.GetArchiveEventQuestsForManager(targetQuest.EventQuestManagerId);
            var expectedCurrent = GetCurrentActiveQuest(user, targetQuest.EventQuestManagerId);

            if (expectedCurrent != null)
            {
                if (questId == expectedCurrent.Id)
                {
                    if (!user.ClearedArchiveEventQuestIds.Contains(questId))
                    {
                        user.ClearedArchiveEventQuestIds.Add(questId);
                        changed = true;
                    }
                }
                else if (questId == expectedCurrent.NextQuestId)
                {
                    // Intro transition or paired claim: auto-clear prerequisite expectedCurrent
                    if (!user.ClearedArchiveEventQuestIds.Contains(expectedCurrent.Id))
                    {
                        user.ClearedArchiveEventQuestIds.Add(expectedCurrent.Id);
                    }
                    if (!user.ClearedArchiveEventQuestIds.Contains(questId))
                    {
                        user.ClearedArchiveEventQuestIds.Add(questId);
                    }
                    changed = true;
                }
                else if (questId > expectedCurrent.Id)
                {
                    Logging.WriteLine($"[ArchiveQuest] Synchronizing quest chain up to {questId} (expected {expectedCurrent.Id})", LogType.Warning);
                    foreach (var q in managerQuests)
                    {
                        if (q.Id <= questId && !user.ClearedArchiveEventQuestIds.Contains(q.Id))
                        {
                            user.ClearedArchiveEventQuestIds.Add(q.Id);
                            changed = true;
                        }
                    }
                }
            }
            else
            {
                if (!user.ClearedArchiveEventQuestIds.Contains(questId))
                {
                    user.ClearedArchiveEventQuestIds.Add(questId);
                    changed = true;
                }
            }
        }
        else if (!user.ClearedArchiveEventQuestIds.Contains(questId))
        {
            user.ClearedArchiveEventQuestIds.Add(questId);
            changed = true;
        }

        if (changed)
        {
            JsonDb.Save();
        }
    }
}
