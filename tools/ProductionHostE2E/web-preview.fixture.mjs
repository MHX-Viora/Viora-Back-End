// Static UI preview only. No approval, launch, bridge or SSO business acceptance.
// Export was built explicitly with loopback fixture origins and EXPO_NO_DOTENV=1.
import http from 'node:http';
import { readFile } from 'node:fs/promises';
import { resolve, sep, extname } from 'node:path';
const root = resolve(import.meta.dirname, '../../../viora/.codex-tmp/stage8-web-export');
const configuration = JSON.parse(await readFile(resolve(root, 'vercel.json'), 'utf8'));
const rule = configuration.headers.find(x => x.source.startsWith('/mini-apps/'));
const server = http.createServer(async (request, response) => {
  response.setHeader('Cache-Control', 'no-store'); response.setHeader('X-Content-Type-Options', 'nosniff');
  try {
    if (request.headers.host !== 'localhost:5594') { response.writeHead(404); response.end(); return; }
    const path = new URL(request.url, 'http://localhost:5594').pathname;
    if (rule && new RegExp('^' + rule.source + '$').test(path)) {
      for (const header of rule.headers) response.setHeader(header.key, header.value);
    }
    const file = extname(path) ? resolve(root, '.' + decodeURIComponent(path)) : resolve(root, 'index.html');
    if (!file.startsWith(root + sep)) { response.writeHead(404); response.end(); return; }
    response.setHeader('Content-Type', { '.html': 'text/html; charset=utf-8', '.js': 'text/javascript', '.css': 'text/css', '.png': 'image/png', '.svg': 'image/svg+xml', '.ttf': 'font/ttf', '.woff2': 'font/woff2' }[extname(file)] ?? 'application/octet-stream');
    response.end(await readFile(file));
  } catch { if (!response.headersSent) response.writeHead(404); response.end(); }
});
const timer = setTimeout(() => server.close(), 600000);
server.on('close', () => clearTimeout(timer));
process.once('SIGINT', () => server.close()); process.once('SIGTERM', () => server.close());
server.listen(5594, '127.0.0.1', () => console.log('Stage 8 static UI preview: http://localhost:5594/mini-apps/app_' + 'a'.repeat(43)));
