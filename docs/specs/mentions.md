# Mentions

Implement persisted user mentions for posts, comments, replies, and messages.
Creation contracts accept optional `mentionUserIds`; read contracts expose
`mentions: []`. Eligibility excludes self, duplicates, missing/inactive users,
blocked relationships, and users with `AllowMention=false`. Eligible mentions
are persisted before notification delivery. Search is accent-insensitive and
orders friends, following, followers, then other users.
