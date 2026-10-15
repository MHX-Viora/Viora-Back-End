# Default Mini App categories

User authorized adding categories to Neon neondb configured in BE appsettings,
after clarification that this is a remote database. Read existing categories first;
insert eight missing Vietnamese defaults in a transaction with unique stable IDs,
slugs, Ionicons names, active=true and sort order. Preserve existing rows and do
not execute unrelated schema migrations. Save a repeatable SQL seed artifact.
Verify resulting rows and rerun seed to confirm zero duplicate insertions. No
credential output. This task changes database data, not the EF model.
