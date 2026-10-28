import assert from 'node:assert/strict';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { join, resolve } from 'node:path';
import { createRequire } from 'node:module';
import { createHash, X509Certificate, randomUUID } from 'node:crypto';
import { setTimeout as delay } from 'node:timers/promises';
import { startPortal } from '../SandboxExecutor/portal.fixture.mjs';
import { startSsoPartner } from '../SandboxExecutor/partner-sso.fixture.mjs';
import { launchChrome } from '../SandboxExecutor/cdp.mjs';
import { fixtureFetch } from '../SandboxExecutor/fixture-fetch.mjs';
import { executorClient, executeJob } from '../SandboxExecutor/executor.mjs';
import { startAdmin } from './portals.fixture.mjs';
import { productionPartner } from './partner.fixture.mjs';
import { page, button } from './browser-helpers.mjs';

const directory = process.argv[2], config = JSON.parse(await readFile(join(directory, 'fixture.json'), 'utf8'));
assert.equal(config.reviewFixture, true); assert.equal(config.issuer, 'https://identity.ankt.example.com/');
const appId = 'app_' + 'e'.repeat(43), appPath = '/developers/console/apps/' + appId;
const partnerProduction = productionPartner(config);
let portal, partner, adminServer, ownerBrowser, adminBrowser;
const activeJobs = [];
let lostSubmitBody = null, submitReplyDropped = false;
let observedDeveloperToken;
const require = createRequire(import.meta.url);
const { proxyAccountRequest } = require(resolve(import.meta.dirname, '../../../viora/server/developer/account-proxy.cjs'));
process.once('message', message => {
  if (message?.type === 'stop') {
    process.exitCode = 1;
    void Promise.allSettled([ownerBrowser?.close(), adminBrowser?.close(), partner?.close(), portal?.close(), adminServer?.close()]);
  }
});
process.channel?.unref();
try {
  // Capture prepared credentials only inside this controlled backend transport.
  // Never inspect browser cookies/storage/credential DOM, or log any credential.
  portal = await startPortal(directory, config, async (url, options) => {
    const authorization = new Headers(options?.headers).get('Authorization');
    if (authorization?.startsWith('DeveloperSession ')) observedDeveloperToken = authorization.slice('DeveloperSession '.length);
    const response = await fixtureFetch(url, options);
    if (new URL(url).pathname.endsWith('/reviews') && options?.method === 'POST') console.log('REVIEW_SUBMISSION_HTTP_' + response.status);
    if (new URL(url).pathname.endsWith('/reviews') && options?.method === 'POST' && response.ok) {
      if (!submitReplyDropped) {
        submitReplyDropped = true; lostSubmitBody = String(options.body);
        return new Response(JSON.stringify({ error: { code: 'FIXTURE_LOST_REPLY', message: 'Controlled lost reply after real commit.' } }), { status: 503, headers: { 'Content-Type': 'application/json' } });
      }
      if (lostSubmitBody !== null) { assert.equal(String(options.body), lostSubmitBody, 'Lost-reply retry must reuse the exact payload'); lostSubmitBody = null; console.log('REVIEW_LOST_REPLY_EXACT_REQUEST_REPLAY'); }
    }
    if (new URL(url).pathname.includes('/reviews') && !response.ok) {
      let code = 'UNKNOWN'; try { const value = (await response.clone().json()).error?.code; if (/^[A-Z_]{1,64}$/.test(value)) code = value; } catch {}
      console.log('REVIEW_FIXTURE_HTTP_' + (options?.method ?? 'GET') + '_' + response.status + '_' + code);
    }
    if (new URL(url).pathname.endsWith('/production-credentials') && response.ok) {
      const receipt = await response.clone().json(); partnerProduction.prepare(receipt.clientId, receipt.clientSecret);
    }
    return response;
  });
  partner = await startSsoPartner(directory, config, partnerProduction); adminServer = await startAdmin(directory);
  const certificate = new X509Certificate(await readFile(join(directory, 'tls-cert.pem')));
  const args = ['--ignore-certificate-errors-spki-list=' + createHash('sha256').update(certificate.publicKey.export({ type: 'spki', format: 'der' })).digest('base64'), '--host-resolver-rules=MAP partner.ankt.example.com 127.0.0.1, MAP identity.ankt.example.com 127.0.0.1', '--no-proxy-server'];
  ownerBrowser = await launchChrome('C:/Program Files/Google/Chrome/Application/chrome.exe', args);
  adminBrowser = await launchChrome('C:/Program Files/Google/Chrome/Application/chrome.exe', args);
  const owner = await page(ownerBrowser.client, config.sandboxPortalOrigin + appPath + '?tab=review');
  await owner.wait(`!!document.querySelector('input[type=email]')`);
  await owner.type('input[type=email]', 'executor@fixture.test'); await owner.type('input[type=password]', 'Sandbox fixture password 2026!'); await owner.click(`document.querySelector('button[type=submit]')`);
  await owner.wait(`location.pathname==='/developers/console'`);
  console.log('REVIEW_REAL_DEVELOPER_BFF_LOGIN');
  const admin = await page(adminBrowser.client, 'https://localhost:5495/mini-app-reviews');
  await admin.wait(`!!document.querySelector('input[autocomplete=username]')`);
  await admin.type('input[autocomplete=username]', 'review-admin@fixture.test'); await admin.type('input[type=password]', 'Isolated review password 2026!'); await admin.click(`document.querySelector('button[type=submit]')`);
  await admin.wait(`location.pathname==='/'`); await admin.navigate('https://localhost:5495/mini-app-reviews');
  await admin.wait(`document.body.textContent.includes('Không có yêu cầu')`); console.log('REVIEW_REAL_ADMIN_PASSWORD_LOGIN_LIVE_SESSION');
  const fixture = async path => { const response = await fixtureFetch(new URL('/fixture/' + path, config.issuer), { headers: { Authorization: 'SandboxExecutor ' + config.executorKey } }); assert.ok(response.ok); return response.json(); };
  const fixtureSafety = async kind => { assert.ok(['expire-domain', 'restore-domain', 'revoke-client'].includes(kind)); const response = await fixtureFetch(new URL('/fixture/review-safety/' + kind, config.issuer), { method: 'POST', headers: { Authorization: 'SandboxExecutor ' + config.executorKey } }); assert.ok(response.ok); return response.json(); };
  async function rejectPublicationTarget(versionId) {
    assert.match(versionId, /^[a-f0-9-]{36}$/); assert.ok(observedDeveloperToken);
    const proxyConfig = { siteOrigin: config.sandboxPortalOrigin, apiOrigin: config.issuer, production: true, bffSecret: config.bffKey, clientIp: '127.0.0.1', token: observedDeveloperToken };
    const path = 'mini-apps/' + appId, headers = { Origin: config.sandboxPortalOrigin, 'X-ANKT-Developer': '1', 'X-ANKT-Client-IP': '127.0.0.1', 'Content-Type': 'application/json' };
    const readiness = await proxyAccountRequest(new Request(config.sandboxPortalOrigin + '/?versionId=' + versionId, { headers }), path + '/publication-readiness', proxyConfig, fixtureFetch);
    assert.equal(readiness.status, 200); const ready = await readiness.json(); assert.equal(ready.canPublish, false);
    const response = await proxyAccountRequest(new Request(config.sandboxPortalOrigin + '/', { method: 'POST', headers, body: JSON.stringify({ requestId: randomUUID(), concurrencyToken: ready.appVersion, versionId, publicationId: ready.publicationId, publicationRevision: ready.publicationRevision, operationalRevision: ready.operationalRevision }) }), path + '/publish', proxyConfig, fixtureFetch);
    assert.equal(response.status, 409); console.log('REVIEW_REJECTED_TARGET_REAL_BFF_PUBLISH_DENIED_409');
  }
  async function prepareCredential() {
    if (config.partnerClientType !== 'Confidential') return;
    const summary = `[...document.querySelectorAll('summary')].find(x=>x.textContent==='Chỉnh sửa cấu hình Production dự kiến')`;
    if (!await owner.read(`${summary}?.closest('details')?.open`)) await owner.click(summary);
    await owner.wait(`!!${button('Chuẩn bị credential Production')} && !${button('Chuẩn bị credential Production')}.disabled`);
    await owner.click(button('Chuẩn bị credential Production')); await owner.click(button('Xác nhận chuẩn bị credential'));
    await owner.wait(`!!${button('Ẩn credential và kiểm tra lại')}`); await owner.click(button('Ẩn credential và kiểm tra lại'));
    await owner.wait(`!${button('Ẩn credential và kiểm tra lại')}`);
  }
  async function createVersion(name, configure = false) {
    await owner.navigate(config.sandboxPortalOrigin + appPath + '?tab=review'); await owner.wait(`!!document.querySelector('#review-version-name')`);
    await owner.type('#review-version-name', name); await owner.click(button('Tạo bản nháp mới'));
    await owner.wait(`!!document.querySelector('#review-version') && !document.querySelector('#review-version-name')`);
    console.log('REVIEW_WORKING_VERSION_CREATED');
    if (configure) {
      await owner.click(`[...document.querySelectorAll('summary')].find(x=>x.textContent==='Chỉnh sửa cấu hình Production dự kiến')`);
      await owner.wait(`!!document.querySelector('#production-callbacks')`);
      await owner.select('#production-type', config.partnerClientType === 'Confidential' ? 1 : 0);
      await owner.type('#production-callbacks', 'https://partner.ankt.example.com/production/callback' +
        (config.productionFixture ? '\nhttps://partner.ankt.example.com/production/embedded-callback' : ''));
      if (config.productionFixture) {
        await owner.focus(`[...document.querySelectorAll('label')].find(x=>x.textContent.trim()==='profile').querySelector('input')`);
        await owner.key(' ');
      }
      await owner.click(button('Lưu cấu hình vào bản nháp'));
      await owner.wait(`document.querySelector('#production-callbacks')?.closest('fieldset')?.disabled===false && ${button('Lưu cấu hình vào bản nháp')}?.disabled===true`, 'Production draft save completed');
      console.log('REVIEW_PRODUCTION_INTENT_SAVED');
    }
    await prepareCredential();
    const state = await fixture('review-state'); partnerProduction.prepare(state.productionClientId); return state.versions[0];
  }
  const firstVersion = await createVersion('Review E2E 1', !config.nonSsoPartner);
  const sandbox = await page(ownerBrowser.client, config.sandboxPortalOrigin + appPath + '/sandbox');
  const request = executorClient(config.issuer, config.executorKey, fixtureFetch);
  let socialAuthenticated = false;
  async function findPartner() {
    for (let i = 0; i < 100; i++) {
      const target = (await ownerBrowser.client.send('Target.getTargets')).targetInfos.reverse().find(x => x.type === 'iframe' && x.url.startsWith('https://partner.ankt.example.com/'));
      if (target) { const result = await page(ownerBrowser.client, null, target.targetId); await result.wait(`document.body.dataset.fixtureReady==='true' && document.visibilityState==='visible'`); return result; }
      await delay(100);
    }
    throw Error('Actual Sandbox partner frame unavailable');
  }
  async function collect() {
    await ownerBrowser.client.send('Target.activateTarget', { targetId: sandbox.targetId });
    await sandbox.wait(`!!${button('Bắt đầu phiên Sandbox')}||!!${button('Dừng phiên')}`);
    if (await sandbox.read(`!!${button('Dừng phiên')}`)) { await sandbox.click(button('Dừng phiên')); await sandbox.wait(`!!${button('Bắt đầu phiên Sandbox')}`); }
    await sandbox.click(button('Bắt đầu phiên Sandbox')); await sandbox.wait(`document.body.textContent.includes('Đã kết nối bridge')`);
    const frame = await findPartner(); await sandbox.click(button('Chạy 18 kiểm tra')); await sandbox.wait(`!!${button('Hủy lượt kiểm tra')}`);
    let jobs = []; for (let i = 0; i < 50 && !jobs.length; i++) { jobs = await request('runs'); if (!jobs.length) await delay(100); }
    assert.equal(jobs.length, 1);
    const trusted = executeJob(jobs[0], { chrome: 'C:/Program Files/Google/Chrome/Application/chrome.exe', request, fixtureDirectory: directory }); activeJobs.push(trusted); void trusted.catch(() => {});
    if (!config.nonSsoPartner) {
    if (config.partnerClientType === 'Public') await frame.wait(`document.querySelector('#run-ready')?.textContent==='ready'`);
    const before = new Set((await ownerBrowser.client.send('Target.getTargets')).targetInfos.map(x => x.targetId));
    await frame.click(`document.querySelector(${JSON.stringify(config.partnerClientType === 'Public' ? '#force' : '#popup-login')})`);
    let popup;
    for (let i = 0; i < 100 && !popup; i++) { const target = (await ownerBrowser.client.send('Target.getTargets')).targetInfos.find(x => x.type === 'page' && !before.has(x.targetId)); if (target) popup = await page(ownerBrowser.client, null, target.targetId); else await delay(100); }
    if (popup) {
      if (!socialAuthenticated) { await popup.wait(`!!document.querySelector('form[action="/fixture/login"] button')`); await popup.click(`document.querySelector('form[action="/fixture/login"] button')`); socialAuthenticated = true; }
      for (let i = 0; i < 100; i++) {
        if (await frame.read(`document.querySelector('#status')?.textContent==='authenticated'`)) break;
        try { if (await popup.read(`!!document.querySelector('button[name=decision][value=allow]')`)) await popup.click(`document.querySelector('button[name=decision][value=allow]')`); } catch {}
        await delay(100);
      }
    }
    await frame.wait(`document.querySelector('#status')?.textContent==='authenticated'`);
    }
    assert.equal((await trusted).status, 'Passed');
    // Browser evidence is only one input. Wait for the server coordinator to
    // finish all checks before requesting a fresh readiness assessment.
    await sandbox.wait(`[...document.querySelectorAll('[role=status]')].some(x=>x.textContent==='Đã hoàn tất · 18/18 kiểm tra đã xử lý')`, 'All 18 server checks completed');
    await sandbox.wait(`!!${button('Kiểm tra readiness')} && !${button('Kiểm tra readiness')}.disabled`);
    await sandbox.click(button('Kiểm tra readiness'));
    await sandbox.wait(`document.querySelector('.sandbox-readiness div:last-child dd')?.textContent==='Đủ bằng chứng'`);
    console.log('REVIEW_ACTUAL_18_STAGE6_CHECKS_' + config.partnerClientType.toUpperCase());
  }
  await collect();
  async function submit() {
    await owner.navigate(config.sandboxPortalOrigin + appPath + '?tab=review'); await owner.wait(`!!${button('Gửi phiên bản xét duyệt')} && !${button('Gửi phiên bản xét duyệt')}.disabled`);
    await owner.click(button('Gửi phiên bản xét duyệt')); await owner.click(button('Xác nhận gửi xét duyệt'));
    await owner.wait(`/đang chờ xét duyệt/i.test(document.body.textContent)||!!${button('Thử lại cùng yêu cầu')}`);
    if (await owner.read(`!!${button('Thử lại cùng yêu cầu')}`)) await owner.click(button('Thử lại cùng yêu cầu'));
    await owner.wait(`/đang chờ xét duyệt/i.test(document.body.textContent)`); console.log('REVIEW_SUBMITTED_THROUGH_OWNED_BFF'); return (await fixture('review-state')).reviews[0];
  }
  let staleAdminDialogChecked = false;
  async function decide(review, action, reason) {
    await admin.navigate('https://localhost:5495/mini-app-reviews/' + review.id);
    await admin.wait(`!!${button('Bắt đầu xét duyệt')}`); await admin.click(button('Bắt đầu xét duyệt'));
    await admin.wait(`document.querySelector('dialog[open]')?.contains(document.activeElement)`);
    assert.equal(await admin.read(`document.activeElement.textContent.trim()`), 'Hủy');
    for (let i = 0; i < 3; i++) {
      await admin.key('Tab');
      // Chrome reports BODY between native modal tab cycles. No background
      // interactive control may receive focus while the modal is open.
      await admin.wait(`document.activeElement===document.body || document.querySelector('dialog[open]').contains(document.activeElement)`);
    }
    await admin.focus(`document.querySelector('dialog[open] button[type=button]')`);
    await admin.key('Escape'); await admin.wait(`!document.querySelector('dialog[open]')`);
    await admin.wait(`document.activeElement===${button('Bắt đầu xét duyệt')}`, 'Escape restores review trigger focus');
    await admin.click(button('Bắt đầu xét duyệt'));
    if (config.partnerClientType === 'Confidential' && !staleAdminDialogChecked) {
      const competing = await page(adminBrowser.client, 'https://localhost:5495/mini-app-reviews/' + review.id);
      await competing.wait(`!!${button('Bắt đầu xét duyệt')}`);
      await competing.click(button('Bắt đầu xét duyệt')); await competing.click(button('Xác nhận bắt đầu xét duyệt'));
      await competing.wait(`!document.querySelector('dialog[open]') && !!${button('Phê duyệt')}`);
      await admin.click(button('Xác nhận bắt đầu xét duyệt'));
      await admin.wait(`!!${button('Đóng & kiểm tra trạng thái mới')}`);
      await admin.click(button('Đóng & kiểm tra trạng thái mới'));
      await admin.wait(`!document.querySelector('dialog[open]') && document.activeElement?.tagName==='H2'`, 'Stale dialog closes to a live fallback heading');
      await competing.close(); staleAdminDialogChecked = true;
      console.log('REVIEW_STALE_ADMIN_DIALOG_CANNOT_OVERWRITE_NEW_STATE_AND_RESTORES_LIVE_FOCUS');
    } else await admin.click(button('Xác nhận bắt đầu xét duyệt'));
    await admin.wait(`!document.querySelector('dialog[open]') && !!${button('Phê duyệt')}`);
    const label = { approve: 'Phê duyệt', reject: 'Từ chối', 'request-changes': 'Yêu cầu chỉnh sửa' }[action];
    await admin.click(button(label)); await admin.wait(`!!document.querySelector('dialog[open]')`);
    if (reason) await admin.type('dialog textarea[name=reason]', reason);
    if (action === 'request-changes') await admin.type('dialog textarea:not([name])', 'Please correct the reviewed application name.');
    await admin.click(button('Xác nhận ' + label.toLocaleLowerCase('vi-VN'))); await admin.wait(`!document.querySelector('dialog[open]')`);
    assert.equal((await fixture('review-state')).reviews.find(x => x.id === review.id).status, { approve: 'Approved', reject: 'Rejected', 'request-changes': 'ChangesRequested' }[action]);
    console.log('REVIEW_ADMIN_DECISION_' + action.toUpperCase());
  }
  async function publish(versionCode) {
    await owner.navigate(config.sandboxPortalOrigin + appPath + '?tab=review'); await owner.wait(`!!${button('Phát hành phiên bản #' + versionCode)} && !${button('Phát hành phiên bản #' + versionCode)}.disabled`);
    await owner.click(button('Phát hành phiên bản #' + versionCode)); await owner.click(button('Xác nhận'));
    await owner.wait(`document.body.textContent.includes('Phiên bản đang phát hành: #${versionCode}')`);
  }
  async function productionLogin(hold = false, expectedName) {
    if (hold) partnerProduction.holdNext();
    const login = await page(ownerBrowser.client, 'https://partner.ankt.example.com/production/login');
    await login.wait(`!!document.querySelector('button[name=decision][value=allow]')`);
    assert.equal(await login.read(`document.querySelector('[data-environment]')?.getAttribute('data-environment')`), 'Production');
    if (expectedName) assert.equal(await login.read(`document.querySelector('#consent-title')?.textContent.includes(${JSON.stringify(expectedName)})`), true);
    await login.click(`document.querySelector('button[name=decision][value=allow]')`);
    await login.wait(`document.querySelector('#status')?.textContent===${JSON.stringify(hold ? 'held_authorization' : 'production_authenticated')}`);
    await login.close(); if (!hold) assert.equal(await partnerProduction.userInfoWorks(), true);
  }
  const first = await submit(); await decide(first, 'approve');
  await owner.navigate(config.sandboxPortalOrigin + '/developers/console/notifications'); await owner.wait(`document.body.textContent.includes('duyệt')`);
  assert.ok((await fixture('review-state')).notifications >= 3); await publish(1);
  if (config.nonSsoPartner) await (await import('../MiniAppProductionE2E/non-sso.e2e.mjs')).nonSsoWeb({ directory, config, ownerBrowser, fixture, appId });
  else {
    if (config.productionFixture) await (await import('../MiniAppProductionE2E/web-host.e2e.mjs')).productionWebHost({ directory, config, ownerBrowser, fixture, appId, partnerProduction, partnerServer: partner });
    await productionLogin();
    console.log('REVIEW_FLOW_A_APPROVED_EXACT_VERSION_INBOX_REAL_PRODUCTION_OIDC_' + config.partnerClientType.toUpperCase());
  }
  const screenshots = join(import.meta.dirname, '../../.codex-tmp/stage7-admin-screenshots'); await mkdir(screenshots, { recursive: true });
  for (const width of [320, 375, 768, 1440]) {
    await admin.call('Emulation.setDeviceMetricsOverride', { width, height: 1000, deviceScaleFactor: 1, mobile: false });
    await admin.key('Home', 2);
    assert.equal(await admin.read(`document.documentElement.scrollWidth<=innerWidth`), true, 'Admin overflow at ' + width);
    await writeFile(join(screenshots, 'review-' + width + '.png'), Buffer.from((await admin.call('Page.captureScreenshot', { format: 'png' })).data, 'base64'));
  }
  assert.equal(admin.issues.length, 0, 'Admin runtime console issues');
  console.log('REVIEW_ADMIN_RESPONSIVE_320_375_768_1440_ZERO_RUNTIME_ERRORS');
  if (process.argv.includes('--stage10-baseline'))
    await (await import('../MiniAppProductionE2E/load.e2e.mjs')).boundedBaseline({ config, appId, fixture });
  // The remaining scenarios share these real sessions and business operations.
  const context = { directory, ownerBrowser, owner, admin, sandbox, fixture, fixtureSafety, rejectPublicationTarget, firstVersion, appId, appPath, config, createVersion, prepareCredential, collect, submit, decide, publish, productionLogin, partnerProduction, button };
  if (config.productionFixture && process.argv.includes('--runtime-security'))
    await (await import('../MiniAppProductionE2E/moderation.e2e.mjs')).productionModeration(context);
  if (process.argv.includes('--all-flows')) {
    await (await import('./decisions.e2e.mjs')).decisions(context);
    await (await import('./moderation.e2e.mjs')).moderation(context);
    await (await import('./isolation.e2e.mjs')).staleSources(context);
  }
  assert.equal(admin.issues.length, 0, 'Admin final runtime console issues');
  console.log('REVIEW_ADMIN_KEYBOARD_MODAL_FOCUS_ESCAPE_AND_FINAL_RUNTIME_CLEAN');
} finally {
  await ownerBrowser?.close(); await adminBrowser?.close(); await Promise.allSettled(activeJobs);
  await partner?.close(); await portal?.close(); await adminServer?.close();
  await fixtureFetch(new URL('/fixture/executor-shutdown', config.issuer), { method: 'POST', headers: { Authorization: 'SandboxExecutor ' + config.executorKey } }).catch(() => {});
  if (process.connected) process.disconnect();
}
