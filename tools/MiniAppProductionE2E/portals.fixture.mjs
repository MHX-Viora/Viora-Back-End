// Serve the actual Expo export; all API/host requests go to the isolated issuer.
import https from 'node:https';
import { readFile, realpath } from 'node:fs/promises';
import { resolve, join, sep, extname } from 'node:path';
export async function startProductionPortal(directory, config) {
  if (!config.productionFixture || config.productionAppOrigin !== 'https://portal.ankt.localhost:5594' ||
      config.productionHostOrigin !== 'https://production.ankt.localhost:5593' || config.issuer !== 'https://identity.ankt.example.com/')
    throw Error('Unexpected disposable Production fixture');
  const root = await realpath(resolve(import.meta.dirname, '../../../viora/.codex-tmp/stage8-web-export'));
  const deployment = JSON.parse(await readFile(join(root, 'vercel.json'), 'utf8'));
  const rule = deployment.headers.find(rule => rule.source.startsWith('/mini-apps/'));
  if (!rule?.headers.some(header => header.key === 'Content-Security-Policy' && header.value.includes('frame-src ' + config.productionHostOrigin + ';')))
    throw Error('Build the Stage 8 Web export with the isolated Production origins first');
  const pattern = new RegExp('^' + rule.source + '$');
  const server = https.createServer({ cert: await readFile(join(directory, 'tls-cert.pem')), key: await readFile(join(directory, 'tls-key.pem')) }, async (request, response) => {
    response.setHeader('Cache-Control', 'no-store'); response.setHeader('X-Content-Type-Options', 'nosniff');
    if (request.headers.host !== new URL(config.productionAppOrigin).host || request.method !== 'GET') { response.writeHead(404); response.end(); return; }
    try {
      const path = new URL(request.url, config.productionAppOrigin).pathname;
      if (pattern.test(path)) for (const header of rule.headers) response.setHeader(header.key, header.value);
      const file = extname(path) ? resolve(root, '.' + decodeURIComponent(path)) : join(root, 'index.html');
      const actual = await realpath(file);
      if (!actual.startsWith(root + sep)) throw Error('Outside fixture');
      response.setHeader('Content-Type', { '.html': 'text/html; charset=utf-8', '.js': 'text/javascript', '.css': 'text/css', '.png': 'image/png', '.svg': 'image/svg+xml', '.ttf': 'font/ttf', '.woff2': 'font/woff2', '.ico': 'image/x-icon' }[extname(actual)] ?? 'application/octet-stream');
      response.end(await readFile(actual));
    } catch { if (!response.headersSent) response.writeHead(404); response.end(); }
  });
  await new Promise((resolve, reject) => { server.once('error', reject); server.listen(5594, '127.0.0.1', resolve); });
  return { close: () => new Promise(resolve => { server.close(resolve); server.closeAllConnections(); }) };
}
