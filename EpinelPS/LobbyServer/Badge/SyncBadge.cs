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

        long maxClientSeq = Math.Max(req.LastBadgeSeq, req.LastUniqueBadgeSeq);
        if (maxClientSeq > user.LastBadgeSeq)
        {
            user.LastBadgeSeq = maxClientSeq;
        }

        var unclaimedMails = user.MailDatas.Values.Where(m => m.State == 1 && m.HasReward).ToList();
        if (unclaimedMails.Count > 0)
        {
            // 1. Ensure Mailbox badge exists for lobby header envelope icon
            var existingMailboxBadge = user.Badges.FirstOrDefault(b => b.BadgeContent == BadgeContents.Mailbox);
            if (existingMailboxBadge == null || existingMailboxBadge.Seq <= maxClientSeq)
            {
                if (existingMailboxBadge != null)
                {
                    user.Badges.Remove(existingMailboxBadge);
                }
                user.LastBadgeSeq = Math.Max(user.LastBadgeSeq, maxClientSeq) + 1;
                user.Badges.Add(new BadgeModel
                {
                    BadgeContent = BadgeContents.Mailbox,
                    BadgeGuid = Guid.NewGuid().ToString(),
                    Location = "",
                    Seq = user.LastBadgeSeq
                });
            }

            // 2. Ensure each unclaimed mail message has a MailboxMessage badge (red dot on item card in mailbox)
            var unclaimedMsns = unclaimedMails.Select(m => m.Msn.ToString()).ToHashSet();
            user.Badges.RemoveAll(b => b.BadgeContent == BadgeContents.MailboxMessage && !unclaimedMsns.Contains(b.Location));

            foreach (var mail in unclaimedMails)
            {
                string loc = mail.Msn.ToString();
                var existingMsgBadge = user.Badges.FirstOrDefault(b => b.BadgeContent == BadgeContents.MailboxMessage && b.Location == loc);
                if (existingMsgBadge == null || existingMsgBadge.Seq <= maxClientSeq)
                {
                    if (existingMsgBadge != null)
                    {
                        user.Badges.Remove(existingMsgBadge);
                    }
                    user.LastBadgeSeq = Math.Max(user.LastBadgeSeq, maxClientSeq) + 1;
                    user.Badges.Add(new BadgeModel
                    {
                        BadgeContent = BadgeContents.MailboxMessage,
                        BadgeGuid = Guid.NewGuid().ToString(),
                        Location = loc,
                        Seq = user.LastBadgeSeq
                    });
                }
            }
        }
        else
        {
            user.Badges.RemoveAll(b => b.BadgeContent == BadgeContents.Mailbox || b.BadgeContent == BadgeContents.MailboxMessage);
        }

        foreach (BadgeModel item in user.Badges)
        {
            response.BadgeList.Add(item.ToNet());
            response.UniqueBadgeList.Add(item.ToUniqueNet());
        }

        JsonDb.Save();
        await WriteDataAsync(response);
    }
}
