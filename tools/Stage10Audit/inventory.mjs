// Read-only, metadata-only inventory. Never emits configuration values or snippets.
import { execFileSync } from 'node:child_process';
import { readFileSync, existsSync, mkdirSync, writeFileSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { resolve, join } from 'node:path';
const workspace = resolve(import.meta.dirname, '../../..');
const repositories = [];
for (const name of ['viora-BE', 'viora', 'viora-admin']) {
  const cwd = join(workspace, name);
  const git = args => execFileSync('git', args, { cwd, windowsHide: true, encoding: 'utf8', maxBuffer: 8 * 1024 * 1024 }).trim();
  const exclusions = ['.', ':(exclude).codex-tmp', ':(exclude)**/bin/**', ':(exclude)**/obj/**',
    ':(exclude)node_modules', ':(exclude)**/node_modules/**', ':(exclude).git', ':(exclude).expo'];
  const tracked = git(['ls-files', '-z', '--', ...exclusions]).split('\0').filter(Boolean);
  const files = git(['ls-files', '--cached', '--others', '--exclude-standard', '-z', '--', ...exclusions]).split('\0')
    .filter(file => !/(^|\/)(\.codex-tmp|node_modules|bin|obj|dist|docs|\.expo|\.git)(\/|$)/.test(file))
    .filter(file => /\.(cs|csproj|sln|ts|tsx|js|mjs|cjs|json|yml|yaml|kt|swift)$/.test(file) || /(^|\/)(Dockerfile|\.dockerignore|\.gitignore)$/.test(file))
    .filter(file => !/package-lock\.json$/.test(file));
  const digest = createHash('sha256'); let count = 0;
  for (const file of [...new Set(files)].sort()) if (existsSync(join(cwd, file))) {
    digest.update(file + '\0'); digest.update(readFileSync(join(cwd, file))); count++;
  }
  const signals = [];
  const configurations = new Set(tracked.filter(file => /(^|\/)appsettings(?:\.[^/]+)?\.json$/.test(file)));
  for (const file of ['viora-BE/appsettings.json', 'viora-BE/appsettings.Development.json'])
    if (existsSync(join(cwd, file))) configurations.add(file);
  for (const file of configurations) {
    const visit = (value, key = '') => {
      if (value && typeof value === 'object') for (const [child, data] of Object.entries(value)) visit(data, key ? key + '.' + child : child);
      else if (typeof value === 'string' && value && /ConnectionStrings|Password|ApiKey|ApiSecret|PrivateKey|Secret|Credential/i.test(key)) {
        const placeholder = /^(?:\$\{|<|REPLACE|CHANGE|YOUR_|example|test-only)/i.test(value);
        signals.push({ file, key, tracked: tracked.includes(file), populated: true, placeholder,
          literalCredentialCandidate: !placeholder && (/Password|ApiKey|ApiSecret|PrivateKey|Secret|Credential/i.test(key) || /(?:Password|Pwd)\s*=\s*[^;\s]+/i.test(value)) });
      }
    };
    try { visit(JSON.parse(readFileSync(join(cwd, file), 'utf8').replace(/^\uFEFF/, ''))); }
    catch { signals.push({ file, parseFailed: true }); }
  }
  const dependencyLocks = ['package-lock.json', 'packages/miniapp-sdk/package-lock.json'].filter(file => existsSync(join(cwd, file)))
    .map(file => ({ file, sha256: createHash('sha256').update(readFileSync(join(cwd, file))).digest('hex') }));
  repositories.push({ name, revision: git(['rev-parse', 'HEAD']), dirtyEntries: git(['status', '--short']).split('\n').filter(Boolean).length, dependencyLocks,
    sourceFiles: count, sourceSha256: digest.digest('hex'), configurationSignals: signals,
    localEnvPresent: existsSync(join(cwd, '.env')) });
}
const output = resolve(import.meta.dirname, '../../.codex-tmp/stage10/inventory.json');
mkdirSync(resolve(output, '..'), { recursive: true });
writeFileSync(output, JSON.stringify({ capturedUtc: new Date().toISOString(), node: process.version, repositories,
  limits: 'Current dirty source snapshot, not a clean revision artifact. Config candidates need operator validation/rotation; no secret values, history scan, live credential validation or production access.' }, null, 2));
console.log(JSON.stringify({ repositories: repositories.map(({ name, sourceFiles, configurationSignals }) => ({ name, sourceFiles,
  credentialCandidates: configurationSignals.filter(signal => signal.literalCredentialCandidate).map(({ file, key, tracked }) => ({ file, key, tracked })) })) }));
