import { fileURLToPath, URL } from 'node:url'
import { defineConfig } from 'vitest/config'
import vue from '@vitejs/plugin-vue'

export default defineConfig({
  plugins: [vue()],
  resolve: {
    alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) },
  },
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['tests/setup.ts'],
    include: ['tests/**/*.spec.ts'],
    coverage: {
      provider: 'v8',
      reporter: ['text', 'json-summary', 'cobertura'],
      reportsDirectory: 'coverage',
      include: ['src/**/*.{ts,vue}'],
      exclude: [
        // Wiring, not logic: main.ts constructs the app and can only be covered by
        // booting the whole thing, which the API integration tests already do
        // end to end on the server side.
        'src/main.ts',
        'src/App.vue',
        // The service worker and the parsing worker run in worker scopes that jsdom
        // does not provide. The logic they call is tested directly: the statement
        // parsers, the review session and the categoriser all have their own suites.
        'src/service-worker.ts',
        'src/workers/**',
        // Presentational screens with no branching of their own. The two that do
        // carry logic - the add-expense form and the group card - are tested.
        'src/views/ActivityView.vue',
        'src/views/StatsView.vue',
        'src/views/ProfileView.vue',
        'src/views/ExpenseView.vue',
        'src/views/SettleView.vue',
        'src/views/GroupSettingsView.vue',
        'src/views/ImportView.vue',
        'src/views/ConflictsView.vue',
        'src/views/JoinView.vue',
        'src/views/SignInView.vue',
        'src/views/NewGroupView.vue',
        'src/views/NotFoundView.vue',
        'src/views/GroupsView.vue',
        'src/views/GroupView.vue',
        'src/components/layout/**',
        'src/**/*.d.ts',
        // Registration paths that only exist inside a Capacitor shell or a real
        // service worker; the pure part, VAPID key decoding, is tested.
        'src/native/push.ts',
      ],
      thresholds: {
        // Everything that decides anything - money, splits, clocks, balances, the
        // offline engine, the importers, the stores and the HTTP client - is held
        // to a high bar. These are the numbers that matter for correctness.
        lines: 85,
        functions: 85,
        branches: 80,
        statements: 85,
        'src/domain/**': { lines: 95, functions: 95, branches: 85, statements: 95 },
        'src/offline/**': { lines: 85, functions: 80, branches: 70, statements: 85 },
        'src/import/**': { lines: 85, functions: 85, branches: 75, statements: 85 },
        'src/stores/**': { lines: 88, functions: 85, branches: 78, statements: 88 },
        'src/api/**': { lines: 85, functions: 60, branches: 75, statements: 85 },
      },
    },
  },
})
