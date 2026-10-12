using EpinelPS.Data;
using EpinelPS.Database;
using EpinelPS.LobbyServer.Event;
using EpinelPS.Utils;
using System.Collections.Concurrent;

namespace EpinelPS.Utils;

public class GachaUtils
{
    private static readonly List<int> sickPullsExclusionList = [2500601]; // Add more IDs as needed
    private static readonly ConcurrentDictionary<int, List<GachaGradeProbRecord>> _gradeProbsByGroup = new();
    private static readonly ConcurrentDictionary<int, List<GachaListProbRecord>> _listProbsByGroup = new();

    /// <summary>
    /// Overall SSR chance on the friend banner (0.055 = 5.5%). Only raised, never lowered.
    /// </summary>
    private const double LimitedSsrRate = 0.055;

    /// <summary>
    /// Same as LimitedSsrRate but when user.sickpulls is on. Ignores the normal rate (1.0 = every pull is SSR).
    /// </summary>
    private const double LimitedSsrRateSickPulls = 1.0;

    private static readonly ConcurrentDictionary<OriginalRareType, HashSet<int>> _standardPools = new();
    private static Dictionary<int, GachaListProbRecord>? _limitedUnitRows;
    private static readonly object _limitedUnitRowsLock = new();

    /// <summary>
    /// Units the standard banner can give at this rarity (for SSR: the wishlistable units).
    /// </summary>
    private static HashSet<int> GetStandardPool(OriginalRareType rare)
    {
        return _standardPools.GetOrAdd(rare, r =>
        {
            var data = GameData.Instance;
            var standardBanner = data.gachaTypes.Values.FirstOrDefault(g => g.Type == GachaPremiumType.GachaPremium);
            if (standardBanner == null) return [];
            return data.GachaGradeProb.Values
                .Where(p => p.GroupId == standardBanner.GradeProbId && p.Rare == r)
                .SelectMany(p => data.GachaListProb.Values.Where(l => l.GroupId == p.GachaListId).Select(l => l.GachaId))
                .ToHashSet();
        });
    }

    /// <summary>
    /// Limited unit = not part of the standard pool of its own rarity.
    /// </summary>
    private static bool IsLimitedUnit(int characterId)
    {
        return GameData.Instance.CharacterTable.TryGetValue(characterId, out var ch)
               && !GetStandardPool(ch.OriginalRare).Contains(characterId);
    }

    /// <summary>
    /// Rate-up units of the currently active event banners, unordered.
    /// </summary>
    public static HashSet<int> GetActiveRateUpIds(User user) => [.. GetActiveRateUpIdsOrdered(user)];

    /// <summary>
    /// Rate-up units of the currently active event banners, in display order:
    /// newest event first (highest pickup gacha event id), and inside an event pickup units before selectup units.
    /// Chain: banner event -> child events -> shared banner resource table -> pickup gacha event -> GachaType.PickupCharGroupId -> GachaListProb.GroupId
    /// Selectup banners use GachaSelectupListTable (all choices, not the user's pick).
    /// </summary>
    public static List<int> GetActiveRateUpIdsOrdered(User user)
    {
        var data = GameData.Instance;
        var eventManagers = data.eventManagers.Values;
        var perBanner = new List<(int EventKey, List<int> Units)>();

        foreach (var banner in EventHelper.GetLobbyPrivateBannerData(user))
        {
            var resourceTables = eventManagers
                .Where(em => (em.SetField == banner.EventId || em.ParentsEventId == banner.EventId)
                             && em.EventBannerResourceTable != null
                             && em.EventBannerResourceTable.StartsWith("event_"))
                .Select(em => em.EventBannerResourceTable)
                .ToHashSet();

            var gachaEventIds = eventManagers
                .Where(em => em.EventSystemType == EventSystemType.PickupGachaEvent
                             && em.EventBannerResourceTable != null
                             && resourceTables.Contains(em.EventBannerResourceTable))
                .Select(em => em.Id)
                .ToHashSet();

            var units = new List<int>();
            foreach (var gacha in data.gachaTypes.Values
                         .Where(g => gachaEventIds.Contains(g.EventId))
                         .OrderBy(g => g.Type == GachaPremiumType.GachaSelectup ? 1 : 0) // pickup first, then selectup
                         .ThenBy(g => g.Id))
            {
                if (gacha.Type == GachaPremiumType.GachaSelectup)
                {
                    units.AddRange(data.GachaSelectupListTable.Values
                        .Where(s => s.GachaTypeId == gacha.Id)
                        .OrderBy(s => s.Id)
                        .Select(s => s.CharacterId));
                }
                else if (gacha.Type == GachaPremiumType.GachaPickup && gacha.PickupCharGroupId != 0)
                {
                    units.AddRange(data.GachaListProb.Values
                        .Where(l => l.GroupId == gacha.PickupCharGroupId && l.GachaSubType == GachaSubType.PickupCharacter)
                        .OrderBy(l => l.Id)
                        .Select(l => l.GachaId));
                }
            }

            perBanner.Add((gachaEventIds.Count == 0 ? 0 : gachaEventIds.Max(), units));
        }

        var result = new List<int>();
        var seen = new HashSet<int>();
        foreach (var entry in perBanner.OrderByDescending(b => b.EventKey)) // newest event first
        {
            foreach (var id in entry.Units)
            {
                if (seen.Add(id))
                    result.Add(id);
            }
        }
        return result;
    }

    /// <summary>
    /// Every unit that appears as rate-up/limited in the gacha lists and is outside the standard pool of its rarity.
    /// One row per unit, keeps the rate-up weight. Computed once.
    /// </summary>
    private static Dictionary<int, GachaListProbRecord> GetAllLimitedUnitRows()
    {
        if (_limitedUnitRows != null) return _limitedUnitRows;
        lock (_limitedUnitRowsLock)
        {
            return _limitedUnitRows ??= GameData.Instance.GachaListProb.Values
                .Where(r => r.GachaType == GachaCategory.Character
                            && (r.GachaSubType is GachaSubType.PickupCharacter or GachaSubType.LimitedCharacter)
                            && IsLimitedUnit(r.GachaId))
                .GroupBy(r => r.GachaId)
                .ToDictionary(g => g.Key, g => g.First());
        }
    }

    /// <summary>
    /// Candidates for the limited pool of the given rarity:
    /// all limited units (outside the standard pool) plus the rate-up units of the active event banners (even permanent ones).
    /// Keys and Ids are negative so they can never collide with real GachaListProb record ids.
    /// </summary>
    private static Dictionary<int, GachaListProbRecord> GetLimitedCandidates(User user, OriginalRareType rare, int defaultProb)
    {
        var data = GameData.Instance;
        var result = new Dictionary<int, GachaListProbRecord>();
        var limitedRows = GetAllLimitedUnitRows();

        var ids = new HashSet<int>(limitedRows.Keys);
        ids.UnionWith(GetActiveRateUpIds(user));

        foreach (var id in ids)
        {
            if (!data.CharacterTable.TryGetValue(id, out var ch) || ch.OriginalRare != rare)
                continue;

            limitedRows.TryGetValue(id, out var row);
            row ??= data.GachaListProb.Values.FirstOrDefault(r =>
                r.GachaId == id && r.GachaType == GachaCategory.Character &&
                (r.GachaSubType is GachaSubType.PickupCharacter or GachaSubType.LimitedCharacter));

            result[-id] = new GachaListProbRecord
            {
                Id = -id,
                GroupId = row?.GroupId ?? 0,
                GachaType = GachaCategory.Character,
                GachaSubType = GachaSubType.None,
                GachaId = id,
                Prob = row?.Prob ?? defaultProb,
            };
        }
        return result;
    }

    public static List<CharacterRecord> ExecuteGachaPull(GachaTypeRecord gachaType, int numberOfPulls, User user)
    {
        List<CharacterRecord> wishlistCharacters = user.GetWishlistCharacters(gachaType.Id);
        List<CharacterRecord> selectedCharacters = [];

        if (user.sickpulls && gachaType.Type != GachaPremiumType.GachaFriend)
        {
            List<CharacterRecord> entireallCharacterData = [.. GameData.Instance.CharacterTable.Values];
            List<CharacterRecord> allCharacterData = [.. entireallCharacterData.GroupBy(c => c.NameCode).SelectMany(g => g.Where(c => (c.IsVisible && (c.GradeCoreId == 1 || c.GradeCoreId == 101 || c.GradeCoreId == 201)) || c.NameCode == 3999))];
            selectedCharacters = [.. allCharacterData.Where(c => !sickPullsExclusionList.Contains(c.Id)).OrderBy(_ => Random.Shared.Next()).Take(numberOfPulls)];
        }
        else if (gachaType.Type == GachaPremiumType.GachaTutorial)
        {
            if (numberOfPulls != 10)
            {
                Logging.WriteLine("[SelectRandomCharacter] Tutorial Gacha Banners must have 10 pulls", LogType.Error);
                throw new ArgumentException("Tutorial Gacha Banners must have 10 pulls");
            }

            long ssrSpot = Random.Shared.NextInt64(0, 10);

            for (int i = 0; i < numberOfPulls; i++)
            {
                if (i == ssrSpot)
                {
                    GachaGradeProbRecord gradeProbs = GameData.Instance.GachaGradeProb.Values
                        .First(p => p.GroupId == gachaType.GradeProbId && p.Rare == OriginalRareType.SSR);
                    CharacterRecord character = SelectRandomCharacterFromProbList(gradeProbs, wishlistCharacters, user);
                    selectedCharacters.Add(character);
                }
                else
                {
                    CharacterRecord character = SelectRandomCharacter(gachaType, wishlistCharacters, user);
                    selectedCharacters.Add(character);
                }
            }
        }
        else
        {
            for (int i = 0; i < numberOfPulls; i++)
            {
                CharacterRecord character = SelectRandomCharacter(gachaType, wishlistCharacters, user);
                selectedCharacters.Add(character);
            }
        }

        return selectedCharacters;
    }

    private static CharacterRecord SelectRandomCharacter(GachaTypeRecord gachaType, List<CharacterRecord> wishlistCharacters, User user)
    {
        var gradeProbs = _gradeProbsByGroup.GetOrAdd(gachaType.GradeProbId, id =>
            GameData.Instance.GachaGradeProb.Values
                .Where(p => p.GroupId == id)
                .OrderBy(p => p.Prob)
                .ToList());

        bool limitedMode = gachaType.Type == GachaPremiumType.GachaFriend;
        var limitedSsr = limitedMode ? GetLimitedCandidates(user, OriginalRareType.SSR, 100) : [];

        Dictionary<int, int> gradeWeights = gradeProbs.ToDictionary(g => g.Id, g => g.Prob);
        int baseTotal = gradeWeights.Values.Sum();

        if (limitedSsr.Count > 0)
        {
            int ssrSum = gradeProbs.Where(g => g.Rare == OriginalRareType.SSR).Sum(g => g.Prob);
            int otherSum = baseTotal - ssrSum;
            double ssrRate = user.sickpulls ? LimitedSsrRateSickPulls : LimitedSsrRate;
            double targetSsr = ssrRate * baseTotal;

            if (ssrSum > 0 && otherSum > 0 && targetSsr > ssrSum)
            {
                double ssrFactor = targetSsr / ssrSum;
                double otherFactor = (baseTotal - targetSsr) / otherSum;
                foreach (var g in gradeProbs)
                {
                    int w = (int)Math.Round(g.Prob * (g.Rare == OriginalRareType.SSR ? ssrFactor : otherFactor));
                    gradeWeights[g.Id] = ssrRate >= 1.0 ? (g.Rare == OriginalRareType.SSR ? Math.Max(1, w) : 0) : Math.Max(1, w);
                }
            }
        }

        int maxProb = gradeWeights.Values.Sum();
        if (maxProb <= 0) maxProb = 1;

        int gradeRoll = (int)Random.Shared.NextInt64(maxProb);
        GachaGradeProbRecord selectedGrade = gradeProbs[0];
        int curVal = 0;
        foreach (var p in gradeProbs)
        {
            curVal += gradeWeights[p.Id];
            if (gradeRoll < curVal)
            {
                selectedGrade = p;
                break;
            }
        }

        return RollCharacterFromGrade(selectedGrade, wishlistCharacters, user, gachaType, limitedMode ? limitedSsr : null);
    }

    private static CharacterRecord RollCharacterFromGrade(
        GachaGradeProbRecord selectedGrade,
        List<CharacterRecord> wishlistCharacters,
        User user,
        GachaTypeRecord? gachaType,
        Dictionary<int, GachaListProbRecord>? limitedSsr = null)
    {
        List<GachaListProbRecord> candidateList;

        if (gachaType?.Type == GachaPremiumType.GachaFriend && selectedGrade.Rare == OriginalRareType.SSR && limitedSsr != null && limitedSsr.Count > 0)
        {
            candidateList = limitedSsr.Values.ToList();
        }
        else
        {
            var fullList = _listProbsByGroup.GetOrAdd(selectedGrade.GachaListId, id =>
                GameData.Instance.GachaListProb.Values
                    .Where(g => g.GroupId == id)
                    .ToList());

            if (wishlistCharacters != null && wishlistCharacters.Count == 20 && selectedGrade.GachaListId != selectedGrade.CustomizeListId && selectedGrade.CustomizeListId != 0)
            {
                var ids = new HashSet<int>(wishlistCharacters.Select(c => c.Id));
                candidateList = fullList.Where(g => ids.Contains(g.GachaId)).ToList();
                if (candidateList.Count == 0) candidateList = fullList;
            }
            else if (gachaType?.Type == GachaPremiumType.GachaFriend && selectedGrade.Rare == OriginalRareType.SR)
            {
                candidateList = [.. fullList];
                int avg = candidateList.Count == 0 ? 100 : Math.Max(1, (int)candidateList.Average(r => r.Prob));
                foreach (var kv in GetLimitedCandidates(user, OriginalRareType.SR, avg))
                {
                    candidateList.Add(kv.Value);
                }
            }
            else
            {
                candidateList = fullList;
            }
        }

        int maxCharProb = 0;
        foreach (var cp in candidateList) maxCharProb += cp.Prob;
        if (maxCharProb <= 0) maxCharProb = 1;

        int charRoll = (int)Random.Shared.NextInt64(maxCharProb);
        GachaListProbRecord selectedCharacter = candidateList[0];
        int accum = 0;
        foreach (var cp in candidateList)
        {
            accum += cp.Prob;
            if (charRoll < accum)
            {
                selectedCharacter = cp;
                break;
            }
        }

        int characterID = selectedCharacter.GachaId;
        if (selectedCharacter.GachaType == GachaCategory.GachaSelectup && gachaType != null)
        {
            try
            {
                if (user.GachaSelectupChoices.TryGetValue(gachaType.Id, out int choice))
                {
                    characterID = GameData.Instance.GachaSelectupListTable[choice].CharacterId;
                }
            }
            catch
            {
                Logging.WriteLine("[SelectRandomCharacter] Could not get the character from the selectup choice", LogType.Warning);
            }
        }

        return GameData.Instance.CharacterTable[characterID];
    }

    /// <summary>
    /// This functions is mostly used for the tutorial pull and will not handle the selectup banners (already handled by the main gacha pull function)
    /// </summary>
    private static CharacterRecord SelectRandomCharacterFromProbList(GachaGradeProbRecord selectedGrade, List<CharacterRecord> wishlistCharacters, User user)
    {
        return RollCharacterFromGrade(selectedGrade, wishlistCharacters, user, null);
    }
}
