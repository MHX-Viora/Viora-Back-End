import assert from 'node:assert/strict';
export async function draftIsolation(ctx, reviewedVersion, reviewedHash) {
  const state = await ctx.fixture('review-state');
  assert.equal(state.publications.find(x => x.status === 'Published').miniAppVersionId, ctx.firstVersion.id);
  assert.equal(state.versions.find(x => x.id === ctx.firstVersion.id).snapshotHash, ctx.firstVersion.snapshotHash);
  assert.equal(state.versions.find(x => x.id === reviewedVersion).snapshotHash, reviewedHash);
  await ctx.productionLogin(false, 'Disposable executor partner');
  console.log('REVIEW_DRAFT_EDIT_PRESERVES_OLD_REVIEW_HASH_PUBLICATION_AND_PRODUCTION_DISPLAY');
}
export async function staleSources(ctx) {
  let safety = await ctx.fixtureSafety('expire-domain');
  assert.equal(safety.domainValid, false); assert.equal(safety.clientValid, true);
  assert.equal(await ctx.partnerProduction.userInfoWorks(), false);
  assert.equal(await ctx.partnerProduction.authorizationDenied(), true);
  console.log('REVIEW_EXPIRED_DOMAIN_DENIES_OLD_TOKEN_AND_NEW_AUTHORIZATION');
  // Reset only the original, explicitly seeded local-domain proof. No review,
  // decision, publication, consent or token is seeded by this fixture control.
  safety = await ctx.fixtureSafety('restore-domain'); assert.equal(safety.domainValid, true);
  await ctx.productionLogin();
  safety = await ctx.fixtureSafety('revoke-client');
  assert.equal(safety.domainValid, true); assert.equal(safety.clientValid, false);
  assert.equal(await ctx.partnerProduction.userInfoWorks(), false);
  assert.equal(await ctx.partnerProduction.authorizationDenied(), true);
  console.log('REVIEW_STALE_CLIENT_DENIES_OLD_TOKEN_AND_NEW_AUTHORIZATION');
}
