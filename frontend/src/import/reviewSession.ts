import { db } from '@/offline/db'
import { computeFingerprint } from '@/domain/fingerprint'
import { categoriseRow, learnFromCorrection, type CategoryRule } from './categorisation'
import type { StatementRow } from './statementParser'
import type { SplitType } from '@/domain/splitting'

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
  categoryId: string | null
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
  categoryKey: string | null
  categoryId: string | null
  fingerprint: string | null
  isDuplicate: boolean
  duplicateOf: DuplicateMatch | null
  isForeignCurrency: boolean
  notes: string | null
}

export interface ReviewContext {
  rules: CategoryRule[]
  suggestions: SplitSuggestion[]
  duplicates: DuplicateMatch[]
  statementCurrency?: string
}

export interface CommitRow {
  groupId: string
  paidByMemberId: string
  description: string
  amount: number
  currency: string
  spentAt: string
  categoryId: string | null
  splitType: SplitType
  splits: SplitAssignment[]
  fingerprint: string
  notes: string | null
}

export interface CommitPayload {
  rows: CommitRow[]
  skipDuplicates: boolean
  sourceLabel: string | null
}

/** Keys under which parsing staged anything on-device during the session. */
const STAGING_KEYS = ['statement:staging', 'statement:ocr', 'statement:text']

/**
 * The review wizard's state.
 *
 * Two things it is careful about. First, nothing is charged to a group unless the
 * user says so: rows default to "personal, not split" and a suggestion only
 * pre-fills what the user can still change. Second, the commit payload carries
 * only confirmed expense records - never the raw line, the extracted text or the
 * file - and the staged parsing data is cleared whether the session commits or
 * is cancelled.
 */
export class StatementReviewSession {
  readonly rows: ReviewRow[]
  readonly learnedRules: CategoryRule[] = []

  private readonly context: ReviewContext
  private sourceLabel: string | null

  constructor(parsed: StatementRow[], context: ReviewContext, sourceLabel: string | null = null) {
    this.context = context
    this.sourceLabel = sourceLabel
    this.rows = parsed.map((row) => this.toReviewRow(row))
  }

  private toReviewRow(row: StatementRow): ReviewRow {
    const category = categoriseRow(row.description, this.context.rules)
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
      // A merchant previously split with a group is pre-filled; everything else
      // stays personal until the user decides.
      action: suggestion ? 'split' : 'personal',
      groupId: suggestion?.groupId ?? null,
      paidByMemberId: suggestion?.paidByMemberId ?? null,
      splitType: suggestion?.splitType ?? 'Equal',
      splits: suggestion ? [...suggestion.splits] : [],
      categoryKey: category?.categoryKey ?? null,
      categoryId: suggestion?.categoryId ?? category?.categoryId ?? null,
      fingerprint: null,
      isDuplicate: false,
      duplicateOf: null,
      isForeignCurrency,
      notes: null,
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

  /**
   * Attaches the fingerprint computed for a row and marks it as already recorded
   * when the server reported a match.
   */
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

  setCategoryForMany(rowNumbers: number[], categoryKey: string, categoryId = categoryKey): void {
    for (const rowNumber of rowNumbers) {
      const row = this.require(rowNumber)
      row.categoryKey = categoryKey
      row.categoryId = categoryId
    }
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
  }

  /**
   * Records a category correction and remembers it, so the next statement gets it
   * right without being asked again.
   */
  correctCategory(rowNumber: number, categoryKey: string, categoryId = categoryKey): void {
    const row = this.require(rowNumber)
    row.categoryKey = categoryKey
    row.categoryId = categoryId

    const learned = learnFromCorrection(
      row.description,
      categoryKey,
      [...this.context.rules, ...this.learnedRules],
      categoryId,
    )

    const existingIndex = this.learnedRules.findIndex((rule) => rule.id === learned.id)
    if (existingIndex >= 0) this.learnedRules[existingIndex] = learned
    else this.learnedRules.push(learned)
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

  /**
   * The only thing that ever goes to the server: confirmed expense records. The
   * raw line, extracted text and file are deliberately absent from this shape.
   */
  async buildCommitPayload(skipDuplicates = true): Promise<CommitPayload> {
    const rows: CommitRow[] = []

    for (const row of this.rows) {
      if (!this.isCommittable(row)) continue

      if (!row.paidByMemberId) {
        throw new Error(`Row ${row.rowNumber} has no payer selected.`)
      }

      // Computed here rather than required from the caller: the session already
      // holds every input, and a forgotten step would silently disable duplicate
      // detection on the server.
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
        categoryId: row.categoryId,
        splitType: row.splitType,
        splits: row.splits,
        fingerprint,
        notes: row.notes,
      })
    }

    return { rows, skipDuplicates, sourceLabel: this.sourceLabel }
  }

  /** Clears everything the parsing staged on-device. Safe to call twice. */
  async dispose(): Promise<void> {
    this.rows.length = 0
    this.learnedRules.length = 0
    this.sourceLabel = null
    await db.meta.bulkDelete(STAGING_KEYS)
  }

  /** Cancelling has to leave no trace either, not just a successful commit. */
  async cancel(): Promise<void> {
    await this.dispose()
  }
}
