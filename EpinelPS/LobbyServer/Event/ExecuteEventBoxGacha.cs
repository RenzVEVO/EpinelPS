using EpinelPS.Data;
using EpinelPS.Database;
using EpinelPS.Models;
using EpinelPS.Utils;

namespace EpinelPS.LobbyServer.Event;

[GameRequest("/event/boxgacha/execute")]
public class ExecuteEventBoxGacha : LobbyMessage
{
    protected override async Task HandleAsync()
    {
        ReqExecuteEventBoxGacha req = await ReadData<ReqExecuteEventBoxGacha>();
        User user = GetUser();

        // 1. Find EventBoxGachaRecord by EventId
        var boxGacha = GameData.Instance.EventBoxGachaTable.Values.FirstOrDefault(x => x.EventId == req.EventId);
        if (boxGacha == null)
        {
            Logging.WriteLine($"[EventBoxGacha] Unknown event id: {req.EventId}", LogType.Warning);
            await WriteDataAsync(new ResExecuteEventBoxGacha());
            return;
        }

        if (!user.EventBoxGachaData.TryGetValue(req.EventId, out var gachaData))
        {
            gachaData = new UserEventBoxGachaData();
            user.EventBoxGachaData[req.EventId] = gachaData;
        }

        int currentCount = gachaData.GachaCount + 1; // 1-indexed roll number

        // 2. Determine price from EventBoxGachaPriceTable
        var priceRec = GameData.Instance.EventBoxGachaPriceTable.Values
            .FirstOrDefault(p => p.Group == boxGacha.PriceGroup && p.Count == currentCount);

        // 3. Check and deduct tickets
        int ticketId = boxGacha.EventItemId;
        int remainingTickets = 0;
        DbItemData? ticketItem = user.Items.FirstOrDefault(i => i.ItemType == ticketId);

        if (priceRec != null && priceRec.ItemType == RewardType.Item && priceRec.ItemCount > 0)
        {
            if (ticketItem == null || ticketItem.Count < priceRec.ItemCount)
            {
                Logging.WriteLine($"[EventBoxGacha] User {user.ID} has insufficient tickets for event {req.EventId}. Needed: {priceRec.ItemCount}, Has: {ticketItem?.Count ?? 0}", LogType.Warning);
                await WriteDataAsync(new ResExecuteEventBoxGacha());
                return;
            }

            ticketItem.Count -= priceRec.ItemCount;
            remainingTickets = ticketItem.Count;
        }
        else
        {
            remainingTickets = ticketItem?.Count ?? 0;
        }

        // 4. Determine pool of undrawn rewards
        var drawnOrders = new HashSet<int>(gachaData.RewardOrders);
        var allRewards = GameData.Instance.EventBoxGachaRewardTable.Values
            .Where(r => r.Group == boxGacha.GachaRewardGroup)
            .OrderBy(r => r.Order)
            .ToList();

        var availableRewards = allRewards.Where(r => !drawnOrders.Contains(r.Order)).ToList();
        if (availableRewards.Count == 0)
        {
            Logging.WriteLine($"[EventBoxGacha] Event {req.EventId} box is already completed for user {user.ID}", LogType.Warning);
            await WriteDataAsync(new ResExecuteEventBoxGacha());
            return;
        }

        // 5. Calculate probabilities using EventBoxGachaProbTable for currentCount
        var probRecords = GameData.Instance.EventBoxGachaProbTable.Values
            .Where(p => p.Group == boxGacha.ProbGroup && p.Count == currentCount)
            .ToDictionary(p => p.Order, p => p.Rate);

        int totalWeight = 0;
        foreach (var rew in availableRewards)
        {
            int rate = probRecords.TryGetValue(rew.Order, out var r) ? r : 100;
            totalWeight += rate;
        }

        EventBoxGachaRewardRecord winningReward;
        if (totalWeight <= 0)
        {
            winningReward = availableRewards[Rng.Next(0, availableRewards.Count)];
        }
        else
        {
            int roll = Rng.Next(0, totalWeight);
            int cumulative = 0;
            winningReward = availableRewards.Last();
            foreach (var rew in availableRewards)
            {
                int rate = probRecords.TryGetValue(rew.Order, out var r) ? r : 100;
                cumulative += rate;
                if (roll < cumulative)
                {
                    winningReward = rew;
                    break;
                }
            }
        }

        // 6. Grant winning reward to user
        NetRewardData rewardData = new();
        RewardUtils.AddSingleObject(user, ref rewardData, winningReward.ItemId, winningReward.ItemType, winningReward.ItemCount);

        // 7. Update user box gacha state
        gachaData.RewardOrders.Add(winningReward.Order);
        gachaData.GachaCount = currentCount;
        JsonDb.Save();

        // 8. Build response
        ResExecuteEventBoxGacha response = new()
        {
            Reward = rewardData,
            Ticket = new NetUserItemData
            {
                Tid = ticketId,
                Count = remainingTickets,
                Isn = ticketItem?.Isn ?? 0,
                Corporation = ticketItem?.Corp ?? 0,
            },
        };
        response.RewardOrders.AddRange(gachaData.RewardOrders);

        await WriteDataAsync(response);
    }
}