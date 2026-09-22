using EpinelPS.Models;

namespace EpinelPS.LobbyServer.Shop.InApp;

[GameRequest("/inappshop/getreceivableproductlist")]
public class GetRetrivableProductList : LobbyMessage
{
    protected override async Task HandleAsync()
    {
        _ = await ReadData<ReqGetInAppShopReceivableProductList>();
        User user = GetUser();

        ResGetInAppShopReceivableProductList response = new();
        var pending = InAppPurchaseHelper.TakePendingReceivableProducts(user.ID);
        foreach (var item in pending)
        {
            response.DataList.Add(item);
        }

        await WriteDataAsync(response);
    }
}
