<script setup lang="ts">
import { onMounted, ref } from 'vue'
import AppShell from '@/components/layout/AppShell.vue'
import { db, type LocalConflict, type OutboxOperation } from '@/offline/db'
import { useApi } from '@/api/provider'
import { useExpensesStore } from '@/stores/expenses'

const expenses = useExpensesStore()

const conflicts = ref<LocalConflict[]>([])
const rejected = ref<OutboxOperation[]>([])
const error = ref<string | null>(null)


onMounted(load)

async function load(): Promise<void> {
  conflicts.value = await db.conflicts.toArray()
  rejected.value = await db.outbox.where('status').equals('rejected').toArray()
}

/** Reads a payload field for display without trusting its shape. */
function field(json: string, name: string): string {
  try {
    const parsed = JSON.parse(json) as Record<string, unknown>
    const value = parsed[name]
    return value === undefined || value === null ? '(empty)' : String(value)
  } catch {
    return '(unreadable)'
  }
}

async function resolve(conflict: LocalConflict, resolution: 'KeepLocal' | 'KeepRemote'): Promise<void> {
  error.value = null

  try {
    await useApi().post('/sync/conflicts/resolve', {
      conflictId: conflict.conflictId,
      resolution,
      mergedPayloadJson: null,
    })

    await db.conflicts.delete(conflict.conflictId)
    await expenses.sync()
    await load()
  } catch (caught) {
    error.value = caught instanceof Error ? caught.message : 'Could not resolve that conflict.'
  }
}

async function discard(operationId: string): Promise<void> {
  // Through the store, so the local row stops claiming to be unsent. Deleting only
  // the queue entry would leave a row nothing can ever sync.
  await expenses.discardRejected(operationId)
  await load()
}
</script>

<template>
  <AppShell
    title="Needs attention"
    :pending-count="expenses.pendingCount"
    :rejected-count="expenses.rejectedCount"
    :is-syncing="expenses.isSyncing"
  >
    <section v-if="conflicts.length > 0" class="mb-6">
      <h2 class="mb-2 text-sm font-medium text-[var(--text-muted)]">
        Edited on two devices at once
      </h2>
      <p class="mb-3 text-xs text-[var(--text-muted)]">
        Both versions were kept. Pick the one to keep - nothing was overwritten.
      </p>

      <ul class="flex flex-col gap-3">
        <li v-for="conflict in conflicts" :key="conflict.conflictId" class="surface-card p-4">
          <p class="text-xs text-[var(--text-muted)]">
            {{ conflict.entityType }} - {{ conflict.conflictingFields.join(', ') || 'whole record' }}
          </p>

          <dl class="mt-2 grid grid-cols-2 gap-3 text-sm">
            <div>
              <dt class="text-xs text-[var(--text-muted)]">On the server</dt>
              <dd>{{ field(conflict.storedPayloadJson, 'description') }}</dd>
            </div>
            <div>
              <dt class="text-xs text-[var(--text-muted)]">Your version</dt>
              <dd>{{ field(conflict.incomingPayloadJson, 'description') }}</dd>
            </div>
          </dl>

          <div class="mt-3 flex gap-2">
            <button
              type="button"
              class="tap-target flex-1 rounded-lg border text-sm"
              style="border-color: var(--border)"
              @click="resolve(conflict, 'KeepLocal')"
            >
              Keep the server version
            </button>
            <button
              type="button"
              class="tap-target flex-1 rounded-lg bg-brand-600 text-sm font-medium text-white"
              @click="resolve(conflict, 'KeepRemote')"
            >
              Keep mine
            </button>
          </div>
        </li>
      </ul>
    </section>

    <section v-if="rejected.length > 0">
      <h2 class="mb-2 text-sm font-medium text-[var(--text-muted)]">Changes the server refused</h2>

      <ul class="flex flex-col gap-3">
        <li v-for="operation in rejected" :key="operation.operationId" class="surface-card p-4">
          <p class="text-sm">{{ operation.operation }} {{ operation.entityType }}</p>
          <p class="mt-1 text-xs text-owing">{{ operation.lastError }}</p>
          <button
            type="button"
            class="tap-target mt-3 rounded-lg border px-3 text-sm"
            style="border-color: var(--border)"
            @click="discard(operation.operationId)"
          >
            Discard this change
          </button>
        </li>
      </ul>
    </section>

    <p
      v-if="conflicts.length === 0 && rejected.length === 0"
      class="surface-card p-6 text-center text-sm text-[var(--text-muted)]"
    >
      Nothing needs your attention.
    </p>

    <p v-if="error" class="mt-4 text-sm text-owing" role="alert">{{ error }}</p>
  </AppShell>
</template>
