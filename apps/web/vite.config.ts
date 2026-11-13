import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import { VitePWA } from 'vite-plugin-pwa'

export default defineConfig({
  plugins: [
    react(),
    tailwindcss(),
    VitePWA({
      registerType: 'autoUpdate',
      injectRegister: 'auto',
      includeAssets: ['icone.svg'],
      manifest: {
        name: 'Alerta em Ação — RMR',
        short_name: 'Alerta em Ação',
        description: 'Prevenção, alertas e pedido de socorro na Região Metropolitana do Recife.',
        lang: 'pt-BR',
        theme_color: '#193B44',
        background_color: '#F8F5EF',
        display: 'standalone',
        start_url: '/',
        scope: '/',
        id: '/',
        orientation: 'portrait',
        categories: ['utilities', 'weather'],
        shortcuts: [
          { name: 'Mapa e riscos', short_name: 'Mapa', url: '/mapa' },
          { name: 'Meus pedidos', short_name: 'Pedidos', url: '/pedidos' },
        ],
        icons: [
          { src: '/icone-192.png', sizes: '192x192', type: 'image/png' },
          { src: '/icone-512.png', sizes: '512x512', type: 'image/png' },
          { src: '/icone-maskable-192.png', sizes: '192x192', type: 'image/png', purpose: 'maskable' },
          { src: '/icone-maskable-512.png', sizes: '512x512', type: 'image/png', purpose: 'maskable' },
        ],
      },
      workbox: {
        navigateFallback: '/index.html',
        importScripts: ['/sw-push.js'],
        navigateFallbackDenylist: [/^\/api\//, /^\/simulador\//, /^\/hubs\//, /^\/openapi\//, /^\/health/],
        globPatterns: ['**/*.{js,css,html,svg,png,woff2}'],
        maximumFileSizeToCacheInBytes: 4 * 1024 * 1024,
        runtimeCaching: [
          {
            urlPattern: ({ url }) => ['/api/municipios', '/api/municipios/limites', '/api/abrigos', '/api/contatos-emergencia', '/api/apoio/necessidades', '/api/territorio/areas-risco'].includes(url.pathname),
            handler: 'NetworkFirst',
            options: { cacheName: 'dados-referencia', networkTimeoutSeconds: 4, expiration: { maxEntries: 40, maxAgeSeconds: 7 * 24 * 3600 } },
          },
          {
            urlPattern: ({ url }) => url.hostname === 'tile.openstreetmap.org',
            handler: 'CacheFirst',
            options: { cacheName: 'mapa-base', expiration: { maxEntries: 300, maxAgeSeconds: 7 * 24 * 3600 }, cacheableResponse: { statuses: [0, 200] } },
          },
        ],
      },
    }),
  ],
  worker: { format: 'es' },
  server: {
    proxy: {
      '/api': 'http://localhost:8080',
      '/simulador': 'http://localhost:8080',
      '/hubs': { target: 'http://localhost:8080', ws: true },
      '/health': 'http://localhost:8080',
    },
  },
  build: { outDir: '../../backend/src/AlertaEmAcao.Api/wwwroot', emptyOutDir: true },
  test: { environment: 'jsdom', setupFiles: ['./src/testes/setup.ts'], include: ['src/**/*.test.{ts,tsx}'] },
})
