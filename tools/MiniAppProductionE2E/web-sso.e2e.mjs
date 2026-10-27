import assert from 'node:assert/strict';
import { setTimeout as delay } from 'node:timers/promises';
import { page } from '../MiniAppReviewE2E/browser-helpers.mjs';
export async function embeddedLogin({ parent, frame, cdp, id, decision = 'allow', consent = false }) {
  const auth = value => `document.querySelector('#production-auth')?.textContent===${JSON.stringify(value)}`;
    const before = new Set((await cdp.send('Target.getTargets')).targetInfos.map(target => target.targetId));
    await cdp.send('Target.activateTarget', { targetId: parent.targetId });
    await frame.click(`document.querySelector(${JSON.stringify('#' + id)})`);
    let popup;
    for (let attempt = 0; attempt < 100 && !popup; attempt++) {
      const target = (await cdp.send('Target.getTargets')).targetInfos.find(target => target.type === 'page' && !before.has(target.targetId));
      if (target) popup = await page(cdp, null, target.targetId);
      else if (!consent && await frame.read(auth('production_authenticated'))) return;
      else await delay(100);
    }
    assert.ok(popup, 'Actual issuer popup must open');
    if (consent) {
      await popup.wait(`!!document.querySelector('button[name=decision][value=allow]')`, 'Production consent');
      assert.equal(await popup.read(`document.querySelector('[data-environment]')?.getAttribute('data-environment')`), 'Production');
      if (id === 'production-scope') assert.equal(await popup.read(`document.body.textContent.includes('profile')`), true);
      await popup.click(`document.querySelector('button[name=decision][value=${decision}]')`);
    }
    await frame.wait(auth(decision === 'deny' ? 'production_denied' : 'production_authenticated'), 'actual embedded ' + decision);
    await cdp.send('Target.activateTarget', { targetId: parent.targetId });
}
export async function productionWebSso({ parent, partner, findPartner, ownerBrowser, config, partnerProduction }) {
  const cdp = ownerBrowser.client;
  let frame = partner;
  const auth = value => `document.querySelector('#production-auth')?.textContent===${JSON.stringify(value)}`;
  await frame.wait(`!!document.querySelector('#production-login')`);
  const login = (id, decision, consent) => embeddedLogin({ parent, frame, cdp, id, decision, consent });
  await login('production-login', 'deny', true);
  await login('production-login', 'allow', true);
  await login('production-login', 'allow', false);
  await login('production-scope', 'allow', true);
  await login('production-scope', 'allow', false);
  const before = partnerProduction.embeddedMetrics();
  if (config.partnerClientType === 'Confidential') { assert.ok(before.exchanges >= 4); assert.ok(before.userInfo >= before.exchanges); }
  else assert.equal(before.exchanges, 0, 'Public tokens must never be exchanged by the partner backend');
  console.log('STAGE8_EMBEDDED_SSO_DENY_RETRY_REPEAT_NEW_SCOPE_' + config.partnerClientType.toUpperCase());
  await frame.click('document.querySelector("#production-reload")');
  frame = await findPartner();
  await frame.wait(auth(config.partnerClientType === 'Confidential' ? 'production_authenticated' : 'unauthenticated'), 'reload/session policy');
  if (config.partnerClientType === 'Public') await login('production-scope', 'allow', false);
  else assert.ok(partnerProduction.embeddedMetrics().userInfo > before.userInfo, 'Reload must freshly validate UserInfo');
  console.log('STAGE8_EMBEDDED_SSO_DENY_RETRY_REPEAT_NEW_SCOPE_RELOAD_' + config.partnerClientType.toUpperCase());
  return frame;
}
