import { test, after } from 'node:test';
import assert from 'node:assert/strict';
import { once } from 'node:events';
import { request } from 'node:http';

for (const name of ['NODE_ENV', 'MINI_APP_ORIGIN', 'ANKT_API_URL', 'ANKT_APP_ORIGIN']) delete process.env[name];
process.env.ANKT_CLIENT_ID = 'test-three-variable-client';
process.env.ANKT_CLIENT_SECRET = 'synthetic-backend-only-secret';
process.env.ANKT_REDIRECT_URI = 'https://nghethuatso.example/social/ankt';
process.env.PORT = '5392';
process.env.MINI_APP_DATABASE = ':memory:';
const {server, db} = await import('./server.mjs');
if (!server.listening) await once(server, 'listening');
after(async () => { await new Promise(resolve => server.close(resolve)); db.close(); });
function fetch(url, options = {}) {
  return new Promise((resolve, reject) => {
    const req = request(url, {method:options.method, headers:options.headers}, res => {
      let body = '';
      res.setEncoding('utf8');
      res.on('data', chunk => { body += chunk; });
      res.on('end', () => resolve({status:res.statusCode, text:async () => body, headers:{get:name => Array.isArray(res.headers[name]) ? res.headers[name].join('; ') : res.headers[name]}}));
    });
    req.on('error', reject);
    req.end(options.body?.toString());
  });
}

test('three-variable server uses HTTPS cookies, official SDK and custom callback without exposing secret', async () => {
  const headers = {host:'nghethuatso.example'};
  const page = await fetch('http://127.0.0.1:5392/', {headers});
  assert.equal(page.status, 200);
  const html = await page.text();
  const cookie = page.headers.get('set-cookie');
  assert.match(cookie, /__Host-mini-session=/);
  assert.match(cookie, /Secure/);
  assert.match(cookie, /HttpOnly/);
  const csrf = html.match(/name="csrf" value="([^"]+)"/)[1];
  const start = await fetch('http://127.0.0.1:5392/auth/ankt/start', {method:'POST', headers:{...headers, origin:'https://nghethuatso.example', cookie:cookie.split(';')[0], 'content-type':'application/x-www-form-urlencoded'}, body:new URLSearchParams({csrf, intent:'login'})});
  assert.equal(start.status, 200);
  const login = await start.text();
  assert.match(login, /https:\/\/api\.mxh\.ankt\.vn\/mini-app-sdk\/ankt-mini-app\.js/);
  assert.match(login, /hostOrigin:"https:\/\/app\.ankt\.vn"/);
  assert.match(login, /target\.pathname!=="\/social\/ankt"/);
  assert.ok(!login.includes(process.env.ANKT_CLIENT_SECRET));
  assert.equal((await fetch('http://127.0.0.1:5392/social/ankt?code=invalid&state=invalid', {headers})).status, 400);
  assert.equal((await fetch('http://127.0.0.1:5392/auth/ankt/callback', {headers})).status, 404);
});
