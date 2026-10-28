import assert from 'node:assert/strict';
import { setTimeout as delay } from 'node:timers/promises';
import { writeFile } from 'node:fs/promises';
import { join } from 'node:path';
import { randomUUID } from 'node:crypto';
import { page } from '../MiniAppReviewE2E/browser-helpers.mjs';
import { fixtureFetch } from '../SandboxExecutor/fixture-fetch.mjs';
import { startProductionPortal } from './portals.fixture.mjs';
import { productionWebSso, embeddedLogin } from './web-sso.e2e.mjs';
import { lifecycleSample, recordProductionPerformance } from './performance.e2e.mjs';
import { productionWebSecurity } from './security.e2e.mjs';
const button = label => `[...document.querySelectorAll('button,[role=button]')].find(e=>(e.getAttribute('aria-label')||e.textContent.trim())===${JSON.stringify(label)})`;
// This proves anonymous opening of an actually reviewed SSO-enabled publication,
// not non-SSO publication or partner authentication. Source remains anonymous.
export async function productionWebHost({ directory, config, ownerBrowser, fixture, appId, partnerProduction, partnerServer }) {
  const portal = await startProductionPortal(directory, config);
  let parent, partner, phase = 'catalog';
  const logoStart = partnerServer?.logoRequests();
  try {
    const response = await fixtureFetch(new URL('/api/mini-apps?page=1&pageSize=20', config.issuer));
    assert.equal(response.status, 200); const catalog = await response.json();
    assert.ok(catalog.items.some(item => item.id === appId), 'Real publication appears in anonymous catalog');
    const detail = await fixtureFetch(new URL('/api/mini-apps/' + appId, config.issuer));
    assert.equal(detail.status, 200); assert.equal((await detail.json()).canOpen, true);
    const invalidSource = await fixtureFetch(new URL('/api/mini-apps/' + appId + '/launch', config.issuer), {
      method: 'POST', headers: { Origin: config.productionAppOrigin, Authorization: 'Bearer invalid-fixture-source', 'Content-Type': 'application/json' },
      body: JSON.stringify({ requestId: randomUUID(), platform: 'Web', bindingId: 'untrusted', browserNonce: 'a'.repeat(43) }),
    });
    assert.equal(invalidSource.status, 401, 'Supplied invalid source cannot downgrade to anonymous');
    const before = await fixture('production-state');
    assert.equal(before.catalogCount, 1);
    phase = 'initial launch'; parent = await page(ownerBrowser.client);
    const failures = [];
    const stopObservation = ownerBrowser.client.on(event => {
      if (event.method === 'Network.loadingFailed') failures.push(event.params.errorText);
      if (event.method === 'Network.responseReceived' && event.params.response.status >= 400) {
        const url = new URL(event.params.response.url);
        failures.push(url.origin + url.pathname + ':' + event.params.response.status);
      }
    });
    await parent.call('Network.enable');
    const initialStarted = performance.now();
    await parent.navigate(config.productionAppOrigin + '/mini-apps/' + appId);
    try { await parent.wait(`document.body.textContent.includes('Đã kết nối SDK · Production')`, 'actual Production bridge connected'); }
    catch (error) {
      // Known public UI text only; never inspect cookies/storage/credentials.
      console.error('STAGE8_HOST_UI_DIAGNOSTIC ' + JSON.stringify({
        network: failures.slice(-8), issues: parent.issues,
        ui: await parent.read(`({text:document.body.innerText.slice(0,500),frames:document.querySelectorAll('iframe').length})`),
      })); throw error;
    } finally { stopObservation(); }
    const initialLaunchMs = performance.now() - initialStarted;
    const samples = [await lifecycleSample(parent, 'initial')];
    assert.equal(await parent.read(`location.pathname===${JSON.stringify('/mini-apps/' + appId)}`), true, 'Anonymous root route remains public');
    async function findPartner() {
      for (let attempt = 0; attempt < 100; attempt++) {
        const target = (await ownerBrowser.client.send('Target.getTargets')).targetInfos.reverse().find(target => target.type === 'iframe' && target.url === 'https://partner.ankt.example.com/');
        if (target) {
          const frame = await page(ownerBrowser.client, null, target.targetId);
          if (await frame.read(`document.querySelector('#status')?.textContent==='production_bridge_verified'`)) return frame;
          await ownerBrowser.client.send('Target.detachFromTarget', { targetId: target.targetId }).catch(() => {});
        }
        await delay(100);
      }
      throw Error('Actual Production SDK v1 frame unavailable');
    }
    phase = 'partner frame'; partner = await findPartner();
    assert.equal(await partner.read(`document.querySelector('#context').textContent`), 'Production · inside true');
    let state = await fixture('production-state');
    assert.equal(state.sessions, before.sessions + 1); assert.equal(state.activeSessions, 1);
    for (const width of [320, 375, 768, 1440]) {
      phase = 'viewport ' + width;
      await parent.call('Emulation.setDeviceMetricsOverride', { width, height: 900, deviceScaleFactor: 1, mobile: false });
      await parent.wait(`innerWidth===${width}`);
      assert.equal(await parent.read('document.documentElement.scrollWidth<=innerWidth+1'), true, 'Real host overflow at ' + width);
      if (width === 320 || width === 1440) {
        phase = 'screenshot ' + width;
        await ownerBrowser.client.send('Target.activateTarget', { targetId: parent.targetId });
        const screenshot = await parent.call('Page.captureScreenshot', { format: 'png', captureBeyondViewport: false });
        await writeFile(join(import.meta.dirname, '../../.codex-tmp/stage8-real-host-' + config.partnerClientType.toLowerCase() + '-' + width + '.png'), Buffer.from(screenshot.data, 'base64'));
      }
    }
    phase = 'embedded SSO'; partner = await productionWebSso({ parent, partner, findPartner, ownerBrowser, config, partnerProduction });
    phase = 'hostile Web cases'; await productionWebSecurity({ parent, partner, fixture, appId });
    const beforeReopen = partnerProduction.embeddedMetrics();
    phase = 'SDK close'; await partner.click('document.querySelector("#close")');
    await parent.wait(`document.body.textContent.includes('Đã đóng Mini App.')`, 'SDK close confirmed by actual host endpoint');
    state = await fixture('production-state'); assert.equal(state.activeSessions, 0);
    phase = 'retry'; await parent.click(button('Thử lại'));
    await parent.wait(`document.body.textContent.includes('Đã kết nối SDK · Production')`, 'fresh launch after SDK close');
    state = await fixture('production-state'); assert.equal(state.sessions, before.sessions + 2); assert.equal(state.activeSessions, 1);
    const reopened = await findPartner();
    await reopened.wait(`document.querySelector('#production-auth')?.textContent===${JSON.stringify(config.partnerClientType === 'Confidential' ? 'production_authenticated' : 'unauthenticated')}`);
    if (config.partnerClientType === 'Public') await embeddedLogin({ parent, frame: reopened, cdp: ownerBrowser.client, id: 'production-scope' });
    else assert.ok(partnerProduction.embeddedMetrics().userInfo > beforeReopen.userInfo, 'Reopen requires a fresh partner UserInfo check');
    console.log('STAGE8_EMBEDDED_SSO_FRESH_HOST_REOPEN_' + config.partnerClientType.toUpperCase());
    samples.push(await lifecycleSample(parent, 'after-close-retry'));
    phase = 'replace'; await parent.click(button('Quay về ANKT'));
    await parent.navigate(config.productionAppOrigin + '/mini-apps/' + appId);
    await parent.wait(`document.body.textContent.includes('Đã kết nối SDK · Production')`, 'replacement launch and disposal');
    state = await fixture('production-state'); assert.equal(state.sessions, before.sessions + 3);
    for (let attempt = 0; attempt < 50 && state.activeSessions !== 1; attempt++) { await delay(100); state = await fixture('production-state'); }
    assert.equal(state.activeSessions, 1, 'Replaced host closed its original server session');
    samples.push(await lifecycleSample(parent, 'after-replace'));
    await recordProductionPerformance({ fixture, config, samples, initialLaunchMs,
      logoRequests: logoStart === undefined ? null : partnerServer.logoRequests() - logoStart });
    assert.equal(parent.issues.length, 0, 'Real parent runtime errors/warnings');
    phase = 'page close'; await parent.close(); parent = undefined;
    for (let attempt = 0; attempt < 50 && state.activeSessions !== 0; attempt++) { await delay(100); state = await fixture('production-state'); }
    assert.equal(state.activeSessions, 0, 'Closing the real page disposes its server session');
    console.log('STAGE8_WEB_REAL_PUBLICATION_CATALOG_ANONYMOUS_LAUNCH_REDEEM_V1_CONTEXT_CLOSE_FRESH_REOPEN_' + config.partnerClientType.toUpperCase());
  } catch (error) {
    console.error('STAGE8_WEB_PIPELINE_FAILED_AT_' + phase.replaceAll(' ', '_')); throw error;
  } finally {
    await parent?.close(); await portal.close();
  }
}
