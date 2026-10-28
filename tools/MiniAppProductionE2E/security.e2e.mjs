import assert from 'node:assert/strict';
export async function productionWebSecurity({ parent, partner, fixture, appId }) {
  const negative = value => `document.querySelector('#production-negative')?.textContent===${JSON.stringify(value)}`;
  await partner.click('document.querySelector("#production-forge-host")');
  await partner.wait(negative('forged_sent'));
  await partner.click('document.querySelector("#production-check-context")');
  await partner.wait(negative('context_verified'));
  assert.equal(await parent.read(`location.pathname===${JSON.stringify('/mini-apps/' + appId)}&&document.querySelectorAll('iframe').length===1`), true);
  assert.equal((await fixture('production-state')).activeSessions, 1, 'Forged private-channel messages cannot close another host');
  await partner.click('document.querySelector("#production-dangerous-link")');
  await partner.wait(negative('dangerous_link_denied'));
  await partner.click('document.querySelector("#production-permission")');
  await partner.wait(negative('media_policy_denied'));
  console.log('STAGE8_FLOW_D_REAL_FORGED_CHANNEL_CONTEXT_DANGEROUS_SCHEME_AND_WEB_MEDIA_POLICY_DENIAL');
}
