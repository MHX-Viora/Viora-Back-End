import assert from 'node:assert/strict';
import { setTimeout as delay } from 'node:timers/promises';
import { page } from '../MiniAppReviewE2E/browser-helpers.mjs';
import { fixtureFetch } from '../SandboxExecutor/fixture-fetch.mjs';
import { startProductionPortal } from './portals.fixture.mjs';
const control = label => `[...document.querySelectorAll('button,[role=button]')].find(e=>(e.getAttribute('aria-label')||e.textContent.trim())===${JSON.stringify(label)})`;
export async function nonSsoWeb({ directory, config, ownerBrowser, fixture, appId }) {
  const portal = await startProductionPortal(directory, config), cdp = ownerBrowser.client;
  let parent, remove;
  try {
    const review = await fixture('review-state'); assert.equal(review.productionClientId, null, 'Non-SSO publication has no Production OAuth client');
    const response = await fixtureFetch(new URL('/api/mini-apps/' + appId, config.issuer));
    assert.equal(response.status, 200); const detail = await response.json(); assert.equal(detail.loginAvailable, false);
    parent = await page(cdp, config.productionAppOrigin + '/mini-apps/' + appId);
    await parent.wait(`document.body.textContent.includes('Đã kết nối SDK · Production')`, 'anonymous non-SSO deep link');
    assert.equal((await fixture('production-state')).anonymousSessions, 1);
    await parent.navigate(config.productionAppOrigin + '/login');
    await parent.wait(`!!document.querySelector('input[autocomplete=username]')`);
    await parent.type('input[autocomplete=username]', 'isolated-user@example.test');
    await parent.type('input[type=password]', 'Isolated Social password 2026!');
    await parent.click(`[...document.querySelectorAll('button,[role=button]')].find(e=>[...e.querySelectorAll('div,span')].some(text=>text.textContent==='Đăng nhập'))`);
    await parent.wait(`location.pathname!=='/login'&&location.pathname!=='/complete-profile'`, 'actual Social password login');
    let walletReads = 0;
    await parent.call('Network.enable');
    remove = cdp.on(event => { if (event.method === 'Network.responseReceived' &&
      event.params.response.status === 200 && new URL(event.params.response.url).pathname === '/api/wallet') walletReads++; });
    await parent.navigate(config.productionAppOrigin + '/utilities');
    await parent.wait(`!!${control('Nạp tiền')}&&${control('Nạp tiền')}.getAttribute('aria-disabled')!=='true'`, 'real Wallet balance loaded');
    await parent.wait(`!!document.querySelector('[aria-label^="Mở "]')`, 'real published non-SSO catalog');
    assert.equal(await parent.read(`(()=>{const headings=[...document.querySelectorAll('[role=heading],h1,h2')];return headings.findIndex(e=>e.textContent==='Ví ANKT')<headings.findIndex(e=>e.textContent==='Mini App')})()`), true, 'Catalog follows actual Wallet');
    const social = await fixture('sso-state'), reads = walletReads;
    await parent.click(`document.querySelector('[aria-label^="Mở "]')`);
    await parent.wait(`document.body.textContent.includes('Đã kết nối SDK · Production')`, 'authenticated non-SSO launch from Utilities');
    assert.equal((await fixture('production-state')).authenticatedSessions, 1);
    await parent.click(control('Quay về ANKT'));
    await parent.wait(`location.pathname==='/utilities'&&!!${control('Nạp tiền')}`, 'return to Utilities');
    await parent.wait(`${control('Nạp tiền')}.getAttribute('aria-disabled')!=='true'`);
    for (let attempt = 0; attempt < 30 && walletReads < reads + 1; attempt++) await delay(100);
    assert.equal(walletReads, reads + 1, 'Wallet performs one focus refresh on return');
    assert.deepEqual(await fixture('sso-state'), social, 'Closing a Mini App preserves Social sessions');
    assert.equal((await fixture('production-state')).wallets, 1, 'Real Wallet service created one persistent wallet');
    const changeProof = async kind => {
      const response = await fixtureFetch(new URL('/fixture/review-safety/' + kind, config.issuer), { method: 'POST',
        headers: { Authorization: 'SandboxExecutor ' + config.executorKey } }); assert.equal(response.status, 200);
    };
    await changeProof('expire-domain'); await parent.navigate(config.productionAppOrigin + '/utilities');
    await parent.wait(`document.body.textContent.includes('Chưa có Mini App khả dụng')&&!!${control('Nạp tiền')}`);
    assert.equal(await parent.read(`${control('Nạp tiền')}.getAttribute('aria-disabled')!=='true'`), true, 'Wallet survives catalog eligibility failure');
    await changeProof('restore-domain');
    console.log('STAGE8_FLOW_A_REAL_NON_SSO_NO_PRODUCTION_CLIENT_ANONYMOUS_AND_SOCIAL_UTILITIES_WALLET_RETURN');
  } finally { remove?.(); await parent?.close(); await portal.close(); }
}
