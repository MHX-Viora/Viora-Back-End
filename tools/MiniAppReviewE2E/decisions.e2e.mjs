import assert from 'node:assert/strict';
export async function decisions(ctx) {
  const { owner, admin, config, appPath, createVersion, collect, submit, decide, publish, productionLogin, fixture, button } = ctx;
  const original = (await fixture('review-state')).publications.find(x => x.status === 'Published');
  const changedVersion = await createVersion('Review E2E 2'); await collect(); const changes = await submit();
  await decide(changes, 'request-changes', '<script>window.reviewXss=true</script> Please correct the application name.');
  assert.equal(await admin.read(`window.reviewXss===undefined && !document.querySelector('script:not([src])')`), true);
  await owner.navigate(config.sandboxPortalOrigin + appPath + '?tab=review&reviewId=' + changes.id);
  await owner.wait(`document.body.textContent.includes('Please correct the application name.')`);
  assert.equal(await owner.read(`window.reviewXss===undefined`), true);
  // The approved/published target remains v1 while v2 is being changed.
  assert.equal((await fixture('review-state')).publications.find(x => x.status === 'Published').miniAppVersionId, original.miniAppVersionId);
  await owner.navigate(config.sandboxPortalOrigin + appPath); await owner.wait(`!!document.querySelector('#mini-tab-1')`);
  await owner.click(`document.querySelector('#mini-tab-1')`); await owner.type('#app-name', 'Corrected review partner'); await owner.click(button('Lưu bản nháp'));
  await owner.wait(`document.body.textContent.includes('Đã lưu') && !document.body.textContent.includes('Có thay đổi chưa lưu.')`);
  await (await import('./isolation.e2e.mjs')).draftIsolation(ctx, changedVersion.id, changedVersion.snapshotHash);
  // The existing editor creates the next working draft from a sealed version.
  await owner.navigate(config.sandboxPortalOrigin + appPath + '?tab=review');
  await owner.wait(`!!document.querySelector('#review-version')`); await ctx.prepareCredential(); await collect();
  const corrected = await submit(); await decide(corrected, 'approve');
  const correctedState = await fixture('review-state'), correctedVersion = correctedState.versions.find(x => x.id === corrected.miniAppVersionId);
  assert.ok(correctedVersion.versionCode > 2); await publish(correctedVersion.versionCode); await productionLogin();
  console.log('REVIEW_FLOW_B_CHANGES_ESCAPED_REASON_NEW_DRAFT_NEW_CHECKS_RESUBMIT_APPROVE');
  await createVersion('Review E2E rejected'); await collect(); const rejected = await submit();
  await decide(rejected, 'reject', 'The submitted integration does not meet the required review criteria.');
  await owner.navigate(config.sandboxPortalOrigin + appPath + '?tab=review&reviewId=' + rejected.id);
  await owner.wait(`document.body.textContent.includes('The submitted integration does not meet')`);
  const rejectedState = await fixture('review-state'), rejectedVersion = rejectedState.versions.find(x => x.id === rejected.miniAppVersionId);
  await owner.wait(`!!${button('Phát hành phiên bản #' + rejectedVersion.versionCode)} && ${button('Phát hành phiên bản #' + rejectedVersion.versionCode)}.disabled`);
  assert.equal(await owner.read(`!!${button('Phát hành phiên bản #' + rejectedVersion.versionCode)}?.disabled`), true);
  await ctx.rejectPublicationTarget(rejectedVersion.id);
  await createVersion('Review E2E after rejection'); await collect(); const resubmitted = await submit(); await decide(resubmitted, 'approve');
  const latest = (await fixture('review-state')).versions.find(x => x.id === resubmitted.miniAppVersionId);
  await publish(latest.versionCode); await productionLogin();
  ctx.latestVersionCode = latest.versionCode;
  console.log('REVIEW_FLOW_C_REJECTION_CANNOT_PUBLISH_NEW_VERSION_NEW_CHECKS_APPROVAL');
}
