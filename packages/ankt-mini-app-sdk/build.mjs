import { copyFile, mkdir } from 'node:fs/promises';
const output = process.argv.includes('--declarations') ? 'dist' : 'src';
await mkdir(output, { recursive: true });
const extensions = output === 'dist' ? ['d.ts'] : ['js', 'd.ts'];
for (const extension of extensions) await copyFile(new URL(`../../viora-BE/wwwroot/mini-app-sdk/ankt-mini-app.${extension}`, import.meta.url), `${output}/ankt-mini-app.${extension}`);
