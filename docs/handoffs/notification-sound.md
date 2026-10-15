# Notification sound payloads

FirebaseMessagingClient general Android alerts select ankt_notifications_v2 and
raw resource ankt_notification with DefaultSound=false. General/chat APNs alerts
select bundled ankt_notification.wav. Incoming calls retain their existing sound
and call lifecycle control pushes remain data-only/silent. Chat on Android remains
data-only for Notifee rendering with the versioned messages channel.

NotificationSoundTests: 2 passed. Matching host WAV/native channel/config changes
are in viora; rebuild native app and deploy BE together. No deployment performed.
