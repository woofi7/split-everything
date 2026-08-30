<script setup lang="ts">
import { t } from '@/i18n'
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { FontAwesomeIcon } from '@fortawesome/vue-fontawesome'
import { faCodeMerge } from '@fortawesome/free-solid-svg-icons'
import AppShell from '@/components/layout/AppShell.vue'
import IconPicker from '@/components/ui/IconPicker.vue'
import PersonPicker from '@/components/groups/PersonPicker.vue'
import ColorChoice from '@/components/ui/ColorChoice.vue'
import { resolveIcon } from '@/domain/icons'
import { useGroupsStore } from '@/stores/groups'
import { useAuthStore } from '@/stores/auth'
import { useApi } from '@/api/provider'
import type { AddableUser } from '@/api/types'
import type { SplitType } from '@/domain/splitting'

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
 * How this group splits an expense unless someone says otherwise.
 *
 * Shown here because it is a fact about the household rather than about one
 * expense: it was only settable as a side effect of adding one, which meant you
 * could not see what it was, let alone change it, without pretending to spend
 * money.
 *
 * Equal, shares and percentage only. An exact amount is a fact about a particular
 * expense, so as a standing rule it would be wrong the moment the total changed.
 * A group already set to one keeps it until someone picks another, rather than
 * being quietly rewritten by opening this screen.
 */
const SPLIT_CHOICES = [
  {
    value: 'Equal' as SplitType,
    label: t('Equally'),
    hint: t('Everyone taking part pays the same.'),
  },
  {
    value: 'Shares' as SplitType,
    label: t('By shares'),
    hint: t('Two shares against one pays twice as much.'),
  },
  {
    value: 'Percentage' as SplitType,
    label: t('By percentage'),
    hint: t('Has to add up to 100.'),
  },
]

const splitType = ref<SplitType>('Equal')
const splitValues = ref<Record<string, number>>({})
const isSaving = ref(false)

/** Only the people who could be on an expense. */
const activeMembers = computed(() =>
  (group.value?.members ?? []).filter((member) => member.status === 'Active'),
)

const splitNeedsValues = computed(() => splitType.value !== 'Equal')

const splitTotal = computed(() =>
  activeMembers.value.reduce((sum, member) => sum + (Number(splitValues.value[member.id]) || 0), 0),
)

/** What is wrong with the numbers as they stand, or null. */
const splitProblem = computed(() => {
  if (!splitNeedsValues.value) return null
  if (splitTotal.value <= 0) return 'Give at least one person a number.'
  if (splitType.value === 'Percentage' && Math.abs(splitTotal.value - 100) > 0.01) {
    return `Percentages add up to ${splitTotal.value.toFixed(2)}, not 100.`
  }
  return null
})

/** The current setting, put into the form. Called whenever the group arrives. */
function readSplitFromGroup(): void {
  const current = group.value
  splitType.value = current?.defaultSplitType ?? 'Equal'

  const stored = current?.defaultSplitValues ?? {}
  const seeded: Record<string, number> = {}
  for (const member of activeMembers.value) {
    seeded[member.id] = stored[member.id] ?? (splitType.value === 'Percentage' ? 0 : 1)
  }
  splitValues.value = seeded
}

/** Seeds sensible numbers when the type changes, rather than leaving them blank. */
function changeSplitType(next: SplitType): void {
  splitType.value = next
  message.value = null
  error.value = null

  if (next === 'Equal') return

  const people = activeMembers.value
  const seeded: Record<string, number> = {}
  for (const member of people) {
    seeded[member.id] = next === 'Percentage'
      ? Math.round((100 / Math.max(people.length, 1)) * 100) / 100
      : 1
  }
  splitValues.value = seeded
}



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

/** Which member's colour row is open, since twelve swatches each is a wall. */
const colouring = ref<string | null>(null)

/**
 * Colours picked but not saved, by member.
 *
 * Staged like the rest of the screen, so one button saves everything and a colour
 * is not committed by a stray tap. Which colour another person ends up with when
 * one is taken from them is the server's to work out, so this only records what
 * was asked for; the answer arrives with the group.
 */
const pendingColours = ref<Record<string, string>>({})

/** Whose colour this person may change: their own, or anyone's if they run the group. */
function canRecolour(memberId: string): boolean {
  return canAdminister.value || memberId === myMemberId.value
}

/** The colours in use, so the row can show which are spoken for. */
const takenColours = computed(() =>
  (group.value?.members ?? [])
    .map((member) => colourOf(member.id))
    .filter((colour): colour is string => !!colour),
)

const colourOf = (memberId: string) =>
  pendingColours.value[memberId]
  ?? (group.value?.members ?? []).find((member) => member.id === memberId)?.colorHex
  ?? null

function pickColour(memberId: string, colorHex: string): void {
  const stored = (group.value?.members ?? []).find((member) => member.id === memberId)?.colorHex
  const staged = { ...pendingColours.value }

  // Back to what the group holds is not a change, so it stops being one.
  if (stored && stored.toLowerCase() === colorHex.toLowerCase()) {
    delete staged[memberId]
    pendingColours.value = staged
    colouring.value = null
    return
  }

  // The swap, shown rather than waited for. The server does this when a colour is
  // taken from somebody, and a preview that leaves two people the same colour is
  // showing the one thing this feature exists to prevent.
  const displaced = (group.value?.members ?? []).find(
    (member) => member.id !== memberId && colourOf(member.id)?.toLowerCase() === colorHex.toLowerCase(),
  )
  const mine = colourOf(memberId)
  if (displaced && mine) staged[displaced.id] = mine

  staged[memberId] = colorHex
  pendingColours.value = staged
  colouring.value = null
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
    mergeError.value = caught instanceof Error ? caught.message : t('Could not merge those two.')
  } finally {
    isMerging.value = false
  }
}


onMounted(async () => {
  const loaded = await groups.get(groupId.value)
  name.value = loaded?.name ?? ''
  iconName.value = loaded?.iconName ?? null
  readSplitFromGroup()
  await loadAddable()
})

const icon = computed(() => resolveIcon(iconName.value))

const group = computed(() => groups.groups.find((candidate) => candidate.id === groupId.value))

// The group arrives from the cache first and the server a moment later, and the
// second one is the one worth showing. Declared after the group it watches: watch
// reads it during setup, so above it this is a const in its dead zone.
watch(group, readSplitFromGroup)

/**
 * Whether anything on this screen differs from the group as it stands.
 *
 * The name, the icon and the default split are settings: they are edited and then
 * kept, so they are saved together, once. Adding a person, removing one, changing
 * a colour and creating an invite are actions rather than settings, and stay
 * immediate.
 */
const isDirty = computed(() => {
  const current = group.value
  if (!current) return false

  if (name.value.trim() !== current.name) return true
  if ((iconName.value ?? null) !== (current.iconName ?? null)) return true

  const storedType = current.defaultSplitType ?? 'Equal'
  if (splitType.value !== storedType) return true
  if (!splitNeedsValues.value) return false

  const stored = current.defaultSplitValues ?? {}
  return activeMembers.value.some(
    (member) => (stored[member.id] ?? null) !== (splitValues.value[member.id] ?? null),
  )
})

/** Anything at all to save, colours included. */
const hasChanges = computed(
  () => isDirty.value || Object.keys(pendingColours.value).length > 0,
)

/**
 * Puts every field back to the group, so a change can be abandoned.
 *
 * Reads the group again rather than remembering what things were: the group is
 * the thing being edited, so it is the only honest starting point, and it may have
 * moved on underneath while the screen was open.
 */
function revert(): void {
  const current = group.value
  name.value = current?.name ?? ''
  iconName.value = current?.iconName ?? null
  pendingColours.value = {}
  readSplitFromGroup()
  message.value = null
  error.value = null
}

/**
 * Saves the lot in one request.
 *
 * The group's fields and its default split are the same PATCH, so there is no
 * reason for two round trips or for one of them to succeed alone.
 */
async function save(): Promise<void> {
  if (splitProblem.value) return

  error.value = null
  message.value = null
  isSaving.value = true

  try {
    if (isDirty.value) {
      await groups.update(groupId.value, {
        name: name.value,
        iconName: iconName.value,
        defaultSplitType: splitType.value,
        defaultSplitValues: splitNeedsValues.value ? splitValues.value : null,
      })
    }

    // One at a time, and each its own request: a colour is a change to one
    // member, and the server may move somebody else to make room for it.
    for (const [memberId, colorHex] of Object.entries(pendingColours.value)) {
      await groups.setMemberColor(groupId.value, memberId, colorHex)
    }
    pendingColours.value = {}

    message.value = t('Saved.')
  } catch (caught) {
    error.value = caught instanceof Error ? caught.message : t('Could not save the group.')
  } finally {
    isSaving.value = false
  }
}

/** Chosen, not saved: the one save at the foot of the screen does that. */
function chooseIcon(next: string | null): void {
  iconName.value = next
  isPickingIcon.value = false
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
    error.value = caught instanceof Error ? caught.message : t('Could not add that person.')
  }
}

async function removeMember(memberId: string): Promise<void> {
  error.value = null
  try {
    await groups.removeMember(groupId.value, memberId)
  } catch (caught) {
    error.value = caught instanceof Error ? caught.message : t('Could not remove that person.')
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
    error.value = caught instanceof Error ? caught.message : t('Could not create an invite.')
  }
}

async function copyInviteLink(): Promise<void> {
  if (!newInvite.value) return

  // The clipboard API needs a secure context, which a plain LAN address is not.
  // Claiming success there told people the link was copied when nothing had
  // happened, so say what is going on and leave the link on screen to select.
  if (!navigator.clipboard) {
    message.value = null
    error.value = t('Copying needs a secure connection. The link above can be selected instead.')
    return
  }

  try {
    await navigator.clipboard.writeText(newInvite.value.url)
    error.value = null
    message.value = t('Invite link copied.')
  } catch {
    message.value = null
    error.value = t('Could not copy the link. It can be selected above instead.')
  }
}

async function archive(): Promise<void> {
  error.value = null
  try {
    await groups.archive(groupId.value)
    await router.replace({ name: 'dashboard' })
  } catch (caught) {
    error.value = caught instanceof Error ? caught.message : t('Could not archive the group.')
  }
}

async function unarchive(): Promise<void> {
  error.value = null
  try {
    await groups.unarchive(groupId.value)
  } catch (caught) {
    error.value = caught instanceof Error ? caught.message : t('Could not reopen the group.')
  }
}
</script>

<template>
  <AppShell
    :title="t('Group settings')"
    :subtitle="group?.name"
    :back-to="{ name: 'group', params: { groupId } }"
    :back-label="group?.name ?? 'Group'"
  >
    <form class="surface-card mb-4 flex flex-col gap-3 p-4" @submit.prevent="save">
      <div class="flex items-end gap-3">
        <div class="flex flex-col gap-1">
          <span class="text-sm text-[var(--text-muted)]">{{ t('Icon') }}</span>
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
          <span class="text-sm text-[var(--text-muted)]">{{ t('Name') }}</span>
          <input
            v-model="name"
            type="text"
            maxlength="120"
            class="tap-target rounded-lg border bg-[var(--surface)] px-3"
            style="border-color: var(--border)"
          />
        </label>
      </div>

      <!-- Enter still saves, but the button that does it is at the foot of the
           screen, where it can speak for every setting rather than just these. -->
      <button type="submit" class="hidden" aria-hidden="true" tabindex="-1" />
    </form>

    <!--
      A fact about the household rather than about one expense, so it belongs on
      the group's own screen. It used to be settable only as a side effect of
      adding an expense, which meant you could not see what it was.
    -->
    <section class="surface-card mb-4 p-4">
      <h2 class="mb-2 text-sm font-medium text-[var(--text-muted)]">{{ t('How a new expense is split') }}
      </h2>

      <div class="flex flex-col gap-2" role="radiogroup" :aria-label="t('How a new expense is split')">
        <label
          v-for="choice in SPLIT_CHOICES"
          :key="choice.value"
          class="flex cursor-pointer items-start gap-3 rounded-lg p-2"
          :class="splitType === choice.value ? 'bg-[var(--surface-sunken)]' : ''"
        >
          <input
            type="radio"
            name="default-split"
            :value="choice.value"
            :checked="splitType === choice.value"
            :data-testid="`split-${choice.value}`"
            :disabled="!canAdminister"
            class="mt-1"
            @change="changeSplitType(choice.value)"
          />
          <span class="min-w-0">
            <span class="block text-sm">{{ choice.label }}</span>
            <span class="block text-xs text-[var(--text-muted)]">{{ choice.hint }}</span>
          </span>
        </label>
      </div>

      <!--
        An exact amount is a fact about a particular expense, so it is not offered
        as a standing rule. A group already set to one keeps it until someone picks
        another rather than being rewritten by opening this screen.
      -->
      <p
        v-if="!SPLIT_CHOICES.some((choice) => choice.value === splitType)"
        class="mt-2 text-xs text-[var(--text-muted)]"
      >
        Currently set to {{ splitType }}, which is not offered here. Picking one
        above replaces it.
      </p>

      <ul v-if="splitNeedsValues" class="mt-3 flex flex-col gap-2">
        <li
          v-for="member in activeMembers"
          :key="member.id"
          class="flex items-center justify-between gap-3 text-sm"
        >
          <label class="min-w-0 flex-1 truncate" :for="`split-value-${member.id}`">
            {{ member.displayName }}
          </label>
          <span class="flex shrink-0 items-center gap-1">
            <input
              :id="`split-value-${member.id}`"
              v-model.number="splitValues[member.id]"
              type="number"
              min="0"
              :step="splitType === 'Percentage' ? '0.01' : '1'"
              :disabled="!canAdminister"
              class="tap-target w-24 rounded-lg border bg-[var(--surface)] px-2 text-right"
              style="border-color: var(--border)"
            />
            <span class="w-4 text-xs text-[var(--text-muted)]">
              {{ splitType === 'Percentage' ? '%' : 'x' }}
            </span>
          </span>
        </li>
      </ul>

      <p v-if="splitNeedsValues" class="mt-2 text-xs text-[var(--text-muted)]">
        {{ t('Total') }}
        {{ splitType === 'Percentage'
          ? `${splitTotal.toFixed(2)}%`
          : t('{count} shares', { count: splitTotal }) }}
      </p>

      <p v-if="splitProblem" class="mt-2 text-xs text-owing" role="alert">{{ splitProblem }}</p>

      <p v-if="!canAdminister" class="mt-3 text-xs text-[var(--text-muted)]">{{ t('Only an owner or an admin can change this.') }}
      </p>
    </section>

    <section class="surface-card mb-4 p-4">
      <div class="mb-2 flex items-center justify-between gap-2">
        <h2 class="text-sm font-medium text-[var(--text-muted)]">{{ t('People') }}</h2>

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
          :aria-label="t('Merge two people')"
          :title="t('Merge two people')"
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
            >{{ t('You') }}
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
            >{{ t('Owner') }}
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
          <span class="flex shrink-0 items-center gap-3">
            <!--
              The person's colour, and the way to change it. A swatch rather than a
              word because the colour is the thing being chosen, and it is what the
              expense cards and the charts use.
            -->
            <button
              type="button"
              :data-testid="`recolour-${member.id}`"
              class="h-5 w-5 rounded-full transition-transform active:scale-95 disabled:opacity-60"
              :style="{ backgroundColor: colourOf(member.id) ?? 'var(--surface-sunken)' }"
              :disabled="!canRecolour(member.id)"
              :aria-label="`Colour for ${member.displayName}`"
              :title="canRecolour(member.id) ? `Change the colour for ${member.displayName}` : `Colour for ${member.displayName}`"
              @click="colouring = colouring === member.id ? null : member.id"
            />

            <button
              v-if="member.status === 'Active' && member.role !== 'Owner'"
              type="button"
              class="text-xs text-[var(--text-muted)] underline"
              @click="removeMember(member.id)"
            >{{ t('Remove') }}
            </button>
          </span>
        </li>

        <li v-if="colouring" :key="`${colouring}-colours`" class="pt-1">
          <ColorChoice
            :value="colourOf(colouring)"
            :taken="takenColours"
            :label="'Colour'"
            @pick="(colour) => pickColour(colouring!, colour)"
          />
          <p class="mt-1 text-xs text-[var(--text-muted)]">{{ t('Taking a colour someone else has swaps the two, so nobody ends up without one.') }}
          </p>
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
        :aria-label="t('Merge two people')"
      >
        <p class="text-sm text-[var(--text-muted)]">{{ t('For one person who ended up in this group twice. Everything the first paid, owes and is owed moves to the second, and the first is removed.') }}
        </p>

        <label class="flex flex-col gap-1">
          <span class="text-xs text-[var(--text-muted)]">{{ t('Merge this person') }}</span>
          <select
            v-model="mergeSource"
            data-testid="merge-source"
            class="tap-target rounded-lg border bg-[var(--surface)] px-3"
            style="border-color: var(--border)"
          >
            <option value="" disabled>{{ t('Choose who goes') }}</option>
            <option v-for="person in mergeSources" :key="person.id" :value="person.id">
              {{ labelFor(person) }}
            </option>
          </select>
        </label>

        <label class="flex flex-col gap-1">
          <span class="text-xs text-[var(--text-muted)]">{{ t('Into this person') }}</span>
          <select
            v-model="mergeTarget"
            data-testid="merge-target"
            class="tap-target rounded-lg border bg-[var(--surface)] px-3"
            style="border-color: var(--border)"
          >
            <option value="" disabled>{{ t('Choose who stays') }}</option>
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
        <p v-else class="text-xs text-[var(--text-muted)]">{{ t('Choose both. This cannot be undone.') }}
        </p>

        <p v-if="mergeError" class="text-sm text-owing" role="alert">{{ mergeError }}</p>

        <div class="flex gap-2">
          <button
            type="button"
            class="btn btn-press btn-secondary flex-1"
            style="border-color: var(--border)"
            @click="isMergeOpen = false"
          >{{ t('Cancel') }}
          </button>
          <button
            type="button"
            data-testid="merge-confirm-button"
            class="btn btn-press btn-danger flex-1"
            :disabled="!isMergeReady || isMerging"
            @click="confirmMerge"
          >
            {{ isMerging ? t('Merging') : t('Merge for good') }}
          </button>
        </div>
      </div>

      <PersonPicker
        :candidates="addable"
        :label="t('Add someone to this group')"
        @pick="addPerson"
      />
    </section>

    <section class="surface-card mb-4 p-4">
      <h2 class="mb-2 text-sm font-medium text-[var(--text-muted)]">{{ t('Invite someone') }}</h2>
      <p class="mb-3 text-xs text-[var(--text-muted)]">{{ t('They sign in with Google to join, so the link alone gives no access.') }}
      </p>

      <div class="flex gap-2">
        <input
          v-model="inviteEmail"
          type="email"
          :placeholder="t('Email, or leave blank for a link')"
          class="tap-target flex-1 rounded-lg border bg-[var(--surface)] px-3 text-sm"
          style="border-color: var(--border)"
        />
        <button
          type="button"
          class="btn btn-press btn-primary"
          @click="createInvite"
        >{{ t('Invite') }}
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
        >{{ t('Copy the invite link') }}
        </button>
      </div>
    </section>

    <section class="surface-card flex flex-col gap-3 p-4">
      <button
        v-if="!group?.isArchived"
        type="button"
        class="btn btn-press btn-secondary w-full justify-start"
        @click="archive"
      >{{ t('Archive this group') }}
      </button>
      <button v-else type="button" class="btn btn-press btn-secondary w-full justify-start" @click="unarchive">{{ t('Reopen this group') }}
      </button>
      <p class="text-xs text-[var(--text-muted)]">{{ t('Archiving freezes a group without deleting anything. Balances and history stay readable.') }}
      </p>
    </section>

    <IconPicker
      :open="isPickingIcon"
      :model-value="iconName"
      :title="t('Group icon')"
      @update:model-value="chooseIcon"
      @close="isPickingIcon = false"
    />

    <p v-if="message" class="mt-4 text-sm text-owed" role="status">{{ message }}</p>
    <p v-if="error" class="mt-4 text-sm text-owing" role="alert">{{ error }}</p>

    <!--
      One save for every setting on the screen, in the corner, and only once there
      is something to save. Fixed rather than in the flow: the settings it covers
      are spread down a long page, and a button that has scrolled away cannot be
      the answer to "I changed something".

      Clear of the tab bar and of the add button in the middle of it.
    -->
    <div
      v-if="hasChanges && canAdminister"
      data-testid="save-bar"
      class="fixed right-4 z-40 flex gap-2"
      style="bottom: calc(6rem + env(safe-area-inset-bottom))"
    >
      <button
        type="button"
        data-testid="cancel-changes"
        class="btn btn-press btn-secondary shadow-lg"
        style="border-color: var(--border)"
        :disabled="isSaving"
        @click="revert"
      >{{ t('Cancel') }}
      </button>
      <button
        type="button"
        data-testid="save-settings"
        class="btn btn-press btn-primary shadow-lg"
        :disabled="isSaving || splitProblem !== null"
        @click="save"
      >
        {{ isSaving ? t('Saving') : t('Save changes') }}
      </button>
    </div>
  </AppShell>
</template>
