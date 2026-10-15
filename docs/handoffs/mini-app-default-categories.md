# Default Mini App categories

User explicitly authorized seeding Neon neondb configured in BE appsettings after
clarification that it is remote. Initially MiniAppCategories contained zero rows.
Inserted eight active defaults: Tiện ích, Mua sắm, Ăn uống, Du lịch, Giáo dục,
Giải trí, Sức khỏe, Dịch vụ. SortOrder 10–80; Ionicons names and unique Vietnamese
slugs. No schema migrations or unrelated data changes were applied.

Repeatable SQL: docs/deployment/seed-mini-app-categories.sql. Transactional insert,
ON CONFLICT DO NOTHING, preserves existing admin edits. Verified eight rows after
insertion; second execution inserted zero rows and retained all eight. The
temporary runner loaded credentials directly from config and did not print them.
