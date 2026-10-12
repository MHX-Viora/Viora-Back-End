import { defineConfig } from 'vite';
export default defineConfig({ server: { host: '127.0.0.1', port: 4314, proxy: { '/auth/ankt': { target: process.env.PARTNER_BFF_ORIGIN || 'http://127.0.0.1:4310', changeOrigin: true } } } });
