import { db } from '@/offline/db'
import { computeFingerprint } from '@/domain/fingerprint'
import type { StatementRow } from './statementParser'
import type { SplitType } from '@/domain/splitting'
import { categoriseByKeywords, type Category } from '@/domain/categories'

export type RowAction = 'personal' | 'split' | 'ignore' | 'alreadyRecorded'

export interface SplitAssignment {
  memberId: string
  value: number | null
}

export interface SplitSuggestion {
  normalizedMerchant: string
  groupId: string
  groupName: string
  splitType: SplitType
  splits: SplitAssignment[]
  paidByMemberId: string
  timesUsed: number
  lastUsedAt: string
}

export interface DuplicateMatch {
  fingerprint: string
  expenseId: string
  groupId: string
  groupName: string
  description: string
  amount: number
  spentAt: string
}

export interface ReviewRow {
  rowNumber: number
  date: Date | null
  description: string
  amount: number | null
  currency: string | null
  problems: string[]
  action: RowAction
  groupId: string | null
  paidByMemberId: string | null
  splitType: SplitType
  splits: SplitAssignment[]
  fingerprint: string | null
  isDuplicate: boolean
  duplicateOf: DuplicateMatch | null
  isForeignCurrency: boolean
  notes: string | null
  categoryKey: string | null
  categoryChosen: boolean
}

export interface ReviewContext {
  suggestions: SplitSuggestion[]
  duplicates: DuplicateMatch[]
  statementCurrency?: string
  categoriesByGroup?: Record<string, Category[]>
}

export interface CommitRow {
  groupId: string
  paidByMemberId: string
  description: string
  amount: number
  currency: string
  spentAt: string
  splitType: SplitType
  splits: SplitAssignment[]
  fingerprint: string
  notes: string | null
  categoryKey: string | null
}

export interface CommitPayload {
  rows: CommitRow[]
  skipDuplicates: boolean
  sourceLabel: string | null
}

const STAGING_KEYS = ['statement:staging', 'statement:ocr', 'statement:text']

export class StatementReviewSession {
  readonly rows: ReviewRow[]

  private readonly context: ReviewContext
  private sourceLabel: string | null

  constructor(parsed: StatementRow[], context: ReviewContext, sourceLabel: string | null = null) {
    this.context = context
    this.sourceLabel = sourceLabel
    this.rows = parsed.map((row) => this.toReviewRow(row))
  }

  private toReviewRow(row: StatementRow): ReviewRow {
    const suggestion = this.findSuggestion(row.description)

    const statementCurrency = this.context.statementCurrency
    const isForeignCurrency = Boolean(
      row.currency && statementCurrency && row.currency !== statementCurrency,
    )

    return {
      rowNumber: row.rowNumber,
      date: row.date,
      description: row.description,
      amount: row.amount,
      currency: row.currency,
      problems: [...row.problems],
      action: suggestion ? 'split' : 'personal',
      groupId: suggestion?.groupId ?? null,
      paidByMemberId: suggestion?.paidByMemberId ?? null,
      splitType: suggestion?.splitType ?? 'Equal',
      splits: suggestion ? [...suggestion.splits] : [],
      fingerprint: null,
      isDuplicate: false,
      duplicateOf: null,
      isForeignCurrency,
      notes: null,
      categoryKey: suggestion
        ? categoriseByKeywords(
            row.description,
            this.context.categoriesByGroup?.[suggestion.groupId] ?? [],
          )
        : null,
      categoryChosen: false,
    }
  }

  private findSuggestion(description: string): SplitSuggestion | undefined {
    const upper = description.toUpperCase()
    return this.context.suggestions.find((suggestion) =>
      upper.includes(suggestion.normalizedMerchant.toUpperCase()),
    )
  }

  private require(rowNumber: number): ReviewRow {
    const row = this.rows.find((candidate) => candidate.rowNumber === rowNumber)
    if (!row) throw new Error(`No row ${rowNumber} in this review.`)
    return row
  }

  setFingerprint(rowNumber: number, fingerprint: string): void {
    const row = this.require(rowNumber)
    row.fingerprint = fingerprint

    const match = this.context.duplicates.find((d) => d.fingerprint === fingerprint)
    if (match) {
      row.isDuplicate = true
      row.duplicateOf = match
      row.action = 'alreadyRecorded'
    }
  }

  setAction(rowNumber: number, action: RowAction): void {
    this.require(rowNumber).action = action
  }

  setActionForMany(rowNumbers: number[], action: RowAction): void {
    for (const rowNumber of rowNumbers) this.setAction(rowNumber, action)
  }

  assignGroup(
    rowNumber: number,
    groupId: string,
    paidByMemberId: string,
    splits: SplitAssignment[],
    splitType: SplitType = 'Equal',
  ): void {
    const row = this.require(rowNumber)
    row.groupId = groupId
    row.paidByMemberId = paidByMemberId
    row.splits = splits
    row.splitType = splitType
    row.action = 'split'

    if (!row.categoryChosen) {
      row.categoryKey = categoriseByKeywords(
        row.description,
        this.context.categoriesByGroup?.[groupId] ?? [],
      )
    }
  }

  setCategory(rowNumber: number, categoryKey: string | null): void {
    const row = this.require(rowNumber)
    row.categoryKey = categoryKey
    row.categoryChosen = true
  }

  summary(): { toCommit: number; ignored: number; personal: number; duplicates: number; problems: number } {
    return {
      toCommit: this.rows.filter((row) => this.isCommittable(row)).length,
      ignored: this.rows.filter((row) => row.action === 'ignore').length,
      personal: this.rows.filter((row) => row.action === 'personal').length,
      duplicates: this.rows.filter((row) => row.isDuplicate).length,
      problems: this.rows.filter((row) => row.problems.length > 0).length,
    }
  }

  private isCommittable(row: ReviewRow): boolean {
    return (
      row.action === 'split' &&
      row.groupId !== null &&
      row.amount !== null &&
      row.date !== null &&
      row.problems.length === 0
    )
  }

  async buildCommitPayload(skipDuplicates = true): Promise<CommitPayload> {
    const rows: CommitRow[] = []

    for (const row of this.rows) {
      if (!this.isCommittable(row)) continue

      if (!row.paidByMemberId) {
        throw new Error(`Row ${row.rowNumber} has no payer selected.`)
      }

      const currency = row.currency ?? this.context.statementCurrency ?? 'CAD'
      const fingerprint =
        row.fingerprint ??
        (await computeFingerprint(row.date!, row.amount!, currency, row.description))

      rows.push({
        groupId: row.groupId!,
        paidByMemberId: row.paidByMemberId,
        description: row.description,
        amount: Math.abs(row.amount!),
        currency,
        spentAt: row.date!.toISOString(),
        splitType: row.splitType,
        splits: row.splits,
        fingerprint,
        notes: row.notes,
        categoryKey: row.categoryKey,
      })
    }

    return { rows, skipDuplicates, sourceLabel: this.sourceLabel }
  }

  async dispose(): Promise<void> {
    this.rows.length = 0
    this.sourceLabel = null
    await db.meta.bulkDelete(STAGING_KEYS)
  }

  async cancel(): Promise<void> {
    await this.dispose()
  }
}
