<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import AppShell from '@/components/layout/AppShell.vue'
import { useGroupsStore } from '@/stores/groups'
import { useApi } from '@/api/provider'

interface InviteDto {
  id: string
  token: string
  url: string
  invitedEmail: string | null
  expiresAt: string
  maxUses: number
  useCount: number
}

const route = useRoute()
const router = useRouter()
const groups = useGroupsStore()

const groupId = computed(() => String(route.params.groupId))
const name = ref('')
const inviteEmail = ref('')
const newInvite = ref<InviteDto | null>(null)
const qrUrl = ref<string | null>(null)
const memberDraft = ref('')
const error = ref<string | null>(null)
const message = ref<string | null>(null)


onMounted(async () => {
  const group = await groups.get(groupId.value)
  name.value = group?.name ?? ''
})

const group = computed(() => groups.groups.find((candidate) => candidate.id === groupId.value))

async function rename(): Promise<void> {
  error.value = null
  try {
    await groups.update(groupId.value, { name: name.value })
    message.value = 'Saved.'
  } catch (caught) {
    error.value = caught instanceof Error ? caught.message : 'Could not rename the group.'
  }
}

async function addMember(): Promise<void> {
  error.value = null
  try {
    await groups.addPlaceholderMember(groupId.value, memberDraft.value)
    memberDraft.value = ''
  } catch (caught) {
    error.value = caught instanceof Error ? caught.message : 'Could not add that person.'
  }
}

async function removeMember(memberId: string): Promise<void> {
  error.value = null
  try {
    await groups.removeMember(groupId.value, memberId)
  } catch (caught) {
    error.value = caught instanceof Error ? caught.message : 'Could not remove that person.'
  }
}

async function createInvite(): Promise<void> {
  error.value = null
  qrUrl.value = null

  try {
    newInvite.value = await useApi().post<InviteDto>(`/groups/${groupId.value}/invites`, {
      email: inviteEmail.value.trim() || null,
      claimsMemberId: null,
      maxUses: 1,
      expiresInHours: 72,
    })

    // The QR encodes the same invite; it is the alternate presentation, not a
    // second credential.
    const png = await useApi().blob(`/groups/invites/${newInvite.value.id}/qr`, { size: 8 })
    qrUrl.value = URL.createObjectURL(png)
  } catch (caught) {
    error.value = caught instanceof Error ? caught.message : 'Could not create an invite.'
  }
}

async function copyInviteLink(): Promise<void> {
  if (!newInvite.value) return
  await navigator.clipboard?.writeText(newInvite.value.url)
  message.value = 'Invite link copied.'
}

async function archive(): Promise<void> {
  error.value = null
  try {
    await groups.archive(groupId.value)
    await router.replace({ name: 'groups' })
  } catch (caught) {
    error.value = caught instanceof Error ? caught.message : 'Could not archive the group.'
  }
}

async function unarchive(): Promise<void> {
  error.value = null
  try {
    await groups.unarchive(groupId.value)
  } catch (caught) {
    error.value = caught instanceof Error ? caught.message : 'Could not reopen the group.'
  }
}
</script>

<template>
  <AppShell title="Group settings" :subtitle="group?.name" :show-nav="false">
    <form class="surface-card mb-4 flex flex-col gap-3 p-4" @submit.prevent="rename">
      <label class="flex flex-col gap-1">
        <span class="text-sm text-[var(--text-muted)]">Name</span>
        <input
          v-model="name"
          type="text"
          maxlength="120"
          class="tap-target rounded-lg border bg-[var(--surface)] px-3"
          style="border-color: var(--border)"
        />
      </label>
      <button type="submit" class="tap-target rounded-lg bg-brand-600 text-sm font-medium text-white">
        Save
      </button>
    </form>

    <section class="surface-card mb-4 p-4">
      <h2 class="mb-2 text-sm font-medium text-[var(--text-muted)]">People</h2>

      <ul class="mb-3 flex flex-col gap-2 text-sm">
        <li
          v-for="member in group?.members ?? []"
          :key="member.id"
          class="flex items-center justify-between gap-2"
        >
          <span class="min-w-0 truncate">
            {{ member.displayName }}
            <span v-if="member.isPlaceholder" class="text-xs text-[var(--text-muted)]">
              (not signed in yet)
            </span>
            <span v-if="member.status !== 'Active'" class="text-xs text-[var(--text-muted)]">
              (removed)
            </span>
          </span>
          <button
            v-if="member.status === 'Active' && member.role !== 'Owner'"
            type="button"
            class="shrink-0 text-xs text-[var(--text-muted)] underline"
            @click="removeMember(member.id)"
          >
            Remove
          </button>
        </li>
      </ul>

      <div class="flex gap-2">
        <input
          v-model="memberDraft"
          type="text"
          placeholder="Add someone by name"
          class="tap-target flex-1 rounded-lg border bg-[var(--surface)] px-3 text-sm"
          style="border-color: var(--border)"
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
    </section>

    <section class="surface-card mb-4 p-4">
      <h2 class="mb-2 text-sm font-medium text-[var(--text-muted)]">Invite someone</h2>
      <p class="mb-3 text-xs text-[var(--text-muted)]">
        They sign in with Google to join, so the link alone gives no access.
      </p>

      <div class="flex gap-2">
        <input
          v-model="inviteEmail"
          type="email"
          placeholder="Email, or leave blank for a link"
          class="tap-target flex-1 rounded-lg border bg-[var(--surface)] px-3 text-sm"
          style="border-color: var(--border)"
        />
        <button
          type="button"
          class="tap-target rounded-lg bg-brand-600 px-3 text-sm font-medium text-white"
          @click="createInvite"
        >
          Invite
        </button>
      </div>

      <div v-if="newInvite" class="mt-3 flex flex-col items-center gap-3">
        <img v-if="qrUrl" :src="qrUrl" alt="Invite QR code" class="h-40 w-40 rounded-lg bg-white p-2" />
        <button
          type="button"
          class="tap-target rounded-lg border px-3 text-sm"
          style="border-color: var(--border)"
          @click="copyInviteLink"
        >
          Copy the invite link
        </button>
      </div>
    </section>

    <section class="surface-card flex flex-col gap-3 p-4">
      <button
        v-if="!group?.isArchived"
        type="button"
        class="tap-target text-left text-sm"
        @click="archive"
      >
        Archive this group
      </button>
      <button v-else type="button" class="tap-target text-left text-sm" @click="unarchive">
        Reopen this group
      </button>
      <p class="text-xs text-[var(--text-muted)]">
        Archiving freezes a group without deleting anything. Balances and history stay readable.
      </p>
    </section>

    <p v-if="message" class="mt-4 text-sm text-owed" role="status">{{ message }}</p>
    <p v-if="error" class="mt-4 text-sm text-owing" role="alert">{{ error }}</p>
  </AppShell>
</template>
