import { fileURLToPath, URL } from 'node:url'
import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import tailwindcss from '@tailwindcss/vite'
import { VitePWA } from 'vite-plugin-pwa'

export default defineConfig({
  plugins: [
    vue(),
    tailwindcss(),
    VitePWA({
      registerType: 'prompt',
      // The service worker is written by hand: it has to handle Web Push and the
      // offline shell, neither of which the generated worker covers.
      strategies: 'injectManifest',
      srcDir: 'src',
      filename: 'service-worker.ts',
      injectManifest: {
        globPatterns: ['**/*.{js,css,html,svg,png,woff2}'],
      },
      manifest: {
        name: 'Split Everything',
        short_name: 'Split',
        description: 'Shared expenses, settled properly.',
        theme_color: '#0f172a',
        background_color: '#0f172a',
        display: 'standalone',
        orientation: 'portrait',
        start_url: '/',
        icons: [
          { src: '/icons/icon-192.png', sizes: '192x192', type: 'image/png' },
          { src: '/icons/icon-512.png', sizes: '512x512', type: 'image/png' },
          { src: '/icons/icon-maskable.png', sizes: '512x512', type: 'image/png', purpose: 'maskable' },
        ],
      },
      devOptions: { enabled: false },
    }),
  ],
  resolve: {
    alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) },
  },
  server: {
    port: 5173,
    // The dev server binds every interface so a phone on the same network can
    // reach it. That lives on the dev script as --host, not here: Vite 8.2.2
    // ignores server.host from the config file, and a setting that looks applied
    // but is not is worse than none.
    //
    // Only this port is exposed. The API stays on localhost and is reached
    // through the proxy below, from this machine.
    proxy: {
      // xfwd passes the caller's address on, so the API's rate limits count a
      // phone on the network as itself rather than counting every device that
      // comes through this proxy as one caller.
      '/api': { target: 'http://localhost:5080', changeOrigin: true, xfwd: true },
      '/hubs': { target: 'http://localhost:5080', ws: true, changeOrigin: true, xfwd: true },
    },
  },
  build: { sourcemap: true },
})
