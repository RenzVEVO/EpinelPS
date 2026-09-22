using System.Collections.Concurrent;
using EpinelPS.Data;
using EpinelPS.Database;
using EpinelPS.Models;
using EpinelPS.Utils;

namespace EpinelPS.LobbyServer.Shop.InApp;

internal static class InAppPurchaseHelper
{
    private static readonly ConcurrentDictionary<(ulong UserId, string ProductId), NetRewardData> PendingRewards = new();
    private static readonly ConcurrentDictionary<ulong, ConcurrentDictionary<int, (int ProductType, int ShopTid, int BuyCount)>> SessionClaimedPackages = new();
    private static readonly ConcurrentDictionary<ulong, ConcurrentQueue<NetInAppShopReceivableProductData>> PendingReceivableProducts = new();

    public static bool TrySimulatePurchase(User user, string productId, NetStartPurchaseExtraData? extraData,
        out NetRewardData reward)
    {
        reward = new NetRewardData { PassPoint = new NetPassPointData() };
        if (!GameConfig.Root.EnablePurchaseSimulation)
            return false;

        var midas = FindMidasProduct(productId);
        if (midas == null || !midas.IsActive)
            return false;

        bool granted = midas.ProductType switch
        {
            ProductType.CashShop => GrantCashShop(user, midas.ProductId, ref reward),
            ProductType.PackageShop => GrantPackageShop(user, midas.ProductId, ref reward),
            ProductType.CostumeShop => GrantCostumeShop(user, midas.ProductId, ref reward),
            ProductType.PassCostumeShop => GrantPassCostumeShop(user, midas.ProductId, ref reward),
            ProductType.MonthlyAmount => GrantMonthlyAmount(user, midas.ProductId, ref reward),
            _ => false,
        };

        if (!granted)
            return false;

        PendingRewards[(user.ID, productId)] = reward.Clone();

        var queue = PendingReceivableProducts.GetOrAdd(user.ID, _ => new ConcurrentQueue<NetInAppShopReceivableProductData>());
        queue.Enqueue(new NetInAppShopReceivableProductData
        {
            ProductId = productId,
            Token = $"tok-{Guid.NewGuid():N}",
            SubTid = 0
        });

        JsonDb.Save();
        return true;
    }

    public static NetRewardData TakePendingReward(ulong userId, string productId)
    {
        return PendingRewards.TryRemove((userId, productId), out var reward)
            ? reward
            : new NetRewardData { IsEmptyReward = true, PassPoint = new NetPassPointData() };
    }

    public static List<NetInAppShopReceivableProductData> TakePendingReceivableProducts(ulong userId)
    {
        List<NetInAppShopReceivableProductData> list = [];
        if (PendingReceivableProducts.TryGetValue(userId, out var queue))
        {
            while (queue.TryDequeue(out var item))
            {
                list.Add(item);
            }
        }
        return list;
    }

    public static bool HasClaimedFreePackageThisSession(ulong userId, int listTid)
    {
        return SessionClaimedPackages.TryGetValue(userId, out var userBuys) && userBuys.ContainsKey(listTid);
    }

    public static void RecordFreePackageClaimThisSession(ulong userId, int listTid, int productType, int shopTid)
    {
        var userBuys = SessionClaimedPackages.GetOrAdd(userId, _ => new ConcurrentDictionary<int, (int, int, int)>());
        userBuys[listTid] = (productType, shopTid, 1);
    }

    public static IReadOnlyDictionary<int, (int ProductType, int ShopTid, int BuyCount)> GetSessionClaimedPackages(ulong userId)
    {
        if (SessionClaimedPackages.TryGetValue(userId, out var userBuys))
        {
            return userBuys;
        }
        return new Dictionary<int, (int, int, int)>();
    }

    private static MidasProductRecord? FindMidasProduct(string productId)
    {
        if (GameData.Instance.mediasProductTable.TryGetValue(productId, out var exact))
            return exact;

        return GameData.Instance.mediasProductTable.Values.FirstOrDefault(x =>
            string.Equals(x.MidasProductIdProximabeta, productId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(x.MidasProductIdGamamobi, productId, StringComparison.OrdinalIgnoreCase));
    }

    private static bool GrantCashShop(User user, int cashShopId, ref NetRewardData reward)
    {
        if (!GameData.Instance.CashShopRecords.TryGetValue(cashShopId, out var product) || !product.IsActive)
            return false;

        switch (product.ProductType)
        {
            case CashShopProductType.Currency:
                RewardUtils.AddSingleObject(user, ref reward, product.ProductId, RewardType.Currency, product.ProductValue);
                return true;
            case CashShopProductType.Item:
                RewardUtils.AddSingleObject(user, ref reward, product.ProductId, RewardType.Item, product.ProductValue);
                return true;
            case CashShopProductType.Package:
                return GrantPackageGroup(user, product.ProductId, ref reward);
            default:
                return false;
        }
    }

    private static bool GrantPackageShop(User user, int packageShopId, ref NetRewardData reward)
    {
        return GameData.Instance.PackageShopTable.TryGetValue(packageShopId, out var package) &&
               GrantPackageGroup(user, package.PackageGroupId, ref reward);
    }

    private static bool GrantCostumeShop(User user, int costumeShopId, ref NetRewardData reward)
    {
        if (!GameData.Instance.CostumeShopTable.TryGetValue(costumeShopId, out var costume) || !costume.IsActive)
            return false;

        AddCostume(user, costume.CostumeId, ref reward);
        return GrantPackageGroup(user, costume.PackageGroupId, ref reward, allowEmpty: true);
    }

    private static bool GrantPassCostumeShop(User user, int passCostumeShopId, ref NetRewardData reward)
    {
        if (!GameData.Instance.PassCostumeShopTable.TryGetValue(passCostumeShopId, out var costume))
            return false;

        AddCostume(user, costume.CostumeId, ref reward);
        return GrantPackageGroup(user, costume.PackageGroupId, ref reward, allowEmpty: true);
    }

    private static bool GrantMonthlyAmount(User user, int monthlyAmountId, ref NetRewardData reward)
    {
        if (!GameData.Instance.MonthlyAmountTable.TryGetValue(monthlyAmountId, out var monthly))
            return false;

        // Grant initial purchase package group (e.g. 330 paid gems or 1210 paid gems)
        GrantPackageGroup(user, monthly.BuyPackageGroupId, ref reward, allowEmpty: true);

        // Register or extend subscription
        int days = monthly.Period > 0 ? monthly.Period : 30;
        DateTime now = DateTime.UtcNow;
        DateTime newExpiry = now.AddDays(days);
        if (user.MonthlySubscriptions.TryGetValue(monthlyAmountId, out var existingExpiry) && existingExpiry > now)
        {
            newExpiry = existingExpiry.AddDays(days);
        }
        user.MonthlySubscriptions[monthlyAmountId] = newExpiry;

        return true;
    }

    public static bool GrantPackageGroup(User user, int packageGroupId, ref NetRewardData reward, bool allowEmpty = false)
    {
        var products = GameData.Instance.PackageGroupTable.Values
            .Where(x => x.PackageGroupId == packageGroupId)
            .ToList();
        if (products.Count == 0)
            return allowEmpty;

        foreach (var product in products)
            RewardUtils.AddSingleObject(user, ref reward, product.ProductId, product.ProductType, product.ProductValue);
        return true;
    }

    private static void AddCostume(User user, int costumeId, ref NetRewardData reward)
    {
        if (!user.CostumeList.Contains(costumeId))
            user.CostumeList.Add(costumeId);
        reward.CharacterCostume.Add(costumeId);
    }
}
