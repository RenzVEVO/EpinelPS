using EpinelPS.Data;
using EpinelPS.Database;
using EpinelPS.LobbyServer.Stage;
using EpinelPS.Utils;

namespace EpinelPS.LobbyServer.Mission;

/// <summary>
/// Reconciles mission progression and achievement triggers for users.
/// Ensures all earned milestones (character levels, user levels, limits, core breaks,
/// chapter clears, tower floors, and campaign quests) are properly recorded in the database
/// so they are completely achievable and claimable across all mission tabs.
/// </summary>
public static class MissionReconciler
{
    private static readonly object SyncLock = new();

    private static readonly Dictionary<CorporationTowerType, Trigger> TowerClearTriggers = new()
    {
        [CorporationTowerType.ELYSION] = Trigger.TowerElysionClear,
        [CorporationTowerType.MISSILIS] = Trigger.TowerMissilisClear,
        [CorporationTowerType.TETRA] = Trigger.TowerTetraClear,
        [CorporationTowerType.OVERSPEC] = Trigger.TowerOverspecClear,
        [CorporationTowerType.ALL] = Trigger.TowerBasicClear,
    };

    /// <summary>
    /// Evaluates account state and syncs any missing milestone triggers to the database.
    /// Safe to call frequently; checks existing triggers before writing to avoid redundant records.
    /// </summary>
    public static void ReconcileAll(User user, bool logToConsole = false)
    {
        if (user == null) return;

        lock (SyncLock)
        {
            try
            {
                using var context = GameContext.CreateNew();

                // 1. Reconcile maximum character level (Achievement: "Upgrade NIKKE to level 20... 40... 60")
                try { ReconcileCharacterLevelMax(user, context, logToConsole); }
                catch (Exception ex) { Logging.WriteLine($"[MissionReconciler] Step 1 CharLevel failed: {ex.Message}", LogType.Debug); }

                // 2. Reconcile player account level (Achievement: "Reach Account Level X")
                try { ReconcileUserLevel(user, context, logToConsole); }
                catch (Exception ex) { Logging.WriteLine($"[MissionReconciler] Step 2 UserLevel failed: {ex.Message}", LogType.Debug); }

                // 3. Reconcile character limit break and core break achievements
                try { ReconcileCharacterGradeAndCore(user, context, logToConsole); }
                catch (Exception ex) { Logging.WriteLine($"[MissionReconciler] Step 3 GradeAndCore failed: {ex.Message}", LogType.Debug); }

                // 4. Reconcile character bond / attractive levels
                try { ReconcileAttractiveLevelMax(user, context, logToConsole); }
                catch (Exception ex) { Logging.WriteLine($"[MissionReconciler] Step 4 Attractive failed: {ex.Message}", LogType.Debug); }

                // 5. Reconcile count of unique NIKKEs recruited
                try { ReconcileObtainCharacterNew(user, context, logToConsole); }
                catch (Exception ex) { Logging.WriteLine($"[MissionReconciler] Step 5 ObtainCharNew failed: {ex.Message}", LogType.Debug); }

                // 6. Reconcile campaign chapter clears (Normal and Hard mode)
                try { ReconcileChapterClears(user, context, logToConsole); }
                catch (Exception ex) { Logging.WriteLine($"[MissionReconciler] Step 6 ChapterClears failed: {ex.Message}", LogType.Debug); }

                // 7. Reconcile Tribe Tower floor progression
                try { ReconcileTowerClears(user, context, logToConsole); }
                catch (Exception ex) { Logging.WriteLine($"[MissionReconciler] Step 7 TowerClears failed: {ex.Message}", LogType.Debug); }

                // 8. Reconcile Main Campaign Quests (when static game data is initialized)
                try
                {
                    if (GameData.Instance != null && GameData.Instance.QuestDataRecords != null)
                    {
                        ClearStage.ReconcileMainQuests(user, logToConsole);
                    }
                }
                catch (Exception qEx)
                {
                    Logging.WriteLine($"[MissionReconciler] Main quest reconciliation skipped: {qEx.Message}", LogType.Debug);
                }
            }
            catch (Exception ex)
            {
                Logging.Warn($"[MissionReconciler] Failed during reconciliation for user {user.ID}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Ensures that CharacterLevelMax is recorded with the highest level owned.
    /// This directly solves the issue where achievements like "Upgrade NIKKE to level 20... 40... 60"
    /// were stuck or blocked for existing high-level characters.
    /// </summary>
    private static void ReconcileCharacterLevelMax(User user, GameContext context, bool logToConsole)
    {
        int maxLevel = user.GetMaxCharacterLevel();
        if (maxLevel < 20) return;

        int existingMax = context.Triggers
            .Where(t => t.UserId == user.ID && t.Type == Trigger.CharacterLevelMax)
            .Select(t => (int?)t.Value)
            .Max() ?? 0;

        if (existingMax < maxLevel)
        {
            user.AddTrigger(Trigger.CharacterLevelMax, maxLevel, 0, logToConsole);
            if (logToConsole)
            {
                Logging.WriteLine($"[MissionReconciler] Reconciled CharacterLevelMax to {maxLevel} for user {user.ID}", LogType.Info);
            }
        }
    }

    /// <summary>
    /// Reconciles player account level achievement triggers.
    /// </summary>
    private static void ReconcileUserLevel(User user, GameContext context, bool logToConsole)
    {
        int userLv = user.UserLevel > 0 ? user.UserLevel : 1;
        if (userLv < 10) return;

        int existingLevel = context.Triggers
            .Where(t => t.UserId == user.ID && t.Type == Trigger.UserLevel)
            .Select(t => (int?)t.Value)
            .Max() ?? 0;

        if (existingLevel < userLv)
        {
            user.AddTrigger(Trigger.UserLevel, userLv, 0, logToConsole);
            if (logToConsole)
            {
                Logging.WriteLine($"[MissionReconciler] Reconciled UserLevel to {userLv} for user {user.ID}", LogType.Info);
            }
        }
    }

    /// <summary>
    /// Reconciles maximum limit break (stars) and core breaks across owned characters.
    /// Handles both raw grade numbers and GradeCoreIds (10..12 for SR, 100..103 for SSR, 201..207 for Core).
    /// </summary>
    private static void ReconcileCharacterGradeAndCore(User user, GameContext context, bool logToConsole)
    {
        if (user.Characters.Count == 0) return;

        int stars = 0;
        int core = 0;
        foreach (var c in user.Characters)
        {
            int charStars = 0;
            int charCore = 0;
            if (c.Grade >= 201 && c.Grade <= 207)
            {
                charStars = 3;
                charCore = c.Grade - 200;
            }
            else if (c.Grade >= 100 && c.Grade <= 103)
            {
                charStars = c.Grade - 100;
            }
            else if (c.Grade >= 10 && c.Grade <= 12)
            {
                charStars = c.Grade - 10;
            }
            else if (c.Grade > 3 && c.Grade <= 10)
            {
                charStars = 3;
                charCore = c.Grade - 3;
            }
            else if (c.Grade >= 0 && c.Grade <= 3)
            {
                charStars = c.Grade;
            }

            stars = Math.Max(stars, charStars);
            core = Math.Max(core, charCore);
        }

        // Grade 2 (SR max limit break) and Grade 3 (SSR max limit break)
        if (stars >= 2)
        {
            int targetGrade = Math.Min(stars, 3);
            int existingGrade = context.Triggers
                .Where(t => t.UserId == user.ID && t.Type == Trigger.CharacterGradeMax)
                .Select(t => (int?)t.Value)
                .Max() ?? 0;

            if (existingGrade < targetGrade)
            {
                user.AddTrigger(Trigger.CharacterGradeMax, targetGrade, 0, logToConsole);
            }
        }

        // Core breaks (1..7)
        if (core > 0)
        {
            int targetCore = Math.Min(core, 7);
            int existingCore = context.Triggers
                .Where(t => t.UserId == user.ID && t.Type == Trigger.CharacterCore)
                .Select(t => (int?)t.Value)
                .Max() ?? 0;

            if (existingCore < targetCore)
            {
                user.AddTrigger(Trigger.CharacterCore, targetCore, 0, logToConsole);
            }
        }
    }

    /// <summary>
    /// Reconciles character bond (attractive) level achievements.
    /// </summary>
    private static void ReconcileAttractiveLevelMax(User user, GameContext context, bool logToConsole)
    {
        if (user.BondInfo == null || user.BondInfo.Count == 0) return;

        int maxBond = user.BondInfo.Max(b => b.Lv);
        if (maxBond < 2) return;

        int existingBond = context.Triggers
            .Where(t => t.UserId == user.ID && t.Type == Trigger.CharacterAttractiveLevelMax && t.ConditionId == 0)
            .Select(t => (int?)t.Value)
            .Max() ?? 0;

        if (existingBond < maxBond)
        {
            user.AddTrigger(Trigger.CharacterAttractiveLevelMax, maxBond, 0, logToConsole);
        }
    }

    /// <summary>
    /// Reconciles total count of unique NIKKEs acquired.
    /// </summary>
    private static void ReconcileObtainCharacterNew(User user, GameContext context, bool logToConsole)
    {
        if (user.Characters.Count == 0) return;

        int uniqueCount;
        if (GameData.Instance != null && GameData.Instance.CharacterTable != null)
        {
            uniqueCount = user.Characters
                .Select(c => GameData.Instance.CharacterTable.TryGetValue(c.Tid, out var rec) ? rec.NameCode : 0)
                .Where(nc => nc > 0)
                .Distinct()
                .Count();
        }
        else
        {
            uniqueCount = user.Characters.Select(c => c.Tid).Distinct().Count();
        }

        if (uniqueCount < 5) return;

        int existingCount = context.Triggers
            .Where(t => t.UserId == user.ID && t.Type == Trigger.ObtainCharacterNew)
            .Select(t => (int?)t.Value)
            .Max() ?? 0;

        if (existingCount < uniqueCount)
        {
            user.AddTrigger(Trigger.ObtainCharacterNew, uniqueCount, 0, logToConsole);
        }
    }

    /// <summary>
    /// Reconciles normal and hard chapter clear achievements.
    /// </summary>
    private static void ReconcileChapterClears(User user, GameContext context, bool logToConsole)
    {
        if (GameData.Instance == null || GameData.Instance.ChapterCampaignData == null) return;

        // Reconcile HardChapterClear triggers if the player has cleared Hard stages
        if (user.LastHardStageCleared > 0)
        {
            var existingHardChapters = context.Triggers
                .Where(t => t.UserId == user.ID && t.Type == Trigger.HardChapterClear)
                .Select(t => t.ConditionId)
                .ToHashSet();

            var hardChapters = GameData.Instance.ChapterCampaignData.Values;
            int maxChapter = hardChapters.Count > 0 ? hardChapters.Max(c => c.Chapter) : 0;

            for (int c = 1; c <= maxChapter; c++)
            {
                var hardStages = AdminCommands.GetHardMainStages(c);
                var bossStage = hardStages.LastOrDefault();
                if (bossStage != null && user.IsStageCompleted(bossStage.Id))
                {
                    if (!existingHardChapters.Contains(bossStage.ChapterId))
                    {
                        user.AddTrigger(Trigger.HardChapterClear, 1, bossStage.ChapterId, logToConsole);
                        existingHardChapters.Add(bossStage.ChapterId);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Reconciles Tribe Tower floor achievements across all manufacturer towers.
    /// </summary>
    private static void ReconcileTowerClears(User user, GameContext context, bool logToConsole)
    {
        if (user.TowerProgress.Count == 0 || GameData.Instance == null || GameData.Instance.towerTable == null) return;

        foreach (var (corpType, clearedFloor) in user.TowerProgress)
        {
            if (clearedFloor < 5) continue;
            if (!TowerClearTriggers.TryGetValue(corpType, out Trigger triggerType)) continue;

            var existingFloors = context.Triggers
                .Where(t => t.UserId == user.ID && t.Type == triggerType)
                .Select(t => t.ConditionId)
                .ToHashSet();

            // Check each milestone floor (multiples of 5) up to current cleared floor
            var towerStages = GameData.Instance.towerTable.Values
                .Where(t => t.Type == corpType && t.Floor <= clearedFloor && t.Floor % 5 == 0)
                .OrderBy(t => t.Floor);

            foreach (var stage in towerStages)
            {
                if (!existingFloors.Contains(stage.Id))
                {
                    user.AddTrigger(triggerType, 1, stage.Id, logToConsole);
                    existingFloors.Add(stage.Id);
                }
            }
        }
    }
}
