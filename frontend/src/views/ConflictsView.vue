<script setup lang="ts">
import { t } from '@/i18n'
import { onMounted, ref, useTemplateRef } from 'vue'
import AppShell from '@/components/layout/AppShell.vue'
import PullToRefresh from '@/components/ui/PullToRefresh.vue'
import { db, type LocalConflict, type OutboxOperation } from '@/offline/db'
import { useApi } from '@/api/provider'
import { useExpensesStore } from '@/stores/expenses'
import { report } from '@/ui/toasts'
import { checkForAppUpdate } from '@/native/appUpdate'

const expenses = useExpensesStore()

const conflicts = ref<LocalConflict[]>([])
const rejected = ref<OutboxOperation[]>([])
const waiting = ref<OutboxOperation[]>([])
const isResetting = ref(false)
const isSyncing = ref(false)
const confirmingReset = ref(false)

onMounted(load)

async function load(): Promise<void> {
  conflicts.value = await db.conflicts.toArray()
  rejected.value = await db.outbox.where('status').equals('rejected').toArray()
  waiting.value = await db.outbox.filter((row) => row.status !== 'rejected').toArray()
}

const pull = useTemplateRef<{ done: () => void }>('pull')

async function refresh(): Promise<void> {
  await checkForAppUpdate()
  await syncNow()
  pull.value?.done()
}

async function syncNow(): Promise<void> {
  isSyncing.value = true

  try {
    await expenses.sync()
  } catch (caught) {
    report(caught, t('Could not send those changes.'))
  } finally {
    isSyncing.value = false
    await load()
  }
}

async function resetToServer(): Promise<void> {
  isResetting.value = true

  try {
    await expenses.resetToServer()
    confirmingReset.value = false
    await load()
  } catch (caught) {
    report(caught, t('Could not reload from the server.'))
  } finally {
    isResetting.value = false
  }
}

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
    report(caught, t('Could not resolve that conflict.'))
  }
}

async function discard(operationId: string): Promise<void> {
  await expenses.discardRejected(operationId)
  await load()
}
</script>
<template>
  <AppShell
    width="wide"
    :title="t('Needs attention')"
    :pending-count="expenses.pendingCount"
    :rejected-count="expenses.rejectedCount"
    :is-syncing="expenses.isSyncing"
    :back-to="{ name: 'profile' }"
    :back-label="t('Profile')"
  >
    <PullToRefresh ref="pull" @refresh="refresh" />
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
      <div class="mb-2 flex items-center justify-between gap-2">
        <h2 class="min-w-0 text-sm font-medium text-[var(--text-muted)]">
          {{ t('Changes waiting to be sent') }}
        </h2>
        <button
          type="button"
          data-testid="sync-now"
          class="btn btn-press btn-secondary min-h-0 shrink-0 px-3 py-1.5 text-xs"
          style="border-color: var(--border)"
          :disabled="isSyncing"
          @click="syncNow"
        >
          {{ isSyncing ? t('Sending') : t('Send now') }}
        </button>
      </div>
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
  </AppShell>
</template>
