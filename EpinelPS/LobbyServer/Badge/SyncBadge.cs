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
            // CRITICAL: The official client only displays the red dot if the badge sequence
            // is strictly greater than req.LastBadgeSeq. If the sequence is <= req.LastBadgeSeq,
            // the client ignores it as already seen/acknowledged.
            // By always ensuring a fresh sequence > req.LastBadgeSeq, the red dot is guaranteed
            // to show on every sync as long as rewards remain unclaimed.
            long freshSeq = Math.Max(user.LastBadgeSeq, req.LastBadgeSeq) + 1;
            user.LastBadgeSeq = freshSeq;

            user.Badges.RemoveAll(b => b.BadgeContent == BadgeContents.Mailbox);
            user.Badges.Add(new BadgeModel
            {
                BadgeContent = BadgeContents.Mailbox,
                BadgeGuid = Guid.NewGuid().ToString(),
                Location = string.Empty,
                Seq = freshSeq
            });
        }
        else
        {
            // No unclaimed rewards remain -> extinguish the Mailbox red dot badge
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
