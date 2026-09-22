using EpinelPS.Data;
using EpinelPS.Database;
using EpinelPS.Models;

namespace EpinelPS.LobbyServer.Shop.InApp;

[GameRequest("/inappshop/getmonthlysubscriptionreward")]
public class GetMonthlySubscriptionReward : LobbyMessage
{
    protected override async Task HandleAsync()
    {
        _ = await ReadData<ReqGetMonthlySubscriptionReward>();

        User user = GetUser();
        ResGetMonthlySubscriptionReward response = new();

        DateTime now = DateTime.UtcNow;
        bool changed = false;

        foreach (var (tid, expiry) in user.MonthlySubscriptions)
        {
            if (expiry <= now)
                continue;

            response.DataList.Add(new NetMonthlySubscriptionData
            {
                Tid = tid,
                ExpiredAt = expiry.Ticks,
            });

            bool alreadyClaimedToday = user.MonthlySubscriptionLastClaimed.TryGetValue(tid, out var lastClaim) &&
                                       lastClaim.Date == now.Date;

            if (!alreadyClaimedToday && GameData.Instance.MonthlyAmountTable.TryGetValue(tid, out var monthly))
            {
                NetRewardData dailyReward = new() { PassPoint = new NetPassPointData() };
                if (InAppPurchaseHelper.GrantPackageGroup(user, monthly.DailyPackageGroupId, ref dailyReward, allowEmpty: true))
                {
                    response.RewardList.Add(new NetMonthlySubscriptionReward
                    {
                        Tid = tid,
                        Reward = dailyReward,
                    });
                    user.MonthlySubscriptionLastClaimed[tid] = now;
                    changed = true;
                }
            }
        }

        if (changed)
        {
            JsonDb.Save();
        }

        await WriteDataAsync(response);
    }
}
