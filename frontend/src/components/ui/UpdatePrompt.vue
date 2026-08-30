<script setup lang="ts">
import { t } from '@/i18n'
import { onMounted, ref } from 'vue'

/**
 * A new version is waiting.
 *
 * A service worker that has downloaded a new build does not take over while a page
 * is still using the old one, and on a phone a page is never closed: the app sat on
 * whatever version it was installed with until the tab was killed by hand. That is
 * how a client ends up older than the server it is talking to, which the API client
 * already has a message for.
 *
 * So it is asked rather than forced. An app in the middle of typing an expense
 * should not reload itself from under someone, and the offline outbox means there
 * is nothing to lose by waiting either.
 */

const waiting = ref(false)
const offlineReady = ref(false)
let apply: ((reload?: boolean) => Promise<void>) | null = null

onMounted(async () => {
  // Imported here rather than at the top: the virtual module only exists once the
  // PWA plugin has built, and a test mounting this component has no such module.
  try {
    const { registerSW } = await import('virtual:pwa-register')

    apply = registerSW({
      immediate: true,
      onNeedRefresh: () => {
        waiting.value = true
      },
      onOfflineReady: () => {
        offlineReady.value = true
        window.setTimeout(() => {
          offlineReady.value = false
        }, 4000)
      },
    })
  } catch {
    // No service worker in this build, or none allowed by the browser. The app
    // works; it simply cannot tell anybody about a new version.
  }
})

async function update(): Promise<void> {
  waiting.value = false
  // Reloads every tab on this device, which is the point: two tabs on two versions
  // share one local replica.
  await apply?.(true)
}
</script>

<template>
  <!--
    Above the tab bar, out of the way of the thumb, and never over the page's own
    controls: this is not urgent, it is just true.
  -->
  <Teleport to="body">
    <div
      v-if="waiting"
      data-testid="update-prompt"
      class="pointer-events-auto fixed inset-x-0 z-40 mx-auto flex max-w-md items-center gap-3 rounded-xl border p-3 text-sm shadow-lg"
      style="
        background: var(--surface-raised);
        border-color: var(--border);
        bottom: calc(5rem + env(safe-area-inset-bottom));
        left: 1rem;
        right: 1rem;
      "
      role="status"
    >
      <span class="min-w-0 flex-1">{{ t('A new version is ready.') }}</span>

      <button
        type="button"
        data-testid="dismiss-update"
        class="btn btn-press btn-quiet min-h-0 shrink-0 px-2 py-1 text-xs"
        @click="waiting = false"
      >{{ t('Later') }}
      </button>

      <button
        type="button"
        data-testid="apply-update"
        class="btn btn-press btn-primary min-h-0 shrink-0 px-3 py-1.5 text-xs"
        @click="update"
      >{{ t('Reload') }}
      </button>
    </div>

    <p
      v-else-if="offlineReady"
      data-testid="offline-ready"
      class="pointer-events-none fixed inset-x-0 z-40 mx-auto max-w-md rounded-xl border p-3 text-center text-sm"
      style="
        background: var(--surface-raised);
        border-color: var(--border);
        bottom: calc(5rem + env(safe-area-inset-bottom));
        left: 1rem;
        right: 1rem;
      "
      role="status"
    >{{ t('Ready to work offline.') }}
    </p>
  </Teleport>
</template>
