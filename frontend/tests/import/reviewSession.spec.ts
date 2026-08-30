import { beforeEach, describe, expect, it, vi } from 'vitest'
import { db, resetDatabase } from '@/offline/db'
import { StatementReviewSession } from '@/import/reviewSession'
import type { StatementRow } from '@/import/statementParser'

const groupId = 'group-1'
const memberId = 'member-1'

const row = (overrides: Partial<StatementRow> = {}): StatementRow => ({
  rowNumber: 1,
  date: new Date('2026-01-05T12:00:00Z'),
  description: 'UBER EATS TORONTO',
  amount: 42.5,
  currency: null,
  rawLine: 'Jan 05 UBER EATS TORONTO 42.50',
  problems: [],
  ...overrides,
})

const rules = [
  {
    id: 'rule-1',
    keyword: 'UBER EATS',
    suggestedGroupId: null,
    weight: 1,
    hitCount: 0,
    isEnabled: true,
    isBuiltIn: true,
  },
]

describe('statement review session', () => {
  beforeEach(async () => {
    await resetDatabase()
  })

  it('starts every row unassigned and not split', () => {
    const session = new StatementReviewSession([row()], { rules, suggestions: [], duplicates: [] })

    // Defaulting to "personal, not split" keeps the user in control: nothing is
    // silently charged to a group.
    expect(session.rows[0].groupId).toBeNull()
    expect(session.rows[0].action).toBe('personal')
  })

  it('flags a row the server already has', () => {
    const session = new StatementReviewSession([row()], {
      rules,
      suggestions: [],
      duplicates: [
        {
          fingerprint: 'fp-1',
          expenseId: 'expense-1',
          groupId,
          groupName: 'Roommates',
          description: 'Uber Eats',
          amount: 42.5,
          spentAt: '2026-01-05T12:00:00Z',
        },
      ],
    })
    session.setFingerprint(1, 'fp-1')

    expect(session.rows[0].isDuplicate).toBe(true)
    expect(session.rows[0].action).toBe('alreadyRecorded')
  })

  it('flags a row in a different currency for conversion', () => {
    const session = new StatementReviewSession([row({ currency: 'EUR' })], {
      rules,
      suggestions: [],
      duplicates: [],
      statementCurrency: 'CAD',
    })

    expect(session.rows[0].isForeignCurrency).toBe(true)
  })

  it('does not flag a row in the statement currency', () => {
    const session = new StatementReviewSession([row({ currency: 'CAD' })], {
      rules,
      suggestions: [],
      duplicates: [],
      statementCurrency: 'CAD',
    })

    expect(session.rows[0].isForeignCurrency).toBe(false)
  })

  it('lets the user assign a row to a group', () => {
    const session = new StatementReviewSession([row()], { rules, suggestions: [], duplicates: [] })

    session.assignGroup(1, groupId, memberId, [{ memberId, value: null }])

    expect(session.rows[0].groupId).toBe(groupId)
    expect(session.rows[0].action).toBe('split')
  })

  it('lets the user ignore a row', () => {
    const session = new StatementReviewSession([row()], { rules, suggestions: [], duplicates: [] })

    session.setAction(1, 'ignore')

    expect(session.rows[0].action).toBe('ignore')
  })

  it('applies a bulk action to a selection', () => {
    const rows = [row({ rowNumber: 1 }), row({ rowNumber: 2 }), row({ rowNumber: 3 })]
    const session = new StatementReviewSession(rows, { rules, suggestions: [], duplicates: [] })

    session.setActionForMany([1, 3], 'ignore')

    expect(session.rows.map((r) => r.action)).toEqual(['ignore', 'personal', 'ignore'])
  })

  it('commits only the rows assigned to a group', async () => {
    const rows = [row({ rowNumber: 1 }), row({ rowNumber: 2 }), row({ rowNumber: 3 })]
    const session = new StatementReviewSession(rows, { rules, suggestions: [], duplicates: [] })
    session.assignGroup(1, groupId, memberId, [{ memberId, value: null }])
    session.setAction(2, 'ignore')

    const payload = await session.buildCommitPayload()

    // Row 3 is still "personal": not split, so not the group's business.
    expect(payload.rows).toHaveLength(1)
    expect(payload.rows[0].groupId).toBe(groupId)
  })

  it('never puts the statement file or its raw text in the commit payload', async () => {
    const session = new StatementReviewSession([row()], { rules, suggestions: [], duplicates: [] })
    session.assignGroup(1, groupId, memberId, [{ memberId, value: null }])

    const serialised = JSON.stringify(await session.buildCommitPayload())

    // The whole point of parsing on the device: only confirmed records leave it.
    expect(serialised).not.toContain('Jan 05 UBER EATS TORONTO 42.50')
    expect(serialised).not.toContain('rawLine')
  })

  it('reports nothing to commit when every row was ignored', async () => {
    const session = new StatementReviewSession([row()], { rules, suggestions: [], duplicates: [] })
    session.setAction(1, 'ignore')

    expect((await session.buildCommitPayload()).rows).toHaveLength(0)
  })

  it('refuses to commit a row with no payer', async () => {
    const session = new StatementReviewSession([row()], { rules, suggestions: [], duplicates: [] })
    session.rows[0].groupId = groupId
    session.rows[0].action = 'split'
    session.rows[0].paidByMemberId = null

    await expect(session.buildCommitPayload()).rejects.toThrow(/payer/i)
  })

  it('computes a missing fingerprint itself, so dedupe cannot be skipped', async () => {
    const session = new StatementReviewSession([row()], { rules, suggestions: [], duplicates: [] })
    session.assignGroup(1, groupId, memberId, [{ memberId, value: null }])

    const payload = await session.buildCommitPayload()

    expect(payload.rows[0].fingerprint).toMatch(/^[0-9a-f]{32}$/)
  })

  it('counts what will happen, for the commit button', () => {
    const rows = [row({ rowNumber: 1 }), row({ rowNumber: 2 }), row({ rowNumber: 3 })]
    const session = new StatementReviewSession(rows, { rules, suggestions: [], duplicates: [] })
    session.assignGroup(1, groupId, memberId, [{ memberId, value: null }])
    session.setAction(2, 'ignore')

    const summary = session.summary()

    expect(summary.toCommit).toBe(1)
    expect(summary.ignored).toBe(1)
    expect(summary.personal).toBe(1)
  })

  it('clears the staged statement data when it is done', async () => {
    await db.meta.put({ key: 'statement:staging', value: 'raw statement text' })
    const session = new StatementReviewSession([row()], { rules, suggestions: [], duplicates: [] })

    await session.dispose()

    // Data hygiene from the spec: nothing about the statement survives the review.
    expect(session.rows).toHaveLength(0)
    expect(await db.meta.get('statement:staging')).toBeUndefined()
  })

  it('clears staged data even when the user cancels', async () => {
    await db.meta.put({ key: 'statement:staging', value: 'raw statement text' })
    const session = new StatementReviewSession([row()], { rules, suggestions: [], duplicates: [] })

    await session.cancel()

    expect(await db.meta.get('statement:staging')).toBeUndefined()
  })

  it('keeps the fingerprint on each committed row for server-side dedupe', async () => {
    const session = new StatementReviewSession([row()], { rules, suggestions: [], duplicates: [] })
    session.setFingerprint(1, 'fp-known')
    session.assignGroup(1, groupId, memberId, [{ memberId, value: null }])

    expect((await session.buildCommitPayload()).rows[0].fingerprint).toBe('fp-known')
  })
})
