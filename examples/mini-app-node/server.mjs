import { createServer } from 'node:http';
import { randomBytes, randomUUID, createHash, scryptSync, timingSafeEqual } from 'node:crypto';
import { DatabaseSync } from 'node:sqlite';
import { initializeIdentitySchema, resolveIdentity, unlinkIdentity } from './identity-store.mjs';
import { loadAnktConfig } from './ankt-config.mjs';

const { production, origin, anktApi, anktHost, clientId, clientSecret, callback, callbackPath } = loadAnktConfig(process.env);
const db = new DatabaseSync(process.env.MINI_APP_DATABASE || 'mini-app.sqlite');
initializeIdentitySchema(db);
const token = () => randomBytes(32).toString('base64url');
const hash = value => createHash('sha256').update(value).digest('base64url');
const escape = value => String(value).replace(/[&<>"']/g, char => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[char]));
const cookieName = production ? '__Host-mini-session' : 'mini-session';
const passwordHash = password => { const salt = randomBytes(16).toString('hex'); return `${salt}:${scryptSync(password, salt, 64).toString('hex')}`; };
function verifyPassword(password, stored) {
  if (typeof password !== 'string' || password.length > 256 || !stored) return false;
  const [salt, digest] = stored.split(':'); if (!salt || !digest) return false;
  const expected = Buffer.from(digest, 'hex'); const actual = scryptSync(password, salt, 64);
  return expected.length === actual.length && timingSafeEqual(expected, actual);
}
function setCookie(res, raw) {
  res.setHeader('Set-Cookie', `${cookieName}=${raw}; HttpOnly; Path=/; SameSite=${origin.protocol === 'https:' ? 'None' : 'Lax'}; Max-Age=3600${origin.protocol === 'https:' ? '; Secure' : ''}`);
}
function newSession(res, userId = null) {
  const raw = token(); const idHash = hash(raw); const csrf = token();
  db.prepare('INSERT INTO sessions VALUES (?, ?, ?, ?)').run(idHash, userId, csrf, Date.now() + 3600000);
  setCookie(res, raw); return {id_hash: idHash, user_id: userId, csrf};
}
function session(req, res) {
  const cookie = (req.headers.cookie || '').split(';').map(part => part.trim()).find(part => part.startsWith(`${cookieName}=`))?.slice(cookieName.length + 1);
  const found = cookie && /^[A-Za-z0-9_-]{43}$/.test(cookie) ? db.prepare('SELECT * FROM sessions WHERE id_hash = ? AND expires_at > ?').get(hash(cookie), Date.now()) : null;
  return found || newSession(res);
}
function rotateSession(res, old, userId) {
  db.prepare('DELETE FROM transactions WHERE session_hash = ?').run(old.id_hash);
  db.prepare('DELETE FROM sessions WHERE id_hash = ?').run(old.id_hash);
  return newSession(res, userId);
}
const attempts = new Map();
function rateLimit(req) {
  const key = req.socket.remoteAddress; const now = Date.now(); let value = attempts.get(key);
  if (!value || now - value.start > 60000) { value = {start: now, count: 0}; attempts.set(key, value); }
  if (++value.count > 30) { const error = new Error('Too many requests; try again shortly'); error.status = 429; throw error; }
  if (attempts.size > 10000) for (const [address, entry] of attempts) if (now - entry.start > 60000) attempts.delete(address);
}
async function form(req, current) {
  if (req.headers.origin !== origin.origin || !String(req.headers['content-type'] || '').startsWith('application/x-www-form-urlencoded')) throw Object.assign(new Error('Invalid request origin or content type'), {status: 403});
  let data = ''; for await (const chunk of req) { data += chunk; if (Buffer.byteLength(data) > 8192) throw Object.assign(new Error('Request too large'), {status: 413}); }
  const result = new URLSearchParams(data);
  const supplied = Buffer.from(result.get('csrf') || ''); const expected = Buffer.from(current.csrf);
  if (supplied.length !== expected.length || !timingSafeEqual(supplied, expected)) throw Object.assign(new Error('Invalid CSRF token'), {status: 403});
  return result;
}
function redirect(res, url = '/') { res.writeHead(303, {Location: url}); res.end(); }
function page(res, current, content, script = '') {
  const nonce = token();
  res.setHeader('Content-Security-Policy', `default-src 'none'; script-src 'nonce-${nonce}' ${anktApi.origin}; style-src 'nonce-${nonce}'; connect-src 'self'; img-src 'self'; base-uri 'none'; form-action 'self'; frame-ancestors ${anktHost}`);
  res.setHeader('Content-Type', 'text/html; charset=utf-8');
  res.end(`<!doctype html><html lang="vi"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>ANKT partner example</title><style nonce="${nonce}">body{font:16px system-ui;background:#f4f6f8;color:#19232f;max-width:700px;margin:40px auto;padding:24px}main{background:white;border:1px solid #dce2e8;border-radius:16px;padding:28px}form{display:grid;gap:12px;margin:20px 0}label{display:grid;gap:6px}input,button{font:inherit;padding:12px;border:1px solid #becbd7;border-radius:8px}button{background:#145a73;color:white;cursor:pointer}a{color:#145a73}small{color:#596777}#error{color:#a12323}</style><main>${content}</main>${script ? `<script nonce="${nonce}" src="${anktApi.origin}/mini-app-sdk/ankt-mini-app.js"></script><script nonce="${nonce}">${script}</script>` : ''}</html>`);
}
const csrfInput = current => `<input type="hidden" name="csrf" value="${escape(current.csrf)}">`;
const server = createServer(async (req, res) => {
  res.setHeader('Cache-Control', 'no-store'); res.setHeader('Referrer-Policy', 'no-referrer'); res.setHeader('X-Content-Type-Options', 'nosniff');
  if (production) res.setHeader('Strict-Transport-Security', 'max-age=31536000');
  try {
    if (req.headers.host !== origin.host) throw Object.assign(new Error('Invalid Host'), {status: 400});
    const url = new URL(req.url, origin); const current = session(req, res);
    db.prepare('DELETE FROM transactions WHERE expires_at <= ?').run(Date.now());
    const user = current.user_id && db.prepare('SELECT * FROM users WHERE id = ?').get(current.user_id);
    if (req.method === 'GET' && url.pathname === '/') {
      const passwordFields = '<label>Tên đăng nhập<input name="username" required minlength="3" maxlength="64" autocomplete="username"></label><label>Mật khẩu<input name="password" type="password" required minlength="12" maxlength="256" autocomplete="current-password"></label>';
      const html = user ? `<h1>${escape(user.display_name)}</h1><p>Đây là tài khoản và phiên đăng nhập riêng của mini app.</p><form method="post" action="/auth/ankt/start">${csrfInput(current)}<input type="hidden" name="intent" value="link"><label>Xác nhận mật khẩu để liên kết ANKT<input type="password" name="password" required autocomplete="current-password"></label><button>Liên kết tài khoản ANKT</button></form><form method="post" action="/account/unlink">${csrfInput(current)}<label>Xác nhận mật khẩu để hủy liên kết<input type="password" name="password" required autocomplete="current-password"></label><button>Hủy liên kết ANKT</button></form><form method="post" action="/logout">${csrfInput(current)}<button>Đăng xuất mini app</button></form>` : `<h1>Mini app đối tác</h1><p>Giữ đăng nhập riêng hoặc sử dụng danh tính ANKT.</p><form method="post" action="/login">${csrfInput(current)}${passwordFields}<button>Đăng nhập tài khoản có sẵn</button></form><form method="post" action="/register">${csrfInput(current)}${passwordFields}<button>Tạo tài khoản riêng</button></form><form method="post" action="/auth/ankt/start">${csrfInput(current)}<input type="hidden" name="intent" value="login"><button>Đăng nhập / đăng ký bằng ANKT</button></form>`;
      return page(res, current, html);
    }
    if (req.method === 'POST') {
      rateLimit(req); const input = await form(req, current);
      if (url.pathname === '/register' || url.pathname === '/login') {
        const username = input.get('username')?.trim(); const password = input.get('password');
        if (!username || !/^[\p{L}\p{N}_.-]{3,64}$/u.test(username) || !password || password.length < 12 || password.length > 256) throw Object.assign(new Error('Invalid username or password; use 12–256 characters'), {status: 400});
        let account = db.prepare('SELECT * FROM users WHERE username = ?').get(username);
        if (url.pathname === '/register') {
          if (account) throw Object.assign(new Error('Username unavailable'), {status: 409});
          const id = randomUUID(); db.prepare('INSERT INTO users VALUES (?, ?, ?, ?)').run(id, username, username, passwordHash(password)); account = {id};
        } else if (!account || !verifyPassword(password, account.password_hash)) throw Object.assign(new Error('Invalid credentials'), {status: 401});
        rotateSession(res, current, account.id); return redirect(res);
      }
      if (url.pathname === '/auth/ankt/start') {
        const intent = input.get('intent');
        if (!['login', 'link'].includes(intent)) throw Object.assign(new Error('Invalid intent'), {status: 400});
        if (intent === 'link' && (!user || !verifyPassword(input.get('password'), user.password_hash))) throw Object.assign(new Error('Authenticate your existing account before linking'), {status: 401});
        const state = token(); const verifier = token();
        db.prepare('INSERT INTO transactions VALUES (?, ?, ?, ?, ?, ?)').run(hash(state), current.id_hash, verifier, intent, intent === 'link' ? user.id : null, Date.now() + 300000);
        const params = JSON.stringify({redirectUri: callback, state, codeChallenge: hash(verifier), codeChallengeMethod: 'S256'}).replace(/</g, '\\u003c');
        return page(res, current, '<h1>Tiếp tục với ANKT</h1><p>Xác nhận quyền trong ANKT để tiếp tục.</p><button id="continue">Tiếp tục</button><p id="error" role="alert"></p><a href="/">Quay lại</a>', `ANKT.configure({hostOrigin:${JSON.stringify(anktHost)}});document.getElementById('continue').onclick=async()=>{try{if(!window.ANKT)throw new Error('Mở mini app này trong ANKT để đăng nhập.');const result=await ANKT.requestLogin(${params});const target=new URL(result.launchUrl);if(target.origin!==location.origin||target.pathname!==${JSON.stringify(callbackPath)})throw new Error('Invalid callback');location.replace(target.href)}catch(error){document.getElementById('error').textContent=error.message}};`);
      }
      if (url.pathname === '/account/unlink') {
        if (!user || !verifyPassword(input.get('password'), user.password_hash)) throw Object.assign(new Error('Reauthenticate to unlink'), {status: 401});
        unlinkIdentity(db, user.id); rotateSession(res, current, user.id); return redirect(res);
      }
      if (url.pathname === '/logout') { rotateSession(res, current, null); return redirect(res); }
    }
    if (req.method === 'GET' && url.pathname === callbackPath) {
      rateLimit(req);
      const state = url.searchParams.get('state'); const code = url.searchParams.get('code');
      if (!state || !code || state.length > 256 || code.length > 256) throw Object.assign(new Error('Invalid callback'), {status: 400});
      const transaction = db.prepare('SELECT * FROM transactions WHERE state_hash = ? AND session_hash = ? AND expires_at > ?').get(hash(state), current.id_hash, Date.now());
      if (!transaction || (transaction.intent === 'link' && transaction.user_id !== current.user_id)) throw Object.assign(new Error('SSO transaction expired or belongs to another session'), {status: 400});
      db.prepare('DELETE FROM transactions WHERE state_hash = ?').run(hash(state));
      const response = await fetch(new URL('/api/mini-app-auth/exchange', anktApi), {method: 'POST', headers: {'content-type': 'application/json'}, body: JSON.stringify({clientId, clientSecret, code, redirectUri: callback, state, codeVerifier: transaction.verifier}), redirect: 'error', signal: AbortSignal.timeout(10000)});
      if (!response.ok) throw Object.assign(new Error('ANKT authentication failed'), {status: 401});
      const body = await response.text(); if (body.length > 65536) throw new Error('Invalid identity response');
      const identity = JSON.parse(body); if (!Array.isArray(identity.scopes) || !identity.scopes.includes('identity.login')) throw Object.assign(new Error('identity.login permission is required'), {status: 403});
      const userId = resolveIdentity(db, identity.subject, identity.displayName, transaction.intent, transaction.user_id);
      rotateSession(res, current, userId); return redirect(res);
    }
    res.statusCode = 404; page(res, current, '<h1>Không tìm thấy trang</h1><a href="/">Trang chủ</a>');
  } catch (error) {
    res.statusCode = [400,401,403,409,413,429].includes(error.status) ? error.status : 500;
    page(res, null, `<h1>Không thể tiếp tục</h1><p role="alert">${escape(res.statusCode === 500 ? 'Đã xảy ra lỗi. Vui lòng thử lại.' : error.message)}</p><a href="/">Quay lại</a>`);
  }
});
const cleanup = setInterval(() => { const now = Date.now(); db.prepare('DELETE FROM transactions WHERE expires_at <= ?').run(now); db.prepare('DELETE FROM transactions WHERE session_hash IN (SELECT id_hash FROM sessions WHERE expires_at <= ?)').run(now); db.prepare('DELETE FROM sessions WHERE expires_at <= ?').run(now); }, 60000);
cleanup.unref();
server.listen(Number(process.env.PORT || 5310), '127.0.0.1', () => console.log(`Partner example listening on ${origin.origin}; HTTPS termination required in production`));
process.on('SIGTERM', () => server.close(() => { clearInterval(cleanup); db.close(); }));
export { server, db };
