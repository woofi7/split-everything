<script setup lang="ts">
import { t } from '@/i18n'
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { FontAwesomeIcon } from '@fortawesome/vue-fontawesome'
import AppShell from '@/components/layout/AppShell.vue'
import IconPicker from '@/components/ui/IconPicker.vue'
import PersonPicker from '@/components/groups/PersonPicker.vue'
import { resolveIcon } from '@/domain/icons'
import { useGroupsStore } from '@/stores/groups'
import type { AddableUser } from '@/api/types'

const groups = useGroupsStore()
const router = useRouter()

const name = ref('')
const baseCurrency = ref('CAD')
const iconName = ref<string | null>(null)
const isPickingIcon = ref(false)

const icon = computed(() => resolveIcon(iconName.value))
const addable = ref<AddableUser[]>([])
const chosen = ref<AddableUser[]>([])
const error = ref<string | null>(null)
const isSaving = ref(false)

const currencies = ['CAD', 'USD', 'EUR', 'GBP', 'CHF', 'AUD', 'JPY']

onMounted(async () => {
  try {
    // No group id: nobody is a member of a group that does not exist yet.
    addable.value = await groups.addableUsers()
  } catch {
    // The field still adds people by name, which is all it could do before.
    addable.value = []
  }
})

/** Held until the group exists, since there is nothing to add them to yet. */
function addPerson(person: AddableUser): void {
  if (chosen.value.some((existing) => existing.id === person.id)) return
  chosen.value.push(person)
}

function removePerson(index: number): void {
  chosen.value.splice(index, 1)
}

async function save(): Promise<void> {
  error.value = null

  if (!name.value.trim()) {
    error.value = t('Give the group a name.')
    return
  }

  isSaving.value = true

  try {
    const group = await groups.create({
      name: name.value.trim(),
      baseCurrency: baseCurrency.value,
      iconName: iconName.value,
    })

    // Sequential rather than part of the create: a member row needs a group to
    // belong to. A failure here is not worth stranding anyone on this screen for,
    // since the group itself is already made and the settings page can finish the
    // job.
    for (const person of chosen.value) {
      try {
        await groups.addUserMember(group.id, person.id)
      } catch {
        // Reported on the group screen, where the roster is visible.
      }
    }

    await router.replace({ name: 'group', params: { groupId: group.id } })
  } catch (caught) {
    error.value = caught instanceof Error ? caught.message : t('Could not create the group.')
  } finally {
    isSaving.value = false
  }
}
</script>

<template>
  <AppShell :title="t('New group')" :back-to="{ name: 'dashboard' }" :back-label="t('Dashboard')">
    <form class="flex flex-col gap-5" @submit.prevent="save">
      <label class="flex flex-col gap-1">
        <span class="text-sm text-[var(--text-muted)]">{{ t('Name') }}</span>
        <input
          v-model="name"
          type="text"
          required
          maxlength="120"
          :placeholder="t('Roommates')"
          class="tap-target rounded-lg border bg-[var(--surface-raised)] px-3"
          style="border-color: var(--border)"
        />
      </label>

      <div class="grid grid-cols-2 gap-3">
        <label class="flex flex-col gap-1">
          <span class="text-sm text-[var(--text-muted)]">{{ t('Currency') }}</span>
          <select
            v-model="baseCurrency"
            class="tap-target rounded-lg border bg-[var(--surface-raised)] px-3"
            style="border-color: var(--border)"
          >
            <option v-for="code in currencies" :key="code" :value="code">{{ code }}</option>
          </select>
        </label>

        <div class="flex flex-col gap-1">
          <span class="text-sm text-[var(--text-muted)]">{{ t('Icon') }}</span>
          <button
            type="button"
            class="btn btn-press btn-secondary"
            style="border-color: var(--border)"
            @click="isPickingIcon = true"
          >
            <span
              class="flex h-6 w-6 items-center justify-center rounded-md text-white"
              :style="{ backgroundColor: '#4f46e5' }"
              aria-hidden="true"
            >
              <FontAwesomeIcon :icon="icon.definition" class="h-3.5 w-3.5" />
            </span>
            {{ iconName ? icon.label : t('Choose') }}
          </button>
        </div>
      </div>

      <div class="flex flex-col gap-2">
        <span class="text-sm text-[var(--text-muted)]">{{ t('People. Search anyone who already has an account, or invite them once the group exists.') }}
        </span>

        <PersonPicker
          :candidates="addable"
          :label="t('Add someone to this group')"
          @pick="addPerson"
        />

        <ul
          v-if="chosen.length > 0"
          class="flex flex-wrap gap-2"
          :aria-label="t('People with an account, added so far')"
        >
          <li
            v-for="(person, index) in chosen"
            :key="person.id"
            class="flex items-center gap-1 rounded-full border px-2 py-1 text-sm"
            style="border-color: var(--border)"
          >
            {{ person.displayName }}
            <button
              type="button"
              class="text-[var(--text-muted)]"
              :aria-label="`Remove ${person.displayName}`"
              @click="removePerson(index)"
            >
              x
            </button>
          </li>
        </ul>
      </div>

      <p v-if="error" class="text-sm text-owing" role="alert">{{ error }}</p>

      <button
        type="submit"
        class="btn btn-press btn-primary w-full"
        :disabled="isSaving"
      >
        {{ isSaving ? t('Creating') : t('Create group') }}
      </button>
    </form>

    <IconPicker
      v-model="iconName"
      :open="isPickingIcon"
      :title="t('Group icon')"
      @close="isPickingIcon = false"
    />
  </AppShell>
</template>
