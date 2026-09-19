<script setup lang="ts">
import { t } from '@/i18n'
import { onMounted, ref } from 'vue'

const waiting = ref(false)
const offlineReady = ref(false)
let apply: ((reload?: boolean) => Promise<void>) | null = null

onMounted(async () => {
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
  }
})

async function update(): Promise<void> {
  waiting.value = false
  await apply?.(true)
}
</script>
<template>
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
