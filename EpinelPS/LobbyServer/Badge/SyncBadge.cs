namespace EpinelPS.LobbyServer.Badge;

[GameRequest("/badge/sync")]
public class SyncBadge : LobbyMessage
{
    protected override async Task HandleAsync()
    {
        ReqSyncBadge req = await ReadData<ReqSyncBadge>();
        User user = GetUser();

        ResSyncBadge response = new();

        foreach (BadgeModel item in user.Badges)
        {
            response.BadgeList.Add(item.ToNet());
        }

        bool hasUnclaimedMail = user.MailDatas.Values.Any(m => m.State == 1 && m.HasReward);
        if (hasUnclaimedMail && !response.BadgeList.Any(b => b.BadgeContent == BadgeContents.Mailbox))
        {
            response.BadgeList.Add(new NetBadge
            {
                BadgeContent = BadgeContents.Mailbox,
                BadgeGuid = Google.Protobuf.ByteString.CopyFrom(Guid.NewGuid().ToByteArray()),
                Location = "",
                Seq = 2000
            });
        }

        await WriteDataAsync(response);
    }
}
