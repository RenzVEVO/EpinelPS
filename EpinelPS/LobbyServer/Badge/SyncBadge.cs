using EpinelPS.Database;

namespace EpinelPS.LobbyServer.Badge;

[GameRequest("/badge/sync")]
public class SyncBadge : LobbyMessage
{
    protected override async Task HandleAsync()
    {
        ReqSyncBadge req = await ReadData<ReqSyncBadge>();
        User user = GetUser();

        ResSyncBadge response = new();

        if (req.LastBadgeSeq > user.LastBadgeSeq)
        {
            user.LastBadgeSeq = req.LastBadgeSeq;
        }

        // Clean up any legacy MailboxMessage badges that cause client-side SyncedBadgeHandler crash
        user.Badges.RemoveAll(b => b.BadgeContent == BadgeContents.MailboxMessage);

        bool hasUnclaimedMail = user.MailDatas.Values.Any(m => m.State == 1 && m.HasReward);
        if (hasUnclaimedMail)
        {
            var existingMailboxBadge = user.Badges.FirstOrDefault(b => b.BadgeContent == BadgeContents.Mailbox);
            if (existingMailboxBadge == null)
            {
                user.LastBadgeSeq++;
                existingMailboxBadge = new BadgeModel
                {
                    BadgeContent = BadgeContents.Mailbox,
                    BadgeGuid = Guid.NewGuid().ToString(),
                    Location = string.Empty,
                    Seq = user.LastBadgeSeq
                };
                user.Badges.Add(existingMailboxBadge);
            }
        }
        else
        {
            user.Badges.RemoveAll(b => b.BadgeContent == BadgeContents.Mailbox || b.BadgeContent == BadgeContents.MailboxMessage);
        }

        foreach (BadgeModel item in user.Badges)
        {
            response.BadgeList.Add(item.ToNet());
        }

        JsonDb.Save();
        await WriteDataAsync(response);
    }
}
