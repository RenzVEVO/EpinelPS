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

        // Update user.LastBadgeSeq to track the client's acknowledged sequence
        if (req.LastBadgeSeq > user.LastBadgeSeq)
        {
            user.LastBadgeSeq = req.LastBadgeSeq;
        }

        bool hasUnclaimedMail = user.MailDatas.Values.Any(m => m.State == 1 && m.HasReward);
        if (hasUnclaimedMail)
        {
            // Ensure there is a Mailbox badge with Seq strictly greater than req.LastBadgeSeq
            var existingBadge = user.Badges.FirstOrDefault(b => b.BadgeContent == BadgeContents.Mailbox);
            if (existingBadge == null || existingBadge.Seq <= req.LastBadgeSeq)
            {
                if (existingBadge != null)
                {
                    user.Badges.Remove(existingBadge);
                }
                user.LastBadgeSeq = Math.Max(user.LastBadgeSeq, req.LastBadgeSeq) + 1;
                user.Badges.Add(new BadgeModel
                {
                    BadgeContent = BadgeContents.Mailbox,
                    BadgeGuid = Guid.NewGuid().ToString(),
                    Location = "",
                    Seq = user.LastBadgeSeq
                });
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
