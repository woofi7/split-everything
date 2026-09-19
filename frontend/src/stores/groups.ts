import { computed, ref, toRaw } from 'vue'
import { defineStore } from 'pinia'
import { memberColors } from '@/domain/memberColors'
import { db, type LocalGroup, type LocalMember } from '@/offline/db'
import { looksOffline, type ApiClient } from '@/api/client'
import type { AddableUser } from '@/api/types'
import type { SplitType } from '@/domain/splitting'
import type { Category } from '@/domain/categories'

export interface CategoryDraft {
  key?: string
  name: string
  iconName?: string
  colorHex?: string
  keywords?: string[]
}

const MAIN_GROUP_KEY = 'split-everything.main-group'

interface GroupSummaryDto {
  defaultSplitType?: SplitType
  defaultSplitValues?: Record<string, number> | null
  ignoredNamePatterns?: string[] | null
  id: string
  name: string
  baseCurrency: string
  iconName: string | null
  colorHex: string
  themeName?: string | null
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

export const useGroupsStore = defineStore('groups', () => {
  const groups = ref<LocalGroup[]>([])
  const includeArchived = ref(false)
  const isLoading = ref(false)

  const categoriesByGroup = ref<Record<string, Category[]>>({})

  const ownCategories = ref<Set<string>>(new Set())
  const isOffline = ref(false)
  let api: ApiClient | null = null

  const visibleGroups = computed(() =>
    groups.value
      .filter((group) => includeArchived.value || !group.isArchived)
      .slice()
      .sort((left, right) => {
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

  const mainGroupId = ref<string | null>(null)

  const mainGroup = computed(() =>
    groups.value.find((group) => group.id === mainGroupId.value),
  )

  function restoreMainGroup(): void {
    const stored = localStorage.getItem(MAIN_GROUP_KEY)
    if (stored) mainGroupId.value = stored
  }

  function setMainGroup(groupId: string): void {
    if (!groups.value.some((group) => group.id === groupId)) return

    mainGroupId.value = groupId
    localStorage.setItem(MAIN_GROUP_KEY, groupId)
  }

  function groupInCycle(step: 1 | -1): LocalGroup | undefined {
    const order = visibleGroups.value
    if (order.length < 2) return undefined

    const at = order.findIndex((group) => group.id === mainGroupId.value)
    if (at < 0) return step === 1 ? order[0] : order[order.length - 1]

    return order[(at + step + order.length) % order.length]
  }

  function cycleMainGroup(step: 1 | -1): string | null {
    const next = groupInCycle(step)
    if (!next) return null

    setMainGroup(next.id)
    return next.id
  }

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
      const [cachedCategories, cached] = await Promise.all([
        db.categories.toArray(),
        db.groups.toArray(),
      ])

      categoriesByGroup.value = Object.fromEntries(
        cachedCategories.map((row) => [row.groupId, row.categories]),
      )
      groups.value = cached
      settleMainGroup()

      const summaries = await requireApi().get<GroupSummaryDto[]>('/groups', {
        includeArchived: true,
      })

      const merged = summaries.map((summary) => toLocalGroup(summary, groups.value))
      await db.groups.bulkPut(merged)
      groups.value = merged
      isOffline.value = false
      settleMainGroup()

      const kept = new Set(merged.map((group) => group.id))
      const gone = cached.map((group) => group.id).filter((id) => !kept.has(id))
      if (gone.length > 0) await pruneVanished(gone)
    } catch (caught) {
      isOffline.value = looksOffline(caught)
    } finally {
      isLoading.value = false
    }
  }

  async function pruneVanished(gone: readonly string[]): Promise<void> {
    await db.groups.bulkDelete(gone as string[])
    await db.expenses.where('groupId').anyOf(gone).delete()
    await db.settlements.where('groupId').anyOf(gone).delete()
    await db.comments.where('groupId').anyOf(gone).delete()
    await db.activity.where('groupId').anyOf(gone).delete()
    await db.conflicts.where('groupId').anyOf(gone).delete()
  }

  async function get(groupId: string): Promise<LocalGroup | undefined> {
    const cached = await db.groups.get(groupId)
    if (cached) {
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

      void loadCategories(groupId)

      return local
    } catch (caught) {
      isOffline.value = looksOffline(caught)
      return db.groups.get(groupId)
    }
  }

  async function loadCategories(groupId: string): Promise<Category[] | undefined> {
    try {
      const categories = await requireApi().get<Category[]>(`/groups/${groupId}/categories`)
      await rememberCategories(groupId, categories)
      return categories
    } catch {
      return categoriesOf(groupId)
    }
  }

  async function setCategories(groupId: string, categories: CategoryDraft[]): Promise<Category[]> {
    const saved = await requireApi().put<Category[]>(`/groups/${groupId}/categories`, {
      categories,
    })

    await rememberCategories(groupId, saved, categories.length > 0)
    return saved
  }

  async function rememberCategories(
    groupId: string,
    categories: Category[],
    isOwn?: boolean,
  ): Promise<void> {
    if (!Array.isArray(categories)) return

    await db.categories.put({ groupId, categories })
    categoriesByGroup.value = { ...categoriesByGroup.value, [groupId]: categories }

    if (isOwn === undefined) return

    const own = new Set(ownCategories.value)
    if (isOwn) own.add(groupId)
    else own.delete(groupId)
    ownCategories.value = own
  }

  function categoriesOf(groupId: string): Category[] {
    return categoriesByGroup.value[groupId] ?? []
  }

  function hasOwnCategories(groupId: string): boolean {
    return ownCategories.value.has(groupId)
  }

  async function create(request: {
    name: string
    baseCurrency: string
    description?: string | null
    iconName?: string | null
    colorHex?: string | null
    themeName?: string | null
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
      themeName: string | null
      baseCurrency: string
      defaultSplitType: SplitType
      defaultSplitValues: Record<string, number> | null
      ignoredNamePatterns: string[]
    }>,
  ): Promise<LocalGroup> {
    const payload: Record<string, unknown> = { ...changes }
    if ('iconName' in payload && payload.iconName === null) payload.iconName = ''
    if ('description' in payload && payload.description === null) payload.description = ''
    if ('themeName' in payload && payload.themeName === null) payload.themeName = ''

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

  async function setIgnoredNames(groupId: string, patterns: string[]): Promise<void> {
    const dto = await requireApi().put<GroupSummaryDto>(`/groups/${groupId}/ignored-names`, {
      patterns: patterns.map((pattern) => pattern.trim()).filter(Boolean),
    })

    const local = toLocalGroup(dto, groups.value)
    await db.groups.put(local)
    upsert(local)
  }

  async function setDefaultSplit(
    groupId: string,
    splitType: SplitType,
    values: Record<string, number> | null,
  ): Promise<void> {
    const dto = await requireApi().patch<GroupSummaryDto>(`/groups/${groupId}`, {
      defaultSplitType: splitType,
      defaultSplitValues: splitType === 'Equal' ? {} : (values ?? {}),
    })

    const local = toLocalGroup(dto, groups.value)
    await db.groups.put(local)
    upsert(local)
  }

  async function addUserMember(groupId: string, userId: string): Promise<void> {
    await requireApi().post(`/groups/${groupId}/members/user`, { userId })
    await refresh(groupId)
  }

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

  async function addableUsers(groupId?: string): Promise<AddableUser[]> {
    const people = await requireApi().get<AddableUser[]>(
      '/users/addable',
      groupId ? { groupId } : undefined,
    )

    return Array.isArray(people) ? people : []
  }

  async function removeMember(groupId: string, memberId: string): Promise<void> {
    await requireApi().delete(`/groups/${groupId}/members/${memberId}`)
    await refresh(groupId)
  }

  function membersOf(groupId: string): LocalMember[] {
    return groups.value.find((group) => group.id === groupId)?.members ?? []
  }

  async function setMemberColor(
    groupId: string,
    memberId: string,
    colorHex: string,
  ): Promise<void> {
    await requireApi().patch(`/groups/${groupId}/members/${memberId}/color`, { colorHex })
    await refresh(groupId)
  }

  function colorsOf(groupId: string): Record<string, string> {
    const roster = membersOf(groupId)

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
    setIgnoredNames,
    loadCategories,
    setCategories,
    categoriesOf,
    categoriesByGroup,
    hasOwnCategories,
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
  const cached = existing.find((group) => group.id === dto.id)
  const previous = cached ? toRaw(cached) : undefined

  return {
    id: dto.id,
    name: dto.name,
    description: dto.description ?? previous?.description ?? null,
    baseCurrency: dto.baseCurrency,
    iconName: dto.iconName,
    colorHex: dto.colorHex,
    themeName: dto.themeName ?? null,
    isArchived: dto.isArchived,
    lineageId: dto.lineageId ?? previous?.lineageId ?? '',
    members: dto.members ?? previous?.members ?? [],
    memberCount: dto.memberCount ?? dto.members?.length ?? previous?.memberCount ?? 0,
    defaultSplitType: dto.defaultSplitType ?? previous?.defaultSplitType ?? 'Equal',
    defaultSplitValues: dto.defaultSplitValues ?? previous?.defaultSplitValues ?? null,
    ignoredNamePatterns: dto.ignoredNamePatterns ?? previous?.ignoredNamePatterns ?? null,
    myNetBalance: dto.myNetBalance,
    totalSpend: dto.totalSpend ?? previous?.totalSpend ?? 0,
    expenseCount: dto.expenseCount ?? previous?.expenseCount ?? 0,
    updatedAt: dto.updatedAt ?? previous?.updatedAt ?? new Date().toISOString(),
  }
}
