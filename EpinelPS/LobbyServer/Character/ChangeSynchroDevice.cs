using EpinelPS.Database;
using EpinelPS.Utils;

namespace EpinelPS.LobbyServer.Character;

[GameRequest("/character/SynchroDevice/Change")]
public class ChangeSynchroDevice : LobbyMessage
{
    protected override async Task HandleAsync()
    {
        ReqSynchroChange req = await ReadData<ReqSynchroChange>();
        User user = GetUser();

        ResSynchroChange response = new();

        List<CharacterModel> highestLevelCharacters = [.. user.Characters.OrderByDescending(x => x.Level).Take(5)];

        if (highestLevelCharacters.Count < 5 || highestLevelCharacters.Any(x => x.Level < 200))
        {
            Logging.WriteLine("Synchro change rejected: requires 5 characters of level 200 or higher", LogType.Warning);
            await WriteDataAsync(response);
            return;
        }

        int minStandardLevel = highestLevelCharacters.Min(x => x.Level);
        user.SynchroDeviceLevel = Math.Max(user.SynchroDeviceLevel, minStandardLevel);
        user.SynchroDeviceUpgraded = true;

        if (user.SynchroSlots.Count == 0)
        {
            user.SynchroSlots = [
                new SynchroSlot() { Slot = 1 },
                new SynchroSlot() { Slot = 2 },
                new SynchroSlot() { Slot = 3 },
                new SynchroSlot() { Slot = 4 },
                new SynchroSlot() { Slot = 5 },
            ];
        }

        foreach (CharacterModel item in highestLevelCharacters)
        {
            item.Level = Math.Max(item.Level, user.SynchroDeviceLevel);

            response.Characters.Add(new NetUserCharacterData()
            {
                Default = new()
                {
                    Csn = item.Csn,
                    Skill1Lv = item.Skill1Lvl,
                    Skill2Lv = item.Skill2Lvl,
                    CostumeId = item.CostumeId,
                    Lv = item.Level,
                    Grade = item.Grade,
                    Tid = item.Tid,
                    UltiSkillLv = item.UltimateLevel
                },
                IsSynchro = user.GetSynchro(item.Csn)
            });
        }

        foreach (SynchroSlot item in user.SynchroSlots)
        {
            response.Slots.Add(new NetSynchroSlot()
            {
                Slot = item.Slot,
                AvailableRegisterAt = item.AvailableAt != 0 ? item.AvailableAt : 1,
                Csn = item.CharacterSerialNumber
            });
        }

        JsonDb.Save();

        await WriteDataAsync(response);
    }
}
