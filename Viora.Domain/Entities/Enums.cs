namespace Viora.Domain.Entities;

public enum AccountRole : short { User = 0, Moderator = 1, Admin = 2 }
public enum AccountStatus : short { Banned = 0, Active = 1, Deleted = 2 }
public enum Gender : short { Unknown = 0, Male = 1, Female = 2 }
public enum UserIdentityState : short { NotVerified = 0, Verified = 1, Rejected = 2 }
public enum IdentitySubmissionStatus : short { Pending = 1, Approved = 2, Rejected = 3 }
public enum PostType : short { Post = 0, ShortVideo = 1 }
public enum PostVisibility : short { Public = 0, Followers = 1, Private = 2 }
public enum PostStatus : short { Draft = 0, Published = 1, Hidden = 2, Deleted = 3 }
public enum ReactionType : short { Like = 0, Love = 1, Haha = 2, Wow = 3, Sad = 4, Angry = 5 }
public enum CommentStatus : short { Hidden = 0, Published = 1, Deleted = 2 }
public enum FriendshipStatus : short { Pending = 0, Accepted = 1, Rejected = 2, Cancelled = 3, Blocked = 4, Unfriended = 5 }
public enum ConversationType : short { Private = 0, Group = 1 }
public enum ConversationSendPermission : short { Everyone = 0, AdminsAndOwner = 1, OwnerOnly = 2 }
public enum ConversationMemberRole : short { Member = 0, Admin = 1, Owner = 2 }
public enum ConversationMemberStatus : short { Active = 0, Left = 1, Kicked = 2 }
public enum MessageType : short { Text = 0, Image = 1, Video = 2, File = 3, Audio = 4, Sticker = 5, Location = 6, Recall = 7, System = 100 }
public enum NotificationType : short
{
    System = 0, FriendRequest = 1, FriendAccepted = 2, Follow = 3, PostLike = 4,
    PostComment = 5, CommentReply = 6, Mention = 8, Message = 9, GroupInvite = 10,
    GroupRoleChanged = 11, GroupRemoved = 12, LiveStarted = 13, LiveEnded = 14,
    IdentityApproved = 15, IdentityRejected = 16, AdminAnnouncement = 17, ShopNotification = 18,
    PostShare = 19, FriendRejected = 20, CommentLike = 21
}
public enum NotificationReferenceType : short { User = 0, Post = 1, Comment = 2, Conversation = 3, Message = 4, Identity = 5 }
public enum DevicePlatform : short { Android = 0, Ios = 1, Web = 2, Other = 3 }
public enum ReportTargetType : short { User = 0, Post = 1, Comment = 2, Message = 3 }
public enum ReportReason : short { Spam = 0, Violence = 1, AdultContent = 2, HateSpeech = 3, FakeNews = 4, Scam = 5, Other = 6 }
public enum ReportStatus : short { Pending = 0, Approved = 1, Rejected = 2 }
public enum CallStatus : short { Calling = 0, Accepted = 1, Rejected = 2, Missed = 3, Cancelled = 4, Ended = 5 }
