import { fileURLToPath, URL } from 'node:url'
import { defineConfig } from 'vitest/config'
import vue from '@vitejs/plugin-vue'

export default defineConfig({
  plugins: [vue()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
      'virtual:pwa-register': fileURLToPath(
        new URL('./tests/support/pwaRegister.ts', import.meta.url),
      ),
    },
  },
  test: {
    environment: 'jsdom',
    env: { VITE_API_BASE_URL: '/api', TZ: 'America/Toronto' },
    globals: true,
    setupFiles: ['tests/setup.ts'],
    include: ['tests/**/*.spec.ts'],
    coverage: {
      provider: 'v8',
      reporter: ['text', 'json-summary', 'cobertura'],
      reportsDirectory: 'coverage',
      include: ['src/**/*.{ts,vue}'],
      exclude: [
        'src/main.ts',
        'src/App.vue',
        'src/service-worker.ts',
        'src/workers/**',
        'src/native/push.ts',
        'src/**/*.d.ts',
      ],
      thresholds: {
        lines: 85,
        functions: 85,
        branches: 80,
        statements: 85,
        'src/domain/**': { lines: 95, functions: 95, branches: 85, statements: 95 },
        'src/offline/**': { lines: 85, functions: 80, branches: 70, statements: 85 },
        'src/import/**': { lines: 85, functions: 85, branches: 75, statements: 85 },
        'src/stores/**': { lines: 88, functions: 85, branches: 78, statements: 88 },
        'src/api/**': { lines: 85, functions: 60, branches: 75, statements: 85 },
        'src/views/**': { lines: 90, functions: 85, branches: 80, statements: 90 },
        'src/components/**': { lines: 95, functions: 95, branches: 90, statements: 95 },
      },
    },
  },
})
