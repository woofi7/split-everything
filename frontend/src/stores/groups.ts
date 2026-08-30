import { computed, ref, toRaw } from 'vue'
import { defineStore } from 'pinia'
import { memberColors } from '@/domain/memberColors'
import { db, type LocalGroup, type LocalMember } from '@/offline/db'
import type { ApiClient } from '@/api/client'
import type { AddableUser } from '@/api/types'
import type { SplitType } from '@/domain/splitting'

const MAIN_GROUP_KEY = 'split-everything.main-group'

interface GroupSummaryDto {
  defaultSplitType?: SplitType
  defaultSplitValues?: Record<string, number> | null
  id: string
  name: string
  baseCurrency: string
  iconName: string | null
  colorHex: string
  isArchived: boolean
  myNetBalance: number
  memberCount?: number
  lastActivityAt?: string | null
  members?: LocalMember[]
  description?: string | null
  lineageId?: string
  totalSpend?: number
  expenseCount?: number
  updatedAt?: string
}

/**
 * Groups, backed by the local replica.
 *
 * Reads hydrate from IndexedDB first and then refresh from the server, so the list
 * renders instantly on launch and keeps working with no connection. A failed
 * refresh is not an error the user sees: it sets `isOffline` and leaves the cached
 * data in place.
 */
export const useGroupsStore = defineStore('groups', () => {
  const groups = ref<LocalGroup[]>([])
  const includeArchived = ref(false)
  const isLoading = ref(false)
  const isOffline = ref(false)
  let api: ApiClient | null = null

  const visibleGroups = computed(() =>
    groups.value
      .filter((group) => includeArchived.value || !group.isArchived)
      .slice()
      .sort((left, right) => {
        // Anything outstanding first: a settled group needs no attention.
        const leftOutstanding = Math.abs(left.myNetBalance) > 0.005 ? 0 : 1
        const rightOutstanding = Math.abs(right.myNetBalance) > 0.005 ? 0 : 1
        if (leftOutstanding !== rightOutstanding) return leftOutstanding - rightOutstanding

        if (left.isArchived !== right.isArchived) return left.isArchived ? 1 : -1
        return left.name.localeCompare(right.name)
      }),
  )

  const netAcrossGroups = computed(() =>
    Number(
      groups.value
        .filter((group) => !group.isArchived)
        .reduce((sum, group) => sum + group.myNetBalance, 0)
        .toFixed(2),
    ),
  )

  /**
   * The group the app is about, and the default every screen falls back to.
   *
   * A device preference rather than account state: which group you are looking at
   * is about the screen in your hand, and it has to survive a reload without
   * waiting on the network.
   */
  const mainGroupId = ref<string | null>(null)

  const mainGroup = computed(() =>
    groups.value.find((group) => group.id === mainGroupId.value),
  )

  function restoreMainGroup(): void {
    const stored = localStorage.getItem(MAIN_GROUP_KEY)
    if (stored) mainGroupId.value = stored
  }

  function setMainGroup(groupId: string): void {
    // Ignored rather than trusted: pointing every screen at a group we do not have
    // would empty all of them at once.
    if (!groups.value.some((group) => group.id === groupId)) return

    mainGroupId.value = groupId
    localStorage.setItem(MAIN_GROUP_KEY, groupId)
  }

  /**
   * The group one place before or after this one, without moving to it.
   *
   * In the order the groups are listed, so the cycle matches the picker rather
   * than being an order of its own that nothing on screen shows.
   *
   * Wraps around: with three groups, three steps the same way come back to where
   * they started, which is what makes swiping usable without counting. Nothing
   * with fewer than two, and a main group that is not in the list (an archived
   * one, while archived groups are hidden) steps in from the end asked for.
   *
   * Answered separately from moving because a swipe shows the group it is bringing
   * in while the finger is still down.
   */
  function groupInCycle(step: 1 | -1): LocalGroup | undefined {
    const order = visibleGroups.value
    if (order.length < 2) return undefined

    const at = order.findIndex((group) => group.id === mainGroupId.value)
    if (at < 0) return step === 1 ? order[0] : order[order.length - 1]

    return order[(at + step + order.length) % order.length]
  }

  /** Steps to that group, and answers where it landed. */
  function cycleMainGroup(step: 1 | -1): string | null {
    const next = groupInCycle(step)
    if (!next) return null

    setMainGroup(next.id)
    return next.id
  }

  /**
   * Keeps the choice pointing at something real.
   *
   * Called after every list load, because a group can be archived, left or deleted
   * on another device, and a main group that no longer exists shows as an empty
   * app rather than as an error.
   */
  function settleMainGroup(): void {
    const candidates = groups.value.filter((group) => !group.isArchived)

    if (mainGroupId.value && candidates.some((group) => group.id === mainGroupId.value)) return

    const next = candidates[0]?.id ?? null
    mainGroupId.value = next

    if (next) localStorage.setItem(MAIN_GROUP_KEY, next)
    else localStorage.removeItem(MAIN_GROUP_KEY)
  }

  function attachApi(client: ApiClient): void {
    api = client
  }

  function requireApi(): ApiClient {
    if (!api) throw new Error('The groups store has no API client attached.')
    return api
  }

  async function loadAll(): Promise<void> {
    isLoading.value = true

    try {
      // Cache first, so the screen has content before the network answers. Inside
      // the try with everything else: it was outside, so a replica that failed to
      // answer left "Loading your groups" on screen for good.
      groups.value = await db.groups.toArray()
      settleMainGroup()

      const summaries = await requireApi().get<GroupSummaryDto[]>('/groups', {
        includeArchived: true,
      })

      const merged = summaries.map((summary) => toLocalGroup(summary, groups.value))
      await db.groups.bulkPut(merged)
      groups.value = merged
      isOffline.value = false
      settleMainGroup()
    } catch {
      // Keep the cached list: an unreachable server is a normal state here.
      isOffline.value = true
    } finally {
      isLoading.value = false
    }
  }

  async function get(groupId: string): Promise<LocalGroup | undefined> {
    const cached = await db.groups.get(groupId)
    if (cached) {
      // Refresh in the background, so opening a group is never a spinner.
      void refresh(groupId)
      return cached
    }

    return refresh(groupId)
  }

  async function refresh(groupId: string): Promise<LocalGroup | undefined> {
    try {
      const dto = await requireApi().get<GroupSummaryDto>(`/groups/${groupId}`)
      const local = toLocalGroup(dto, groups.value)

      await db.groups.put(local)
      upsert(local)
      isOffline.value = false
      return local
    } catch {
      isOffline.value = true
      return db.groups.get(groupId)
    }
  }

  async function create(request: {
    name: string
    baseCurrency: string
    description?: string | null
    iconName?: string | null
    colorHex?: string | null
    placeholderMemberNames?: string[]
  }): Promise<LocalGroup> {
    const dto = await requireApi().post<GroupSummaryDto>('/groups', request)
    const local = toLocalGroup(dto, groups.value)

    await db.groups.put(local)
    upsert(local)
    return local
  }

  async function update(
    groupId: string,
    changes: Partial<{
      name: string
      description: string | null
      iconName: string | null
      colorHex: string | null
      baseCurrency: string
      // The group's own fields and how it splits are the same PATCH, so a screen
      // that edits both can save both in one request rather than half-succeeding.
      defaultSplitType: SplitType
      defaultSplitValues: Record<string, number> | null
    }>,
  ): Promise<LocalGroup> {
    // The API reads null as "not supplied" and an empty string as an explicit
    // clear, so removing an icon or a description has to send the empty string.
    const payload: Record<string, unknown> = { ...changes }
    if ('iconName' in payload && payload.iconName === null) payload.iconName = ''
    if ('description' in payload && payload.description === null) payload.description = ''

    // An empty map is the explicit clear, matching the server's convention, and
    // equal needs no values at all.
    if ('defaultSplitType' in changes) {
      payload.defaultSplitValues =
        changes.defaultSplitType === 'Equal' ? {} : (changes.defaultSplitValues ?? {})
    }

    const dto = await requireApi().patch<GroupSummaryDto>(`/groups/${groupId}`, payload)
    const local = toLocalGroup(dto, groups.value)

    await db.groups.put(local)
    upsert(local)
    return local
  }

  async function archive(groupId: string): Promise<void> {
    const dto = await requireApi().post<GroupSummaryDto>(`/groups/${groupId}/archive`)
    const local = toLocalGroup(dto, groups.value)

    await db.groups.put(local)
    upsert(local)
  }

  async function unarchive(groupId: string): Promise<void> {
    const dto = await requireApi().post<GroupSummaryDto>(`/groups/${groupId}/unarchive`)
    const local = toLocalGroup(dto, groups.value)

    await db.groups.put(local)
    upsert(local)
  }

  /**
   * Records how this group splits by default, from what someone just used.
   *
   * An admin-only change on the server, because it decides what everyone else's
   * next expense does.
   */
  async function setDefaultSplit(
    groupId: string,
    splitType: SplitType,
    values: Record<string, number> | null,
  ): Promise<void> {
    const dto = await requireApi().patch<GroupSummaryDto>(`/groups/${groupId}`, {
      defaultSplitType: splitType,
      // An empty map is the explicit clear, matching the server's convention.
      defaultSplitValues: splitType === 'Equal' ? {} : (values ?? {}),
    })

    const local = toLocalGroup(dto, groups.value)
    await db.groups.put(local)
    upsert(local)
  }

  /**
   * Adds someone to the group.
   *
   * By account, and only by account. A member with no account behind them could
   * never open the group, see what they owed, or be told about it, so the only
   * other way in is an invite link they accept themselves.
   */
  async function addUserMember(groupId: string, userId: string): Promise<void> {
    await requireApi().post(`/groups/${groupId}/members/user`, { userId })
    await refresh(groupId)
  }

  /**
   * Folds one member into another, and refreshes the group over the answer.
   *
   * Everything the source paid, owed, was owed and said becomes the target's, and
   * the source is removed. There is no undo: nothing records which rows moved, so
   * nothing can move them back.
   */
  async function mergeMembers(
    groupId: string,
    sourceMemberId: string,
    targetMemberId: string,
  ): Promise<void> {
    const dto = await requireApi().post<GroupSummaryDto>(`/groups/${groupId}/members/merge`, {
      sourceMemberId,
      targetMemberId,
    })

    const local = toLocalGroup(dto, groups.value)
    await db.groups.put(local)
    upsert(local)
  }

  /**
   * People with an account who are not in this group yet.
   *
   * Read on demand rather than cached: the point of the list is to be current,
   * and it changes whenever anyone else signs up or joins.
   */
  async function addableUsers(groupId?: string): Promise<AddableUser[]> {
    const people = await requireApi().get<AddableUser[]>(
      '/users/addable',
      groupId ? { groupId } : undefined,
    )

    // This list is a convenience on top of a field that works without it, so
    // anything unexpected becomes "nobody" rather than breaking the form.
    return Array.isArray(people) ? people : []
  }

  async function removeMember(groupId: string, memberId: string): Promise<void> {
    await requireApi().delete(`/groups/${groupId}/members/${memberId}`)
    await refresh(groupId)
  }

  function membersOf(groupId: string): LocalMember[] {
    return groups.value.find((group) => group.id === groupId)?.members ?? []
  }

  /**
   * Sets one member's colour in one group.
   *
   * The server may swap two people rather than refuse a colour that is taken, so
   * the whole group is read back rather than the one member patched in place.
   */
  async function setMemberColor(
    groupId: string,
    memberId: string,
    colorHex: string,
  ): Promise<void> {
    await requireApi().patch(`/groups/${groupId}/members/${memberId}/color`, { colorHex })
    await refresh(groupId)
  }

  /**
   * The colour of every member of a group.
   *
   * Defined once, from the roster, because the palette resolves a clash by walking
   * to the next free colour in the order it is given: hand it a different set of
   * people, or the same people in a different order, and the same person comes out
   * a different colour. Every screen was building its own list, so the activity
   * feed and the charts disagreed with the expense cards about who was orange.
   */
  function colorsOf(groupId: string): Record<string, string> {
    const roster = membersOf(groupId)

    // Derived first, for anyone the group has not given a colour to: rows written
    // before the group stored them, which is every group until it is next read
    // from the server.
    const colours = memberColors(roster.map((member) => member.id))

    for (const member of roster) {
      if (member.colorHex) colours[member.id] = member.colorHex
    }

    return colours
  }

  function myMemberId(groupId: string, userId: string): string | null {
    return membersOf(groupId).find((member) => member.userId === userId)?.id ?? null
  }

  function upsert(group: LocalGroup): void {
    const index = groups.value.findIndex((candidate) => candidate.id === group.id)
    if (index >= 0) groups.value[index] = group
    else groups.value.push(group)
  }

  return {
    groups,
    visibleGroups,
    netAcrossGroups,
    includeArchived,
    isLoading,
    isOffline,
    attachApi,
    loadAll,
    mainGroupId,
    mainGroup,
    restoreMainGroup,
    setMainGroup,
    groupInCycle,
    cycleMainGroup,
    get,
    refresh,
    create,
    update,
    archive,
    unarchive,
    setDefaultSplit,
    setMemberColor,
    addUserMember,
    mergeMembers,
    addableUsers,
    removeMember,
    membersOf,
    colorsOf,
    myMemberId,
  }
})

function toLocalGroup(dto: GroupSummaryDto, existing: LocalGroup[]): LocalGroup {
  // A summary carries no member list; keep whatever the detail read already gave
  // us rather than blanking the roster on every list refresh.
  //
  // Unwrapped, because the cached copy is read back out of a reactive ref and the
  // result of this goes straight into IndexedDB. A reactive value is a Proxy, and
  // a Proxy cannot be structure-cloned: the write fails with DataCloneError, which
  // the caller cannot tell apart from an unreachable server.
  const cached = existing.find((group) => group.id === dto.id)
  const previous = cached ? toRaw(cached) : undefined

  return {
    id: dto.id,
    name: dto.name,
    description: dto.description ?? previous?.description ?? null,
    baseCurrency: dto.baseCurrency,
    iconName: dto.iconName,
    colorHex: dto.colorHex,
    isArchived: dto.isArchived,
    lineageId: dto.lineageId ?? previous?.lineageId ?? '',
    members: dto.members ?? previous?.members ?? [],
    // A detail read has the roster but no count; a summary has the count but no
    // roster. Either one can answer "how many people".
    memberCount: dto.memberCount ?? dto.members?.length ?? previous?.memberCount ?? 0,
    // A summary carries neither, so the cached copy holds them until a detail read.
    defaultSplitType: dto.defaultSplitType ?? previous?.defaultSplitType ?? 'Equal',
    defaultSplitValues: dto.defaultSplitValues ?? previous?.defaultSplitValues ?? null,
    myNetBalance: dto.myNetBalance,
    totalSpend: dto.totalSpend ?? previous?.totalSpend ?? 0,
    expenseCount: dto.expenseCount ?? previous?.expenseCount ?? 0,
    updatedAt: dto.updatedAt ?? previous?.updatedAt ?? new Date().toISOString(),
  }
}
