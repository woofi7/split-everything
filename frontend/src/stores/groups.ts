import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { db, type LocalGroup, type LocalMember } from '@/offline/db'
import type { ApiClient } from '@/api/client'

interface GroupSummaryDto {
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

  function attachApi(client: ApiClient): void {
    api = client
  }

  function requireApi(): ApiClient {
    if (!api) throw new Error('The groups store has no API client attached.')
    return api
  }

  async function loadAll(): Promise<void> {
    isLoading.value = true

    // Cache first, so the screen has content before the network answers.
    groups.value = await db.groups.toArray()

    try {
      const summaries = await requireApi().get<GroupSummaryDto[]>('/groups', {
        includeArchived: true,
      })

      const merged = summaries.map((summary) => toLocalGroup(summary, groups.value))
      await db.groups.bulkPut(merged)
      groups.value = merged
      isOffline.value = false
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
    }>,
  ): Promise<LocalGroup> {
    // The API reads null as "not supplied" and an empty string as an explicit
    // clear, so removing an icon or a description has to send the empty string.
    const payload = { ...changes }
    if ('iconName' in payload && payload.iconName === null) payload.iconName = ''
    if ('description' in payload && payload.description === null) payload.description = ''

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

  async function addPlaceholderMember(groupId: string, displayName: string): Promise<void> {
    await requireApi().post(`/groups/${groupId}/members`, { displayName })
    await refresh(groupId)
  }

  async function removeMember(groupId: string, memberId: string): Promise<void> {
    await requireApi().delete(`/groups/${groupId}/members/${memberId}`)
    await refresh(groupId)
  }

  function membersOf(groupId: string): LocalMember[] {
    return groups.value.find((group) => group.id === groupId)?.members ?? []
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
    get,
    refresh,
    create,
    update,
    archive,
    unarchive,
    addPlaceholderMember,
    removeMember,
    membersOf,
    myMemberId,
  }
})

function toLocalGroup(dto: GroupSummaryDto, existing: LocalGroup[]): LocalGroup {
  // A summary carries no member list; keep whatever the detail read already gave
  // us rather than blanking the roster on every list refresh.
  const previous = existing.find((group) => group.id === dto.id)

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
    myNetBalance: dto.myNetBalance,
    totalSpend: dto.totalSpend ?? previous?.totalSpend ?? 0,
    expenseCount: dto.expenseCount ?? previous?.expenseCount ?? 0,
    updatedAt: dto.updatedAt ?? previous?.updatedAt ?? new Date().toISOString(),
  }
}
