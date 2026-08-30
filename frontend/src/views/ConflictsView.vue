<script setup lang="ts">
import { t } from '@/i18n'
import { onMounted, ref } from 'vue'
import AppShell from '@/components/layout/AppShell.vue'
import { db, type LocalConflict, type OutboxOperation } from '@/offline/db'
import { useApi } from '@/api/provider'
import { useExpensesStore } from '@/stores/expenses'

const expenses = useExpensesStore()

const conflicts = ref<LocalConflict[]>([])
const rejected = ref<OutboxOperation[]>([])
/** Queued and not sent yet. What the "waiting to sync" count is actually counting. */
const waiting = ref<OutboxOperation[]>([])
const error = ref<string | null>(null)
const isResetting = ref(false)
const confirmingReset = ref(false)


onMounted(load)

async function load(): Promise<void> {
  conflicts.value = await db.conflicts.toArray()
  rejected.value = await db.outbox.where('status').equals('rejected').toArray()
  // Anything not refused is still on its way, or trying to be. The count in the
  // header said three and this screen showed nothing, which is not an answer.
  waiting.value = await db.outbox.filter((row) => row.status !== 'rejected').toArray()
}

/**
 * Takes the server's version of everything.
 *
 * For a replica that has diverged past arguing with: every screen reads from it,
 * so when it is wrong there is nothing else to look at.
 */
async function resetToServer(): Promise<void> {
  error.value = null
  isResetting.value = true

  try {
    await expenses.resetToServer()
    confirmingReset.value = false
    await load()
  } catch (caught) {
    error.value = caught instanceof Error ? caught.message : t('Could not reload from the server.')
  } finally {
    isResetting.value = false
  }
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
    error.value = caught instanceof Error ? caught.message : t('Could not resolve that conflict.')
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
    :title="t('Needs attention')"
    :pending-count="expenses.pendingCount"
    :rejected-count="expenses.rejectedCount"
    :is-syncing="expenses.isSyncing"
    :back-to="{ name: 'profile' }"
    :back-label="t('Profile')"
  >
    <section v-if="conflicts.length > 0" class="mb-6">
      <h2 class="mb-2 text-sm font-medium text-[var(--text-muted)]">{{ t('Edited on two devices at once') }}
      </h2>
      <p class="mb-3 text-xs text-[var(--text-muted)]">{{ t('Both versions were kept. Pick the one to keep - nothing was overwritten.') }}
      </p>

      <ul class="flex flex-col gap-3">
        <li v-for="conflict in conflicts" :key="conflict.conflictId" class="surface-card p-4">
          <p class="text-xs text-[var(--text-muted)]">
            {{ conflict.entityType }} - {{ conflict.conflictingFields.join(', ') || 'whole record' }}
          </p>

          <dl class="mt-2 grid grid-cols-2 gap-3 text-sm">
            <div>
              <dt class="text-xs text-[var(--text-muted)]">{{ t('On the server') }}</dt>
              <dd>{{ field(conflict.storedPayloadJson, 'description') }}</dd>
            </div>
            <div>
              <dt class="text-xs text-[var(--text-muted)]">{{ t('Your version') }}</dt>
              <dd>{{ field(conflict.incomingPayloadJson, 'description') }}</dd>
            </div>
          </dl>

          <div class="mt-3 flex gap-2">
            <button
              type="button"
              class="btn btn-press btn-secondary flex-1"
              style="border-color: var(--border)"
              @click="resolve(conflict, 'KeepLocal')"
            >{{ t('Keep the server version') }}
            </button>
            <button
              type="button"
              class="btn btn-press btn-primary flex-1"
              @click="resolve(conflict, 'KeepRemote')"
            >{{ t('Keep mine') }}
            </button>
          </div>
        </li>
      </ul>
    </section>

    <section v-if="rejected.length > 0">
      <h2 class="mb-2 text-sm font-medium text-[var(--text-muted)]">{{ t('Changes the server refused') }}</h2>

      <ul class="flex flex-col gap-3">
        <li v-for="operation in rejected" :key="operation.operationId" class="surface-card p-4">
          <p class="text-sm">{{ operation.operation }} {{ operation.entityType }}</p>
          <p class="mt-1 text-xs text-owing">{{ operation.lastError }}</p>
          <button
            type="button"
            class="btn btn-press btn-secondary mt-3"
            style="border-color: var(--border)"
            @click="discard(operation.operationId)"
          >{{ t('Discard this change') }}
          </button>
        </li>
      </ul>
    </section>

    <section v-if="waiting.length > 0" class="mt-4">
      <h2 class="mb-2 text-sm font-medium text-[var(--text-muted)]">{{ t('Changes waiting to be sent') }}
      </h2>

      <ul class="flex flex-col gap-3">
        <li
          v-for="operation in waiting"
          :key="operation.operationId"
          data-testid="waiting-operation"
          class="surface-card p-4"
        >
          <p class="text-sm">{{ operation.operation }} {{ operation.entityType }}</p>
          <p class="mt-1 text-xs text-[var(--text-muted)]">
            Queued, attempt {{ operation.attempts + 1 }}.
          </p>
          <!--
            The reason it has not gone, when there is one. Without it a count of
            three sits in the header with nothing behind it.
          -->
          <p v-if="operation.lastError" class="mt-1 text-xs text-owing">
            {{ operation.lastError }}
          </p>
        </li>
      </ul>
    </section>

    <p
      v-if="conflicts.length === 0 && rejected.length === 0 && waiting.length === 0"
      class="surface-card p-6 text-center text-sm text-[var(--text-muted)]"
    >{{ t('Nothing needs your attention.') }}
    </p>

    <!--
      Last resort, and named as one. Every screen reads from the local replica, so
      a replica that has gone wrong cannot be worked around from anywhere else.
    -->
    <section class="mt-6">
      <h2 class="mb-2 text-sm font-medium text-[var(--text-muted)]">{{ t('This device') }}</h2>

      <div v-if="!confirmingReset" class="surface-card p-4">
        <p class="text-sm text-[var(--text-muted)]">{{ t('If this device is showing something the others are not, it can throw away what it has stored and ask the server for all of it again.') }}
        </p>
        <button
          type="button"
          data-testid="reset-replica"
          class="btn btn-press btn-secondary mt-3"
          style="border-color: var(--border)"
          @click="confirmingReset = true"
        >{{ t('Reload everything from the server') }}
        </button>
      </div>

      <div v-else class="surface-card flex flex-col gap-3 p-4">
        <p class="text-sm">{{ t("Everything stored on this device is replaced by the server's version.") }}
        </p>
        <p v-if="waiting.length > 0 || rejected.length > 0" class="text-sm text-owing">
          {{ waiting.length + rejected.length }} change(s) that have not reached the
          server will be lost. Nothing else can bring them back.
        </p>
        <div class="flex gap-2">
          <button
            type="button"
            class="btn btn-press btn-secondary flex-1"
            style="border-color: var(--border)"
            @click="confirmingReset = false"
          >{{ t('Cancel') }}
          </button>
          <button
            type="button"
            data-testid="reset-replica-confirm"
            class="btn btn-press btn-danger flex-1"
            :disabled="isResetting"
            @click="resetToServer"
          >
            {{ isResetting ? t('Reloading') : t('Reload from the server') }}
          </button>
        </div>
      </div>
    </section>

    <p v-if="error" class="mt-4 text-sm text-owing" role="alert">{{ error }}</p>
  </AppShell>
</template>
