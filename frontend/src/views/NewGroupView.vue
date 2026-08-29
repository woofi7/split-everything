<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import AppShell from '@/components/layout/AppShell.vue'
import { useGroupsStore } from '@/stores/groups'

const groups = useGroupsStore()
const router = useRouter()

const name = ref('')
const baseCurrency = ref('CAD')
const emojiIcon = ref('')
const memberDraft = ref('')
const memberNames = ref<string[]>([])
const error = ref<string | null>(null)
const isSaving = ref(false)

const currencies = ['CAD', 'USD', 'EUR', 'GBP', 'CHF', 'AUD', 'JPY']

function addMember(): void {
  const trimmed = memberDraft.value.trim()
  if (!trimmed) return

  if (memberNames.value.some((existing) => existing.toLowerCase() === trimmed.toLowerCase())) {
    memberDraft.value = ''
    return
  }

  memberNames.value.push(trimmed)
  memberDraft.value = ''
}

function removeMember(index: number): void {
  memberNames.value.splice(index, 1)
}

async function save(): Promise<void> {
  error.value = null

  if (!name.value.trim()) {
    error.value = 'Give the group a name.'
    return
  }

  isSaving.value = true

  try {
    const group = await groups.create({
      name: name.value.trim(),
      baseCurrency: baseCurrency.value,
      emojiIcon: emojiIcon.value.trim() || null,
      placeholderMemberNames: memberNames.value,
    })

    await router.replace({ name: 'group', params: { groupId: group.id } })
  } catch (caught) {
    error.value = caught instanceof Error ? caught.message : 'Could not create the group.'
  } finally {
    isSaving.value = false
  }
}
</script>

<template>
  <AppShell title="New group" :show-nav="false">
    <form class="flex flex-col gap-5" @submit.prevent="save">
      <label class="flex flex-col gap-1">
        <span class="text-sm text-[var(--text-muted)]">Name</span>
        <input
          v-model="name"
          type="text"
          required
          maxlength="120"
          placeholder="Roommates"
          class="tap-target rounded-lg border bg-[var(--surface-raised)] px-3"
          style="border-color: var(--border)"
        />
      </label>

      <div class="grid grid-cols-2 gap-3">
        <label class="flex flex-col gap-1">
          <span class="text-sm text-[var(--text-muted)]">Currency</span>
          <select
            v-model="baseCurrency"
            class="tap-target rounded-lg border bg-[var(--surface-raised)] px-3"
            style="border-color: var(--border)"
          >
            <option v-for="code in currencies" :key="code" :value="code">{{ code }}</option>
          </select>
        </label>

        <label class="flex flex-col gap-1">
          <span class="text-sm text-[var(--text-muted)]">Icon</span>
          <input
            v-model="emojiIcon"
            type="text"
            maxlength="4"
            placeholder="house"
            class="tap-target rounded-lg border bg-[var(--surface-raised)] px-3"
            style="border-color: var(--border)"
          />
        </label>
      </div>

      <div class="flex flex-col gap-2">
        <span class="text-sm text-[var(--text-muted)]">
          People, by name. You can invite them properly later.
        </span>

        <div class="flex gap-2">
          <input
            v-model="memberDraft"
            type="text"
            placeholder="Bob"
            class="tap-target flex-1 rounded-lg border bg-[var(--surface-raised)] px-3"
            style="border-color: var(--border)"
            @keydown.enter.prevent="addMember"
          />
          <button
            type="button"
            class="tap-target rounded-lg border px-3 text-sm"
            style="border-color: var(--border)"
            @click="addMember"
          >
            Add
          </button>
        </div>

        <ul v-if="memberNames.length > 0" class="flex flex-wrap gap-2">
          <li
            v-for="(member, index) in memberNames"
            :key="member"
            class="flex items-center gap-1 rounded-full border px-2 py-1 text-sm"
            style="border-color: var(--border)"
          >
            {{ member }}
            <button
              type="button"
              class="text-[var(--text-muted)]"
              :aria-label="`Remove ${member}`"
              @click="removeMember(index)"
            >
              x
            </button>
          </li>
        </ul>
      </div>

      <p v-if="error" class="text-sm text-owing" role="alert">{{ error }}</p>

      <button
        type="submit"
        class="tap-target rounded-lg bg-brand-600 font-medium text-white disabled:opacity-60"
        :disabled="isSaving"
      >
        {{ isSaving ? 'Creating' : 'Create group' }}
      </button>
    </form>
  </AppShell>
</template>
