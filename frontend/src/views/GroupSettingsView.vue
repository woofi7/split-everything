<script setup lang="ts">
import { t } from '@/i18n'
import { computed, onMounted, ref, watch } from 'vue'
import { RouterLink, useRoute, useRouter } from 'vue-router'
import { FontAwesomeIcon } from '@fortawesome/vue-fontawesome'
import { faCodeMerge, faXmark } from '@fortawesome/free-solid-svg-icons'
import AppShell from '@/components/layout/AppShell.vue'
import IconPicker from '@/components/ui/IconPicker.vue'
import PersonPicker from '@/components/groups/PersonPicker.vue'
import ColorChoice from '@/components/ui/ColorChoice.vue'
import AccentChoice from '@/components/ui/AccentChoice.vue'
import { resolveIcon } from '@/domain/icons'
import { groupColor } from '@/domain/themes'
import { useGroupsStore } from '@/stores/groups'
import { notify, report } from '@/ui/toasts'
import { useExpensesStore } from '@/stores/expenses'
import { compileNamePattern } from '@/domain/namePatterns'
import CategoryEditor from '@/components/groups/CategoryEditor.vue'
import CollapsibleSection from '@/components/ui/CollapsibleSection.vue'
import type { CategoryDraft } from '@/stores/groups'
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
const expenses = useExpensesStore()
const auth = useAuthStore()

const groupId = computed(() => String(route.params.groupId))

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

const activeMembers = computed(() =>
  (group.value?.members ?? []).filter((member) => member.status === 'Active'),
)

const splitNeedsValues = computed(() => splitType.value !== 'Equal')

const splitTotal = computed(() =>
  activeMembers.value.reduce((sum, member) => sum + (Number(splitValues.value[member.id]) || 0), 0),
)

const ignoredPatterns = ref<string[]>([])

const categories = computed(() => groups.categoriesOf(groupId.value))
const isSavingCategories = ref(false)

const hasOwnCategories = computed(() =>
  categories.value.length > 0 && groups.hasOwnCategories(groupId.value),
)

async function saveCategories(list: CategoryDraft[]): Promise<void> {
  isSavingCategories.value = true

  try {
    await groups.setCategories(groupId.value, list)
    notify(t('Saved.'), 'done')
  } catch (caught) {
    report(caught, t('Could not save those categories.'))
  } finally {
    isSavingCategories.value = false
  }
}

const patternMatches = computed(() =>
  ignoredPatterns.value.map((pattern) => {
    if (!pattern.trim()) return 0

    const matches = compileNamePattern(pattern)
    return expenses.forGroup(groupId.value).filter((expense) => matches(expense.description)).length
  }),
)

function addPattern(): void {
  ignoredPatterns.value.push('')
}

function removePattern(index: number): void {
  ignoredPatterns.value.splice(index, 1)
}

const splitProblem = computed(() => {
  if (!splitNeedsValues.value) return null
  if (splitTotal.value <= 0) return 'Give at least one person a number.'
  if (splitType.value === 'Percentage' && Math.abs(splitTotal.value - 100) > 0.01) {
    return `Percentages add up to ${splitTotal.value.toFixed(2)}, not 100.`
  }
  return null
})

function readSplitFromGroup(): void {
  const current = group.value
  splitType.value = current?.defaultSplitType ?? 'Equal'
  ignoredPatterns.value = [...(current?.ignoredNamePatterns ?? [])]

  const stored = current?.defaultSplitValues ?? {}
  const seeded: Record<string, number> = {}
  for (const member of activeMembers.value) {
    seeded[member.id] = stored[member.id] ?? (splitType.value === 'Percentage' ? 0 : 1)
  }
  splitValues.value = seeded
}

function changeSplitType(next: SplitType): void {
  splitType.value = next

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

const myMemberId = computed(() =>
  auth.user ? groups.myMemberId(groupId.value, auth.user.id) : null,
)
const name = ref('')
const iconName = ref<string | null>(null)
const themeName = ref('')
const isPickingIcon = ref(false)
const inviteEmail = ref('')
const newInvite = ref<InviteDto | null>(null)
const qrUrl = ref<string | null>(null)
const addable = ref<AddableUser[]>([])

const isMergeOpen = ref(false)
const mergeSource = ref('')
const mergeTarget = ref('')
const isMerging = ref(false)

const mergeSources = computed(() =>
  (group.value?.members ?? []).filter(
    (member) => member.role !== 'Owner' && member.id !== mergeTarget.value,
  ),
)

const mergeTargets = computed(() =>
  (group.value?.members ?? []).filter(
    (member) => member.status === 'Active' && member.id !== mergeSource.value,
  ),
)

const canAdminister = computed(() => {
  const mine = (group.value?.members ?? []).find((member) => member.id === myMemberId.value)
  return mine?.role === 'Owner' || mine?.role === 'Admin'
})

const canMerge = computed(
  () => canAdminister.value && mergeSources.value.length > 0 && mergeTargets.value.length > 0,
)

const isMergeReady = computed(
  () =>
    mergeSource.value !== '' &&
    mergeTarget.value !== '' &&
    mergeSource.value !== mergeTarget.value,
)

const nameOf = (memberId: string) =>
  (group.value?.members ?? []).find((member) => member.id === memberId)?.displayName ?? ''

const labelFor = (member: { displayName: string; status: string; isPlaceholder: boolean }) => {
  if (member.status !== 'Active') return `${member.displayName} (removed)`
  if (member.isPlaceholder) return `${member.displayName} (not signed in yet)`
  return member.displayName
}

const colouring = ref<string | null>(null)

const pendingColours = ref<Record<string, string>>({})

function canRecolour(memberId: string): boolean {
  return canAdminister.value || memberId === myMemberId.value
}

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

  if (stored && stored.toLowerCase() === colorHex.toLowerCase()) {
    delete staged[memberId]
    pendingColours.value = staged
    colouring.value = null
    return
  }

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
  mergeSource.value = ''
  mergeTarget.value = ''
  isMergeOpen.value = true
}

async function confirmMerge(): Promise<void> {
  if (!isMergeReady.value) return

  const goingName = nameOf(mergeSource.value)
  const stayingName = nameOf(mergeTarget.value)
  isMerging.value = true

  try {
    await groups.mergeMembers(groupId.value, mergeSource.value, mergeTarget.value)
    notify(t('{going} was merged into {staying}.', { going: goingName, staying: stayingName }), 'done')
    isMergeOpen.value = false
    await loadAddable()
  } catch (caught) {
    report(caught, t('Could not merge those two.'))
  } finally {
    isMerging.value = false
  }
}

onMounted(async () => {
  const loaded = await groups.get(groupId.value)
  name.value = loaded?.name ?? ''
  iconName.value = loaded?.iconName ?? null
  themeName.value = loaded?.themeName ?? ''
  readSplitFromGroup()

  await expenses.hydrate()

  await loadAddable()
})

const icon = computed(() => resolveIcon(iconName.value))

const group = computed(() => groups.groups.find((candidate) => candidate.id === groupId.value))

watch(group, readSplitFromGroup)

const isDirty = computed(() => {
  const current = group.value
  if (!current) return false

  if (name.value.trim() !== current.name) return true
  if ((iconName.value ?? null) !== (current.iconName ?? null)) return true
  if (themeName.value !== (current.themeName ?? '')) return true

  const storedType = current.defaultSplitType ?? 'Equal'
  if (splitType.value !== storedType) return true
  if (!splitNeedsValues.value) return false

  const stored = current.defaultSplitValues ?? {}
  return activeMembers.value.some(
    (member) => (stored[member.id] ?? null) !== (splitValues.value[member.id] ?? null),
  )
})

const cleanedPatterns = computed(() =>
  ignoredPatterns.value.map((pattern) => pattern.trim()).filter(Boolean),
)

const patternsDirty = computed(() => {
  const stored = group.value?.ignoredNamePatterns ?? []
  const patterns = cleanedPatterns.value

  return (
    patterns.length !== stored.length ||
    patterns.some((pattern, index) => pattern !== stored[index])
  )
})

const hasChanges = computed(() => {
  if (patternsDirty.value) return true
  if (Object.keys(pendingColours.value).length > 0) return true

  return canAdminister.value && isDirty.value
})

function revert(): void {
  const current = group.value
  name.value = current?.name ?? ''
  iconName.value = current?.iconName ?? null
  themeName.value = current?.themeName ?? ''
  pendingColours.value = {}
  readSplitFromGroup()
}

async function save(): Promise<void> {
  if (splitProblem.value) return

  isSaving.value = true

  try {
    if (patternsDirty.value) {
      await groups.setIgnoredNames(groupId.value, cleanedPatterns.value)
    }

    if (canAdminister.value && isDirty.value) {
      await groups.update(groupId.value, {
        name: name.value,
        iconName: iconName.value,
        themeName: themeName.value || null,
        defaultSplitType: splitType.value,
        defaultSplitValues: splitNeedsValues.value ? splitValues.value : null,
      })
    }

    for (const [memberId, colorHex] of Object.entries(pendingColours.value)) {
      await groups.setMemberColor(groupId.value, memberId, colorHex)
    }
    pendingColours.value = {}

    notify(t('Saved.'), 'done')
  } catch (caught) {
    report(caught, t('Could not save the group.'))
  } finally {
    isSaving.value = false
  }
}

function chooseIcon(next: string | null): void {
  iconName.value = next
  isPickingIcon.value = false
}

async function loadAddable(): Promise<void> {
  try {
    addable.value = await groups.addableUsers(groupId.value)
  } catch {
    addable.value = []
  }
}

async function addPerson(person: AddableUser): Promise<void> {
  try {
    await groups.addUserMember(groupId.value, person.id)
    await loadAddable()
  } catch (caught) {
    report(caught, t('Could not add that person.'))
  }
}

async function removeMember(memberId: string): Promise<void> {
  try {
    await groups.removeMember(groupId.value, memberId)
  } catch (caught) {
    report(caught, t('Could not remove that person.'))
  }
}

async function createInvite(): Promise<void> {
  qrUrl.value = null

  try {
    newInvite.value = await useApi().post<InviteDto>(`/groups/${groupId.value}/invites`, {
      email: inviteEmail.value.trim() || null,
      claimsMemberId: null,
      maxUses: 1,
      expiresInHours: 72,
    })

    const png = await useApi().blob(`/groups/invites/${newInvite.value.id}/qr`, { size: 8 })
    qrUrl.value = URL.createObjectURL(png)
  } catch (caught) {
    report(caught, t('Could not create an invite.'))
  }
}

async function copyInviteLink(): Promise<void> {
  if (!newInvite.value) return

  if (!navigator.clipboard) {
    notify(t('Copying needs a secure connection. The link above can be selected instead.'), 'error')
    return
  }

  try {
    await navigator.clipboard.writeText(newInvite.value.url)
    notify(t('Invite link copied.'), 'done')
  } catch {
    notify(t('Could not copy the link. It can be selected above instead.'), 'error')
  }
}

async function archive(): Promise<void> {
  try {
    await groups.archive(groupId.value)
    await router.replace({ name: 'dashboard' })
  } catch (caught) {
    report(caught, t('Could not archive the group.'))
  }
}

async function unarchive(): Promise<void> {
  try {
    await groups.unarchive(groupId.value)
  } catch (caught) {
    report(caught, t('Could not reopen the group.'))
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
            :style="{ backgroundColor: groupColor({ themeName, colorHex: group?.colorHex }) }"
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
      <div class="mt-1 flex flex-col gap-2">
        <span class="text-sm text-[var(--text-muted)]">{{ t('Group colour') }}</span>
        <p class="text-xs text-[var(--text-muted)]">{{ t('Set for everyone in the group. The whole app wears it while you are on this group.') }}
        </p>
        <AccentChoice
          :value="themeName"
          :label="t('Group colour')"
          @pick="themeName = $event"
        />
        <button
          v-if="themeName"
          type="button"
          data-testid="clear-group-colour"
          class="self-start text-xs text-accent"
          @click="themeName = ''"
        >{{ t('No colour of its own') }}
        </button>
      </div>
      <button type="submit" class="hidden" aria-hidden="true" tabindex="-1" />
    </form>
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
    <CollapsibleSection :title="t('Categories')" :count="categories.length" testid="categories">
      <CategoryEditor
        :categories="categories"
        :is-saving="isSavingCategories"
        :description="hasOwnCategories
          ? t('This group keeps its own list. Words are matched against what an expense is called, longest first, so uber eats beats uber.')
          : t('This group uses the list the server ships. Saving any change here takes a copy of it, and the group keeps that copy from then on.')"
        @save="saveCategories"
      />
      <RouterLink
        :to="{ name: 'file-expenses', params: { groupId } }"
        data-testid="file-expenses-link"
        class="btn btn-press btn-secondary mt-3 w-full"
        style="border-color: var(--border)"
      >{{ t('File existing expenses') }}
      </RouterLink>
    </CollapsibleSection>
    <CollapsibleSection
      :title="t('Leave out of the totals')"
      :count="ignoredPatterns.length"
      testid="ignored-names"
    >
      <p class="text-xs text-[var(--text-muted)]">{{ t('The rent is bigger than everything else every month, so it drowns out what the group actually spent. Names matching these are kept out of the month and group totals, and skipped when picking the biggest expense. Each total says how much it left out, so nothing goes missing.') }}
      </p>
      <p class="mt-1 text-xs text-[var(--text-muted)]">{{ t('Balances and who owes whom never change: the rent is still money somebody paid and somebody owes.') }}
      </p>
      <p class="mt-1 text-xs text-[var(--text-muted)]">{{ t('A name matches if it contains what you type. Use * for anything: Loyer* matches everything starting with Loyer.') }}
      </p>
      <div class="mt-3 flex flex-col gap-2">
        <div
          v-for="(pattern, index) in ignoredPatterns"
          :key="index"
          data-testid="pattern-row"
          class="flex flex-col gap-1"
        >
          <div class="flex items-center gap-2">
            <input
              v-model="ignoredPatterns[index]"
              type="text"
              data-testid="pattern-input"
              placeholder="Loyer"
              class="tap-target min-w-0 flex-1 rounded-lg border bg-[var(--surface-raised)] px-3 text-sm"
              style="border-color: var(--border)"
            />
            <button
              type="button"
              data-testid="remove-pattern"
              class="btn-press flex h-11 w-9 shrink-0 items-center justify-center rounded-lg border text-owing"
              style="border-color: var(--border)"
              :aria-label="t('Remove')"
              :title="t('Remove')"
              @click="removePattern(index)"
            >
              <FontAwesomeIcon :icon="faXmark" class="h-4 w-4" />
            </button>
          </div>
          <p
            v-if="pattern.trim()"
            data-testid="pattern-matches"
            class="text-xs text-[var(--text-muted)]"
          >
            {{ patternMatches[index] === 1
              ? t('Matches 1 expense here')
              : t('Matches {count} expenses here', { count: patternMatches[index] }) }}
          </p>
        </div>
        <button
          v-if="ignoredPatterns.length < 10"
          type="button"
          data-testid="add-pattern"
          class="btn btn-press btn-secondary min-h-0 self-start px-3 py-1.5 text-xs"
          style="border-color: var(--border)"
          @click="addPattern"
        >{{ t('Add a name to skip') }}
        </button>
      </div>
      <p class="mt-3 text-xs text-[var(--text-muted)]">{{ t('Anyone in the group can change this. It only decides what the totals say, and never what anybody owes.') }}
      </p>
    </CollapsibleSection>
    <section class="surface-card mb-4 p-4">
      <div class="mb-2 flex items-center justify-between gap-2">
        <h2 class="text-sm font-medium text-[var(--text-muted)]">{{ t('People') }}</h2>
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
            <span
              v-if="member.role === 'Owner'"
              data-testid="owner-tag"
              class="ml-1 rounded-full px-1.5 py-0.5 align-middle text-[0.65rem] font-semibold uppercase tracking-wide"
              style="background: color-mix(in oklab, var(--accent-text) 18%, transparent); color: var(--accent-text)"
            >{{ t('Owner') }}
            </span>
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
    <div
      v-if="hasChanges"
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
