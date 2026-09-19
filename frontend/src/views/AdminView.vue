<script setup lang="ts">
import { t } from '@/i18n'
import { computed, onMounted, ref } from 'vue'
import AppShell from '@/components/layout/AppShell.vue'
import { FontAwesomeIcon } from '@fortawesome/vue-fontawesome'
import { resolveIcon } from '@/domain/icons'
import CategoryEditor from '@/components/groups/CategoryEditor.vue'
import type { Category } from '@/domain/categories'
import type { CategoryDraft } from '@/stores/groups'
import { formatMoney } from '@/domain/money'
import { useApi } from '@/api/provider'
import { useAuthStore } from '@/stores/auth'
import { useGroupsStore } from '@/stores/groups'
import { notify, report } from '@/ui/toasts'

const api = useApi()
const auth = useAuthStore()
const groups = useGroupsStore()

interface AdminGroup {
  id: string
  name: string
  baseCurrency: string
  iconName: string | null
  colorHex: string
  isArchived: boolean
  archivedAt: string | null
  createdAt: string
  lastActivityAt: string | null
  createdByName: string
  memberCount: number
  expenseCount: number
  totalSpend: number
  isMine: boolean
}

const all = ref<AdminGroup[]>([])
const isLoading = ref(true)

const deleting = ref<string | null>(null)
const typedName = ref('')
const isDeleting = ref(false)

onMounted(() => {
  if (auth.user?.isAdmin) {
    void load()
    void loadCategories()
  } else {
    isLoading.value = false
  }
})

const categories = ref<Category[]>([])
const isSavingCategories = ref(false)

async function loadCategories(): Promise<void> {
  try {
    categories.value = await api.get<Category[]>('/admin/categories')
  } catch (caught) {
    report(caught, t('Could not read the categories.'))
  }
}

async function saveCategories(list: CategoryDraft[]): Promise<void> {
  isSavingCategories.value = true

  try {
    categories.value = await api.put<Category[]>('/admin/categories', { categories: list })
    notify(t('Saved.'), 'done')
  } catch (caught) {
    report(caught, t('Could not save those categories.'))
  } finally {
    isSavingCategories.value = false
  }
}

async function load(): Promise<void> {
  isLoading.value = true

  try {
    all.value = await api.get<AdminGroup[]>('/admin/groups')
  } catch (caught) {
    report(caught, t('Could not read the groups.'))
  } finally {
    isLoading.value = false
  }
}

const live = computed(() => all.value.filter((group) => !group.isArchived))
const archived = computed(() => all.value.filter((group) => group.isArchived))

const iconOf = (group: AdminGroup) => resolveIcon(group.iconName)

function startDelete(group: AdminGroup): void {
  deleting.value = group.id
  typedName.value = ''
}

function cancelDelete(): void {
  deleting.value = null
  typedName.value = ''
}

const canDelete = (group: AdminGroup) => typedName.value.trim() === group.name

async function remove(group: AdminGroup): Promise<void> {
  if (!canDelete(group)) return

  isDeleting.value = true

  try {
    await api.delete(`/admin/groups/${group.id}`)
    all.value = all.value.filter((candidate) => candidate.id !== group.id)
    notify(t('{name} is gone.', { name: group.name }), 'done')
    cancelDelete()

    await groups.loadAll()
  } catch (caught) {
    report(caught, t('Could not delete that group.'))
  } finally {
    isDeleting.value = false
  }
}

const on = (iso: string | null) =>
  iso ? new Date(iso).toLocaleDateString(undefined, { day: 'numeric', month: 'short', year: 'numeric' }) : null
</script>
<template>
  <AppShell
    :title="t('Server groups')"
    :subtitle="auth.user?.email"
    :back-to="{ name: 'profile-settings' }"
    back-label="Settings"
  >
    <p v-if="!auth.user?.isAdmin" class="surface-card p-6 text-center text-sm text-[var(--text-muted)]">
      {{ t('This is for whoever runs this server.') }}
    </p>
    <template v-else>
      <p v-if="isLoading" class="py-12 text-center text-[var(--text-muted)]">{{ t('Loading') }}</p>
      <section
        v-for="section in [
          { key: 'live', title: t('In use'), rows: live },
          { key: 'archived', title: t('Archived'), rows: archived },
        ]" :key="section.key" class="mb-5"
      >
        <h2 v-if="section.rows.length > 0" class="mb-2 text-sm font-medium text-[var(--text-muted)]">
          {{ section.title }}
        </h2>
        <ul class="flex flex-col gap-2">
          <li
            v-for="group in section.rows"
            :key="group.id"
            data-testid="admin-group"
            :data-group-id="group.id"
            class="surface-card p-3"
          >
            <div class="flex items-center gap-3">
              <span
                class="flex h-9 w-9 shrink-0 items-center justify-center rounded-lg text-white"
                :style="{ backgroundColor: group.colorHex }"
                aria-hidden="true"
              >
                <FontAwesomeIcon :icon="iconOf(group).definition" class="h-4 w-4" />
              </span>
              <span class="flex min-w-0 flex-1 flex-col">
                <span class="truncate font-medium">{{ group.name }}</span>
                <span class="truncate text-xs text-[var(--text-muted)]">
                  {{ t('{count} people', { count: group.memberCount }) }}
                  <span aria-hidden="true">-</span>
                  {{ t('{count} expenses', { count: group.expenseCount }) }}
                  <span aria-hidden="true">-</span>
                  {{ formatMoney(group.totalSpend, group.baseCurrency) }}
                </span>
              </span>
              <button
                v-if="group.isArchived && deleting !== group.id"
                type="button"
                data-testid="delete-group"
                class="btn btn-press btn-secondary min-h-0 shrink-0 px-2 py-1 text-xs"
                style="border-color: var(--border)"
                @click="startDelete(group)"
              >{{ t('Delete') }}
              </button>
            </div>
            <p class="mt-1 text-xs text-[var(--text-muted)]">
              {{ t('Made by {name}', { name: group.createdByName }) }}
              <template v-if="on(group.lastActivityAt)">
                <span aria-hidden="true"> - </span>
                {{ t('last used {date}', { date: on(group.lastActivityAt) ?? '' }) }}
              </template>
              <template v-if="group.isMine">
                <span aria-hidden="true"> - </span>{{ t("you're in it") }}
              </template>
            </p>
            <div v-if="deleting === group.id" class="mt-3 flex flex-col gap-2">
              <p class="text-xs text-[var(--text-muted)]">
                {{ t('This removes the group and everything in it: {expenses} expenses, every settlement, every comment. It cannot be undone. Type its name to confirm.', {
                  expenses: group.expenseCount,
                }) }}
              </p>
              <input
                v-model="typedName"
                type="text"
                data-testid="confirm-name"
                :placeholder="group.name"
                class="tap-target rounded-lg border bg-[var(--surface-raised)] px-3 text-sm"
                style="border-color: var(--border)"
              />
              <div class="flex gap-2">
                <button
                  type="button"
                  class="btn btn-press btn-secondary flex-1"
                  style="border-color: var(--border)"
                  :disabled="isDeleting"
                  @click="cancelDelete"
                >{{ t('Keep it') }}
                </button>
                <button
                  type="button"
                  data-testid="confirm-delete"
                  class="btn btn-press btn-danger flex-1"
                  :disabled="!canDelete(group) || isDeleting"
                  @click="remove(group)"
                >
                  {{ isDeleting ? t('Deleting') : t('Delete for good') }}
                </button>
              </div>
            </div>
          </li>
        </ul>
      </section>
      <p v-if="!isLoading && all.length === 0" class="surface-card p-6 text-center text-sm text-[var(--text-muted)]">
        {{ t('No groups on this server.') }}
      </p>
      <section class="surface-card mb-5 p-4">
        <h2 class="mb-1 text-sm font-medium text-[var(--text-muted)]">{{ t('Categories') }}</h2>
        <CategoryEditor
          :categories="categories"
          :is-saving="isSavingCategories"
          :description="t('Every group starts from this list. A group that has edited its own keeps that one, and nothing here reaches it.')"
          @save="saveCategories"
        />
      </section>
    </template>
  </AppShell>
</template>
