# Implementation Plan: Group chat

## Phase 1: Foundation

- Add `MessageType.System`, conversation soft deletion, contracts, validators, and
  route contract tests. Verify with focused tests and build.
- Add reusable selectable-friends query using accepted friendships and the online
  registry. Verify filtering, ordering, and pagination contracts.

## Phase 2: Core group flow

- Implement create-group transaction, image upload, members, system message,
  invitations, and realtime delivery.
- Implement group details and paginated member listing with active-membership checks.
- Checkpoint: creation/read tests and existing private-chat suite pass.

## Phase 3: Membership and roles

- Implement add/remove/leave with role rules and reactivation semantics.
- Implement promote/demote/transfer-owner with owner-only authorization.
- Checkpoint: authorization matrix and transactional behavior tests pass.

## Phase 4: Group settings and dissolution

- Implement rename, avatar, and send-permission changes.
- Implement soft-delete dissolution and deactivate all memberships.
- Checkpoint: all group APIs, full tests, and build pass.

## Risks

- External delivery cannot be rolled back: persist first, publish only after commit.
- Composite member key prevents inserting returning members: reactivate existing rows.
- Existing chat queries may expose dissolved groups: filter `DeletedAt` consistently.
- Concurrent role/member changes: lock the group row within each mutation transaction.

