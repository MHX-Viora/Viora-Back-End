import assert from 'node:assert/strict';
import { page } from '../MiniAppReviewE2E/browser-helpers.mjs';
import { fixtureFetch } from '../SandboxExecutor/fixture-fetch.mjs';
import { startProductionPortal } from './portals.fixture.mjs';
export async function productionModeration(ctx) {
  const { directory, config, ownerBrowser, admin, appId, fixture, button, publish, productionLogin, partnerProduction } = ctx;
  const portal = await startProductionPortal(directory, config);
  let parent;
  const catalogHasApp = async () => {
    const response = await fixtureFetch(new URL('/api/mini-apps?page=1&pageSize=20', config.issuer));
    assert.equal(response.status, 200); return (await response.json()).items.some(item => item.id === appId);
  };
  const failed = `document.body.textContent.includes('Không thể mở Mini App.')`;
  try {
    await productionLogin(true); assert.equal(partnerProduction.hasHeld(), true);
    const social = await fixture('sso-state'), before = await fixture('production-state');
    parent = await page(ownerBrowser.client, config.productionAppOrigin + '/mini-apps/' + appId);
    await parent.wait(`document.body.textContent.includes('Đã kết nối SDK · Production')`);
    assert.equal((await fixture('production-state')).activeSessions, 1);
    await admin.navigate('https://localhost:5495/mini-apps/' + appId);
    await admin.wait(`!!${button('Tạm ngưng ứng dụng')}`); await admin.click(button('Tạm ngưng ứng dụng'));
    await admin.type('dialog textarea[name=reason]', 'Stage 8 active runtime suspension fixture.');
    await admin.click(button('Xác nhận tạm ngưng ứng dụng'));
    await admin.wait(`!document.querySelector('dialog[open]')&&!!${button('Khôi phục ứng dụng')}`);
    assert.equal(await catalogHasApp(), false);
    assert.equal((await fixtureFetch(new URL('/api/mini-apps/' + appId, config.issuer))).status, 404);
    await parent.wait(`document.body.textContent.includes('Không thể xác minh phiên Mini App.')`, 'active runtime stops on live suspension');
    assert.equal(await parent.read('document.querySelectorAll("iframe").length'), 0);
    assert.equal((await fixture('production-state')).activeSessions, 0);
    assert.equal(await partnerProduction.userInfoWorks(), false);
    await assert.rejects(partnerProduction.exchangeHeld());
    if (config.partnerClientType === 'Confidential') assert.equal(await partnerProduction.introspectionActive(), false);
    await parent.click(`[...document.querySelectorAll('[role=button]')].find(e=>e.textContent.trim()==='Thử lại')`);
    await parent.wait(failed, 'new opening denied while suspended');
    await admin.click(button('Khôi phục ứng dụng'));
    await admin.type('dialog textarea[name=reason]', 'Stage 8 recovery must remain unpublished.');
    await admin.click(button('Xác nhận khôi phục ứng dụng'));
    await admin.wait(`!document.querySelector('dialog[open]')&&!!${button('Tạm ngưng ứng dụng')}`);
    assert.equal(await catalogHasApp(), false, 'Restore cannot republish');
    assert.equal((await fixture('production-state')).activeSessions, 0);
    await publish(1);
    assert.equal(await catalogHasApp(), true);
    await parent.click(`[...document.querySelectorAll('[role=button]')].find(e=>e.textContent.trim()==='Thử lại')`);
    await parent.wait(`document.body.textContent.includes('Đã kết nối SDK · Production')`, 'fresh runtime after explicit republish');
    const after = await fixture('production-state');
    assert.equal(after.sessions, before.sessions + 2); assert.equal(after.activeSessions, 1);
    assert.equal(await partnerProduction.userInfoWorks(), false, 'Old partner token does not resurrect');
    assert.deepEqual(await fixture('sso-state'), social, 'Moderation preserves unrelated Social state');
    console.log('STAGE8_FLOW_C_ACTIVE_HOST_SUSPEND_CATALOG_LAUNCH_TOKEN_CODE_DENIAL_RESTORE_UNPUBLISHED_REPUBLISH_' + config.partnerClientType.toUpperCase());
  } finally { await parent?.close(); await portal.close(); }
}
