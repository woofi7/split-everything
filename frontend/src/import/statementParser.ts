import Papa from 'papaparse'
import { parseAmountInput } from '@/domain/money'

/**
 * Bank and credit-card statement parsing, entirely in the browser.
 *
 * The privacy constraint from the spec drives the whole design: the statement file
 * never leaves the device. Nothing here uploads, and nothing here calls out. The
 * only thing that ever reaches the API is the list of expense records the user
 * confirmed in the review wizard.
 *
 * Statement layouts vary wildly, so every step is heuristic and every row carries
 * its problems for the user to correct. Automated table extraction from a PDF is
 * inherently unreliable; the manual column-mapping fallback is the real answer,
 * and these parsers exist to make the common cases quick rather than to be right
 * every time.
 */

export interface StatementRow {
  rowNumber: number
  date: Date | null
  description: string
  amount: number | null
  currency: string | null
  rawLine: string
  problems: string[]
}

export interface StatementMapping {
  date: number
  description: number
  amount: number
  debit?: number
  credit?: number
  currency?: number
}

export interface StatementParseResult {
  headers: string[]
  mapping: StatementMapping
  rows: StatementRow[]
  delimiter: string
}

const HEADER_ALIASES: Record<keyof StatementMapping, string[]> = {
  date: ['date', 'transaction date', 'posting date', 'datum', 'date de transaction'],
  description: ['description', 'merchant', 'details', 'narrative', 'payee', 'beschreibung', 'libelle'],
  amount: ['amount', 'montant', 'betrag', 'value'],
  debit: ['debit', 'withdrawal', 'charges', 'debit amount'],
  credit: ['credit', 'deposit', 'payments', 'credit amount'],
  currency: ['currency', 'ccy', 'devise', 'waehrung'],
}

export async function parseStatementCsv(text: string): Promise<StatementParseResult> {
  if (!text || !text.trim()) {
    throw new Error('That file is empty.')
  }

  const parsed = Papa.parse<string[]>(text.trim(), {
    skipEmptyLines: 'greedy',
    // PapaParse sniffs the delimiter, which handles the comma/semicolon split
    // between North American and European exports without asking the user.
    delimitersToGuess: [',', ';', '\t', '|'],
  })

  const table = parsed.data.filter((row) => row.some((cell) => cell && cell.trim()))
  if (table.length === 0) throw new Error('That file has no rows.')

  const headers = table[0].map((header) => header.trim())
  const mapping = guessMapping(headers)
  const dataRows = table.slice(1)

  const rows = dataRows.map((raw, index) => toStatementRow(raw, index + 1, mapping))

  return {
    headers,
    mapping,
    rows,
    delimiter: parsed.meta.delimiter ?? ',',
  }
}

function guessMapping(headers: string[]): StatementMapping {
  const find = (field: keyof StatementMapping): number => {
    const aliases = HEADER_ALIASES[field]

    for (let i = 0; i < headers.length; i++) {
      const header = headers[i].toLowerCase().trim()
      if (aliases.some((alias) => header === alias)) return i
    }
    for (let i = 0; i < headers.length; i++) {
      const header = headers[i].toLowerCase().trim()
      if (aliases.some((alias) => header.includes(alias))) return i
    }
    return -1
  }

  const debit = find('debit')
  const credit = find('credit')
  const amount = find('amount')

  return {
    date: find('date') >= 0 ? find('date') : 0,
    description: find('description') >= 0 ? find('description') : 1,
    // Some exports have no single amount column, only debit and credit.
    amount: amount >= 0 ? amount : debit >= 0 ? debit : 2,
    debit: debit >= 0 ? debit : undefined,
    credit: credit >= 0 ? credit : undefined,
    currency: find('currency') >= 0 ? find('currency') : undefined,
  }
}

function toStatementRow(raw: string[], rowNumber: number, mapping: StatementMapping): StatementRow {
  const problems: string[] = []
  const cell = (index?: number) =>
    index === undefined || index < 0 || index >= raw.length ? '' : (raw[index] ?? '').trim()

  const date = parseFlexibleDate(cell(mapping.date))
  if (!date) problems.push('the date could not be read')

  let amount: number | null = null

  if (mapping.debit !== undefined || mapping.credit !== undefined) {
    const debit = parseAmountInput(cell(mapping.debit))
    const credit = parseAmountInput(cell(mapping.credit))

    // A credit is money coming back, so it carries the opposite sign.
    if (debit !== null && debit !== 0) amount = Math.abs(debit)
    else if (credit !== null && credit !== 0) amount = -Math.abs(credit)
    else amount = parseAmountInput(cell(mapping.amount))
  } else {
    amount = parseAmountInput(cell(mapping.amount))
  }

  if (amount === null) problems.push('the amount could not be read')
  else if (amount === 0) problems.push('the amount is zero')

  const description = cell(mapping.description) || 'Statement transaction'

  const currencyCell = cell(mapping.currency).toUpperCase()
  const currency = /^[A-Z]{3}$/.test(currencyCell) ? currencyCell : null

  return {
    rowNumber,
    date,
    description,
    amount,
    currency,
    rawLine: raw.join(' '),
    problems,
  }
}

const MONTHS: Record<string, number> = {
  jan: 0, feb: 1, mar: 2, apr: 3, may: 4, jun: 5,
  jul: 6, aug: 7, sep: 8, oct: 9, nov: 10, dec: 11,
}

/**
 * Reads the date layouts real statements use. Returns null rather than guessing
 * wrong, so the row is flagged for the user instead of being filed under the
 * wrong month.
 */
export function parseFlexibleDate(value: string, fallbackYear?: number): Date | null {
  if (!value || !value.trim()) return null
  const trimmed = value.trim()

  const iso = /^(\d{4})[-/](\d{1,2})[-/](\d{1,2})/.exec(trimmed)
  if (iso) return utcDate(Number(iso[1]), Number(iso[2]) - 1, Number(iso[3]))

  const dotted = /^(\d{1,2})[.](\d{1,2})[.](\d{4})/.exec(trimmed)
  if (dotted) return utcDate(Number(dotted[3]), Number(dotted[2]) - 1, Number(dotted[1]))

  const slashed = /^(\d{1,2})\/(\d{1,2})\/(\d{4})/.exec(trimmed)
  if (slashed) {
    const first = Number(slashed[1])
    const second = Number(slashed[2])
    // Ambiguous by design: a value over 12 can only be the day.
    const [day, month] = first > 12 ? [first, second] : [second, first]
    return utcDate(Number(slashed[3]), month - 1, day)
  }

  const named = /^([A-Za-z]{3})[a-z]*\.?\s+(\d{1,2})(?:,?\s*(\d{4}))?/.exec(trimmed)
  if (named) {
    const month = MONTHS[named[1].toLowerCase()]
    if (month === undefined) return null
    const year = named[3] ? Number(named[3]) : fallbackYear
    if (year === undefined) return null
    return utcDate(year, month, Number(named[2]))
  }

  return null
}

function utcDate(year: number, month: number, day: number): Date | null {
  const date = new Date(Date.UTC(year, month, day, 12, 0, 0))
  return Number.isNaN(date.getTime()) ? null : date
}

/** Lines that are summary rows rather than transactions. */
const NON_TRANSACTION = [
  'balance', 'total', 'subtotal', 'statement', 'page ', 'account number',
  'minimum payment', 'credit limit', 'available credit', 'interest rate',
  'previous statement', 'amount due', 'payment due',
]

/**
 * Pulls transactions out of a PDF's extracted text.
 *
 * Line-oriented and deliberately conservative: a line needs a leading date and a
 * trailing amount to count. Summary lines are skipped by keyword, because reading
 * a closing balance as a purchase is the most damaging mistake this can make.
 */
export function extractTransactionsFromText(text: string, statementYear: number): StatementRow[] {
  if (!text || !text.trim()) return []

  interface Candidate {
    dateToken: string
    monthName?: string
    description: string
    amount: number
    rawLine: string
  }

  const candidates: Candidate[] = []

  for (const line of text.split(/\r?\n/)) {
    const trimmed = line.trim()
    if (!trimmed) continue

    const lower = trimmed.toLowerCase()
    if (NON_TRANSACTION.some((marker) => lower.includes(marker))) continue

    const amountMatch = /(-?\$?\s?\d[\d,. ]*\d|\d)(-?)\s*$/.exec(trimmed)
    if (!amountMatch) continue

    const amount = parseAmountInput(amountMatch[2] === '-' ? `${amountMatch[1]}-` : amountMatch[1])
    if (amount === null || amount === 0) continue

    const withoutAmount = trimmed.slice(0, amountMatch.index).trim()
    if (!withoutAmount) continue

    const dateMatch =
      /^(\d{4}[-/]\d{1,2}[-/]\d{1,2}|\d{1,2}[./]\d{1,2}[./]\d{4}|[A-Za-z]{3}[a-z]*\.?\s+\d{1,2}(?:,?\s*\d{4})?)/.exec(
        withoutAmount,
      )
    if (!dateMatch) continue

    const description = withoutAmount.slice(dateMatch[1].length).trim()
    if (!description) continue

    candidates.push({
      dateToken: dateMatch[1],
      monthName: /^([A-Za-z]{3})/.exec(dateMatch[1])?.[1]?.toLowerCase(),
      description,
      amount,
      rawLine: trimmed,
    })
  }

  // A statement that spans the new year prints month names without a year, in
  // chronological order. If January appears after December, those December lines
  // belong to the previous year - otherwise they would be filed eleven months
  // late. Deciding this needs the whole page, hence the second pass.
  const firstJanuary = candidates.findIndex((c) => c.monthName === 'jan')
  const lastDecember = candidates.map((c) => c.monthName).lastIndexOf('dec')
  const wrapsYear = firstJanuary >= 0 && lastDecember >= 0 && lastDecember < firstJanuary

  return candidates.map((candidate, index) => {
    const year =
      wrapsYear && candidate.monthName === 'dec' ? statementYear - 1 : statementYear
    const date = parseFlexibleDate(candidate.dateToken, year)

    return {
      rowNumber: index + 1,
      date,
      description: candidate.description,
      amount: candidate.amount,
      currency: null,
      rawLine: candidate.rawLine,
      problems: date ? [] : ['the date could not be read'],
    }
  })
}
