import https from 'node:https';
import { readFile } from 'node:fs/promises';
import { resolve, join, sep, extname } from 'node:path';
import { createRequire } from 'node:module';
import { pathToFileURL } from 'node:url';
export async function startAdmin(directory) {
  const admin = resolve(import.meta.dirname, '../../../viora-admin');
  const require = createRequire(join(admin, 'package.json'));
  const { createBuilder } = await import(pathToFileURL(require.resolve('vite')).href);
  const { default: react } = await import(pathToFileURL(require.resolve('@vitejs/plugin-react')).href);
  const root = join(admin, '.codex-tmp/stage7-review-admin-export');
  // No application .env or configured remote API may enter this fixture build.
  const builder = await createBuilder({ root: admin, configFile: false, envDir: false, plugins: [react()], define: { 'import.meta.env.VITE_API_BASE_URL': '""' }, build: { outDir: root }, logLevel: 'error' });
  await builder.buildApp();
  const ca = await readFile(join(directory, 'root-ca.pem'));
  const server = https.createServer({ cert: await readFile(join(directory, 'tls-cert.pem')), key: await readFile(join(directory, 'tls-key.pem')) }, async (req, res) => {
    res.setHeader('Cache-Control', 'no-store'); res.setHeader('Referrer-Policy', 'no-referrer');
    try {
      if (req.headers.host !== 'localhost:5495') { res.writeHead(404); res.end(); return; }
      const url = new URL(req.url, 'https://localhost:5495');
      if (url.pathname.startsWith('/api/')) {
        if (!/^\/api\/(accounts\/(login|refresh-token|logout)|admin\/(dashboard|mini-apps(?:\/.*)?|mini-app-reviews(?:\/.*)?))$/.test(url.pathname)) { res.writeHead(404); res.end(); return; }
        const upstream = https.request(new URL(url.pathname + url.search, 'https://127.0.0.1:5490'), { method: req.method, ca, headers: { ...req.headers, host: 'identity.ankt.example.com' } }, response => { if (response.statusCode >= 400) console.log('REVIEW_ADMIN_FIXTURE_HTTP_' + req.method + '_' + url.pathname + '_' + response.statusCode); res.writeHead(response.statusCode, response.headers); response.pipe(res); });
        upstream.on('error', () => { if (!res.headersSent) res.writeHead(502); res.end(); });
        upstream.setTimeout(45000, () => upstream.destroy()); req.pipe(upstream); return;
      }
      let file = resolve(root, '.' + decodeURIComponent(url.pathname));
      if (!file.startsWith(root + sep)) { res.writeHead(404); res.end(); return; }
      if (!extname(file)) file = join(root, 'index.html');
      res.setHeader('Content-Type', { '.html': 'text/html; charset=utf-8', '.js': 'text/javascript', '.css': 'text/css', '.png': 'image/png', '.svg': 'image/svg+xml' }[extname(file)] ?? 'application/octet-stream');
      res.end(await readFile(file));
    } catch { if (!res.headersSent) res.writeHead(404); res.end(); }
  });
  await new Promise((resolve, reject) => { server.once('error', reject); server.listen(5495, '127.0.0.1', resolve); });
  return { close: () => new Promise(resolve => server.close(resolve)) };
}
