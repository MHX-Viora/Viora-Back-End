# Live chat implementation plan

1. Add a bounded, thread-safe `ILiveCommentBuffer` and tests for concurrent sends, limits, ordering, and Live isolation.
2. Replace hub database comment path with buffer/event path; add report/delete/mute methods and batch Live comment counter.
3. Keep the legacy table intact; flush and clear transient state when Live ends.
4. Update SignalR client and host/viewer comment behavior for deduplication, deletion, reconnect and bounded state.
5. Run focused backend/frontend tests, builds, and review only Live chat diffs.
