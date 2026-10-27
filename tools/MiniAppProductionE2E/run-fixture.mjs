// Reuse the Stage 7 real review/executor and its owned process/DB/browser cleanup.
// Production mode additionally runs the actual Expo Web -> host -> partner path.
if (!process.argv.includes('--production-e2e')) process.argv.push('--production-e2e');
await import('../MiniAppReviewE2E/run-fixture.mjs');
