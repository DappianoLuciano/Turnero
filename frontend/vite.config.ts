import path from 'node:path';
import tailwindcss from '@tailwindcss/vite';
import react from '@vitejs/plugin-react';
import { loadEnv } from 'vite';
import { VitePWA } from 'vite-plugin-pwa';
import { defineConfig } from 'vitest/config';
import product from '../product.json' with { type: 'json' };

export default defineConfig(({ mode }) => {
  // Single .env at the repo root (shared with docker compose and the API).
  const env = loadEnv(mode, path.resolve(__dirname, '..'), '');
  const apiTarget = env.API_PROXY_TARGET || 'http://localhost:5080';

  return {
    envDir: '..',
    plugins: [
      {
        name: 'product-name-html',
        transformIndexHtml: (html: string) => html.replaceAll('%PRODUCT_NAME%', product.name),
      },
      react(),
      tailwindcss(),
      VitePWA({
        registerType: 'autoUpdate',
        includeAssets: ['icons/favicon.svg'],
        manifest: {
          name: product.name,
          short_name: product.shortName,
          description: product.description,
          lang: 'es-AR',
          start_url: '/app',
          scope: '/',
          display: 'standalone',
          orientation: 'portrait',
          theme_color: product.themeColor,
          background_color: product.backgroundColor,
          icons: [
            { src: '/icons/icon-192.png', sizes: '192x192', type: 'image/png' },
            { src: '/icons/icon-512.png', sizes: '512x512', type: 'image/png' },
            { src: '/icons/icon-maskable-512.png', sizes: '512x512', type: 'image/png', purpose: 'maskable' },
          ],
        },
        workbox: {
          // App shell only: API responses are never cached by the service worker.
          navigateFallback: '/index.html',
          navigateFallbackDenylist: [/^\/api\//, /^\/health/, /^\/docs/, /^\/openapi/],
          globPatterns: ['**/*.{js,css,html,svg,png,woff2}'],
        },
        devOptions: { enabled: false },
      }),
    ],
    resolve: {
      alias: {
        '@': path.resolve(__dirname, 'src'),
        '@product': path.resolve(__dirname, '../product.json'),
      },
    },
    server: {
      port: 5173,
      fs: { allow: ['..'] },
      proxy: {
        '/api': { target: apiTarget, changeOrigin: false },
        '/health': { target: apiTarget, changeOrigin: false },
      },
    },
    test: {
      environment: 'jsdom',
      globals: false,
      setupFiles: ['./src/test/setup.ts'],
      css: false,
      restoreMocks: true,
    },
  };
});
