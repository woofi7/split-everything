<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { FontAwesomeIcon } from '@fortawesome/vue-fontawesome'
import { faCodeMerge } from '@fortawesome/free-solid-svg-icons'
import AppShell from '@/components/layout/AppShell.vue'
import IconPicker from '@/components/ui/IconPicker.vue'
import PersonPicker from '@/components/groups/PersonPicker.vue'
import { resolveIcon } from '@/domain/icons'
import { useGroupsStore } from '@/stores/groups'
import { useAuthStore } from '@/stores/auth'
import { useApi } from '@/api/provider'
import type { AddableUser } from '@/api/types'

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
const auth = useAuthStore()

const groupId = computed(() => String(route.params.groupId))

/**
 * Which of these people is the one reading the list.
 *
 * By member row rather than by name: names repeat, and a group can hold two
 * people called Nicolas.
 */
const myMemberId = computed(() =>
  auth.user ? groups.myMemberId(groupId.value, auth.user.id) : null,
)
const name = ref('')
const iconName = ref<string | null>(null)
const isPickingIcon = ref(false)
const inviteEmail = ref('')
const newInvite = ref<InviteDto | null>(null)
const qrUrl = ref<string | null>(null)
const addable = ref<AddableUser[]>([])
const error = ref<string | null>(null)
const message = ref<string | null>(null)

/** Open while two people are being chosen, and closed the moment it is done. */
const isMergeOpen = ref(false)
const mergeSource = ref('')
const mergeTarget = ref('')
const isMerging = ref(false)
/** Kept apart from the page error, so a refusal is not reported twice. */
const mergeError = ref<string | null>(null)

/**
 * Who can be merged away.
 *
 * Removed members included, and that is the point: removing a member deactivates
 * it rather than deleting it precisely because it still holds expenses, so a
 * removed placeholder is the most likely thing anyone wants to merge. Never the
 * owner, so the group always keeps one.
 */
const mergeSources = computed(() =>
  (group.value?.members ?? []).filter(
    (member) => member.role !== 'Owner' && member.id !== mergeTarget.value,
  ),
)

/**
 * Who can be merged into.
 *
 * Active only: everything ends up here, and a removed member is one nobody can
 * see, so the history would be there and invisible.
 */
const mergeTargets = computed(() =>
  (group.value?.members ?? []).filter(
    (member) => member.status === 'Active' && member.id !== mergeSource.value,
  ),
)

/**
 * Whether the person reading this can merge at all.
 *
 * It rewrites everyone's balances, so the server allows it only to an owner or an
 * admin. Asked here too, or a plain member would be offered a button that answers
 * with a refusal.
 */
const canAdminister = computed(() => {
  const mine = (group.value?.members ?? []).find((member) => member.id === myMemberId.value)
  return mine?.role === 'Owner' || mine?.role === 'Admin'
})

const canMerge = computed(
  () => canAdminister.value && mergeSources.value.length > 0 && mergeTargets.value.length > 0,
)

/** Both chosen, and not the same person twice. */
const isMergeReady = computed(
  () =>
    mergeSource.value !== '' &&
    mergeTarget.value !== '' &&
    mergeSource.value !== mergeTarget.value,
)

const nameOf = (memberId: string) =>
  (group.value?.members ?? []).find((member) => member.id === memberId)?.displayName ?? ''

/**
 * How a person reads in the two lists.
 *
 * Said out loud, because the whole reason for merging is two rows with the same
 * name: without this the list is the same word twice.
 */
const labelFor = (member: { displayName: string; status: string; isPlaceholder: boolean }) => {
  if (member.status !== 'Active') return `${member.displayName} (removed)`
  if (member.isPlaceholder) return `${member.displayName} (not signed in yet)`
  return member.displayName
}

function openMerge(): void {
  error.value = null
  message.value = null
  mergeError.value = null
  mergeSource.value = ''
  mergeTarget.value = ''
  isMergeOpen.value = true
}

async function confirmMerge(): Promise<void> {
  if (!isMergeReady.value) return

  const goingName = nameOf(mergeSource.value)
  const stayingName = nameOf(mergeTarget.value)
  mergeError.value = null
  isMerging.value = true

  try {
    await groups.mergeMembers(groupId.value, mergeSource.value, mergeTarget.value)
    message.value = `${goingName} was merged into ${stayingName}.`
    isMergeOpen.value = false
    await loadAddable()
  } catch (caught) {
    mergeError.value = caught instanceof Error ? caught.message : 'Could not merge those two.'
  } finally {
    isMerging.value = false
  }
}


onMounted(async () => {
  const group = await groups.get(groupId.value)
  name.value = group?.name ?? ''
  iconName.value = group?.iconName ?? null
  await loadAddable()
})

const icon = computed(() => resolveIcon(iconName.value))

const group = computed(() => groups.groups.find((candidate) => candidate.id === groupId.value))

async function save(): Promise<void> {
  error.value = null
  try {
    await groups.update(groupId.value, { name: name.value, iconName: iconName.value })
    message.value = 'Saved.'
  } catch (caught) {
    error.value = caught instanceof Error ? caught.message : 'Could not save the group.'
  }
}

/** Choosing an icon saves straight away: there is no other reason to be here. */
async function chooseIcon(next: string | null): Promise<void> {
  iconName.value = next
  await save()
}

async function loadAddable(): Promise<void> {
  try {
    addable.value = await groups.addableUsers(groupId.value)
  } catch {
    // Not fatal: the field falls back to adding someone by name, which is all it
    // could do before anyway.
    addable.value = []
  }
}

/** Someone who already has an account, so they see the group straight away. */
async function addPerson(person: AddableUser): Promise<void> {
  error.value = null
  try {
    await groups.addUserMember(groupId.value, person.id)
    await loadAddable()
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

  // The clipboard API needs a secure context, which a plain LAN address is not.
  // Claiming success there told people the link was copied when nothing had
  // happened, so say what is going on and leave the link on screen to select.
  if (!navigator.clipboard) {
    message.value = null
    error.value = 'Copying needs a secure connection. The link above can be selected instead.'
    return
  }

  try {
    await navigator.clipboard.writeText(newInvite.value.url)
    error.value = null
    message.value = 'Invite link copied.'
  } catch {
    message.value = null
    error.value = 'Could not copy the link. It can be selected above instead.'
  }
}

async function archive(): Promise<void> {
  error.value = null
  try {
    await groups.archive(groupId.value)
    await router.replace({ name: 'dashboard' })
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
  <AppShell
    title="Group settings"
    :subtitle="group?.name"
    :back-to="{ name: 'group', params: { groupId } }"
    :back-label="group?.name ?? 'Group'"
  >
    <form class="surface-card mb-4 flex flex-col gap-3 p-4" @submit.prevent="save">
      <div class="flex items-end gap-3">
        <div class="flex flex-col gap-1">
          <span class="text-sm text-[var(--text-muted)]">Icon</span>
          <button
            type="button"
            class="tap-target flex h-11 w-11 items-center justify-center rounded-lg text-white"
            :style="{ backgroundColor: group?.colorHex ?? '#4f46e5' }"
            :data-icon="icon.name"
            :aria-label="`Group icon: ${icon.label}. Choose a different one`"
            @click="isPickingIcon = true"
          >
            <FontAwesomeIcon :icon="icon.definition" class="h-5 w-5" aria-hidden="true" />
          </button>
        </div>

        <label class="flex flex-1 flex-col gap-1">
          <span class="text-sm text-[var(--text-muted)]">Name</span>
          <input
            v-model="name"
            type="text"
            maxlength="120"
            class="tap-target rounded-lg border bg-[var(--surface)] px-3"
            style="border-color: var(--border)"
          />
        </label>
      </div>

      <button type="submit" class="btn btn-press btn-primary">
        Save
      </button>
    </form>

    <section class="surface-card mb-4 p-4">
      <div class="mb-2 flex items-center justify-between gap-2">
        <h2 class="text-sm font-medium text-[var(--text-muted)]">People</h2>

        <!--
          One action for the section, in the corner, where an action about the
          list as a whole belongs rather than beside any one name in it.
        -->
        <button
          v-if="canMerge && !isMergeOpen"
          type="button"
          data-testid="merge-open"
          class="btn btn-press btn-secondary h-11 w-11 shrink-0 rounded-full px-0"
          style="border-color: var(--border)"
          aria-label="Merge two people"
          title="Merge two people"
          @click="openMerge"
        >
          <FontAwesomeIcon :icon="faCodeMerge" class="h-4 w-4" />
        </button>
      </div>

      <ul class="mb-3 flex flex-col gap-2 text-sm">
        <li
          v-for="member in group?.members ?? []"
          :key="member.id"
          class="flex items-center justify-between gap-2"
        >
          <span class="min-w-0 truncate">
            {{ member.displayName }}
            <span
              v-if="member.id === myMemberId"
              data-testid="you-tag"
              class="ml-1 rounded-full px-1.5 py-0.5 align-middle text-[0.65rem] font-semibold uppercase tracking-wide"
              style="background: var(--surface-sunken); color: var(--text-muted)"
            >
              You
            </span>
            <!--
              Who can change the group, and the one person no merge or removal can
              take out of it. Worth saying on the row rather than leaving people to
              work it out from which controls they were offered.
            -->
            <span
              v-if="member.role === 'Owner'"
              data-testid="owner-tag"
              class="ml-1 rounded-full px-1.5 py-0.5 align-middle text-[0.65rem] font-semibold uppercase tracking-wide"
              style="background: color-mix(in oklab, var(--color-brand-600) 22%, transparent); color: var(--color-brand-400)"
            >
              Owner
            </span>
            <!--
              Only while they are still in the group. Saying someone has not signed
              in yet and has been removed is two facts where one will do, and the
              second is the one that matters.
            -->
            <span
              v-if="member.isPlaceholder && member.status === 'Active'"
              class="text-xs text-[var(--text-muted)]"
            >
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

      <!--
        The warning is the feature. Everything this moves keeps working afterwards,
        which is exactly why a mistake is invisible: the balances are simply wrong
        from then on, and nothing records what they used to be.
      -->
      <div
        v-if="isMergeOpen"
        data-testid="merge-confirm"
        class="mb-3 flex flex-col gap-3 rounded-lg border p-3"
        style="border-color: var(--color-owing)"
        role="alertdialog"
        aria-label="Merge two people"
      >
        <p class="text-sm text-[var(--text-muted)]">
          For one person who ended up in this group twice. Everything the first
          paid, owes and is owed moves to the second, and the first is removed.
        </p>

        <label class="flex flex-col gap-1">
          <span class="text-xs text-[var(--text-muted)]">Merge this person</span>
          <select
            v-model="mergeSource"
            data-testid="merge-source"
            class="tap-target rounded-lg border bg-[var(--surface)] px-3"
            style="border-color: var(--border)"
          >
            <option value="" disabled>Choose who goes</option>
            <option v-for="person in mergeSources" :key="person.id" :value="person.id">
              {{ labelFor(person) }}
            </option>
          </select>
        </label>

        <label class="flex flex-col gap-1">
          <span class="text-xs text-[var(--text-muted)]">Into this person</span>
          <select
            v-model="mergeTarget"
            data-testid="merge-target"
            class="tap-target rounded-lg border bg-[var(--surface)] px-3"
            style="border-color: var(--border)"
          >
            <option value="" disabled>Choose who stays</option>
            <option v-for="person in mergeTargets" :key="person.id" :value="person.id">
              {{ labelFor(person) }}
            </option>
          </select>
        </label>

        <p v-if="isMergeReady" class="text-xs text-owing">
          {{ nameOf(mergeSource) }} will be removed, and everything they paid, owe
          and are owed becomes {{ nameOf(mergeTarget) }}'s. This cannot be undone:
          there is no record of which expenses moved.
        </p>
        <p v-else class="text-xs text-[var(--text-muted)]">
          Choose both. This cannot be undone.
        </p>

        <p v-if="mergeError" class="text-sm text-owing" role="alert">{{ mergeError }}</p>

        <div class="flex gap-2">
          <button
            type="button"
            class="btn btn-press btn-secondary flex-1"
            style="border-color: var(--border)"
            @click="isMergeOpen = false"
          >
            Cancel
          </button>
          <button
            type="button"
            data-testid="merge-confirm-button"
            class="btn btn-press btn-danger flex-1"
            :disabled="!isMergeReady || isMerging"
            @click="confirmMerge"
          >
            {{ isMerging ? 'Merging' : 'Merge for good' }}
          </button>
        </div>
      </div>

      <PersonPicker
        :candidates="addable"
        label="Add someone to this group"
        @pick="addPerson"
      />
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
          class="btn btn-press btn-primary"
          @click="createInvite"
        >
          Invite
        </button>
      </div>

      <div v-if="newInvite" class="mt-3 flex flex-col items-center gap-3">
        <img v-if="qrUrl" :src="qrUrl" alt="Invite QR code" class="h-40 w-40 rounded-lg bg-white p-2" />

        <!-- Readable on its own, so an invite can be shared without the clipboard. -->
        <p class="w-full break-all text-center text-xs text-[var(--text-muted)]">
          {{ newInvite.url }}
        </p>

        <button
          type="button"
          class="btn btn-press btn-secondary"
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
        class="btn btn-press btn-secondary w-full justify-start"
        @click="archive"
      >
        Archive this group
      </button>
      <button v-else type="button" class="btn btn-press btn-secondary w-full justify-start" @click="unarchive">
        Reopen this group
      </button>
      <p class="text-xs text-[var(--text-muted)]">
        Archiving freezes a group without deleting anything. Balances and history stay readable.
      </p>
    </section>

    <IconPicker
      :open="isPickingIcon"
      :model-value="iconName"
      title="Group icon"
      @update:model-value="chooseIcon"
      @close="isPickingIcon = false"
    />

    <p v-if="message" class="mt-4 text-sm text-owed" role="status">{{ message }}</p>
    <p v-if="error" class="mt-4 text-sm text-owing" role="alert">{{ error }}</p>
  </AppShell>
</template>
