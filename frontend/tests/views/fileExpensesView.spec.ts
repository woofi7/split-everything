import { describe, expect, it, vi } from 'vitest'
import { RouterLinkStub } from '@vue/test-utils'
import FileExpensesView from '@/views/FileExpensesView.vue'
import {
  GROUP_ID,
  fakeApi,
  mountView,
  saidOnScreen,
  settle,
  testExpense,
  testGroup,
  textOf,
  waitFor,
} from '../support/viewHarness'
import { db } from '@/offline/db'
import type { Category } from '@/domain/categories'
import type { LocalExpense } from '@/offline/db'

vi.mock('vue-router', () => ({
  useRoute: () => ({ params: { groupId: GROUP_ID }, query: {} }),
  useRouter: () => ({ push: vi.fn(), replace: vi.fn() }),
  RouterLink: RouterLinkStub,
}))

const categories: Category[] = [
  {
    key: 'groceries',
    name: 'Groceries',
    iconName: 'cart-shopping',
    colorHex: '#16a34a',
    sortOrder: 10,
    keywords: ['metro', 'iga'],
  },
  {
    key: 'dining',
    name: 'Dining out',
    iconName: 'utensils',
    colorHex: '#f97316',
    sortOrder: 20,
    keywords: ['resto'],
  },
]

async function mountScreen(expenses: Partial<LocalExpense>[], list = categories) {
  return mountView(FileExpensesView, {
    api: fakeApi({
      '/groups/group-1/categories': () => list,
      '/groups': () => testGroup(),
    }),
    expenses: expenses.map((overrides) => testExpense(overrides)),
  })
}

const rowsOn = (wrapper: { findAll: (selector: string) => unknown[] }) =>
  wrapper.findAll('[data-testid="filing-row"]')

describe('filing expenses in bulk', () => {
  it('starts on the ones nobody has filed', async () => {
    const { wrapper } = await mountScreen([
      { id: 'a', description: 'Metro' },
      { id: 'b', description: 'Resto', categoryKey: 'dining' },
    ])

    expect(rowsOn(wrapper)).toHaveLength(1)
    expect(textOf(wrapper)).toContain('Metro')
    expect(textOf(wrapper)).not.toContain('Resto')
  })

  it('counts a category the list no longer has as unfiled', async () => {
    const { wrapper } = await mountScreen([{ id: 'a', description: 'Metro', categoryKey: 'gone' }])

    expect(rowsOn(wrapper)).toHaveLength(1)
    expect(wrapper.find('[data-testid="row-filed-as"]').text()).toBe('Not filed')
  })

  it('shows everything when asked', async () => {
    const { wrapper } = await mountScreen([
      { id: 'a', description: 'Metro' },
      { id: 'b', description: 'Resto', categoryKey: 'dining' },
    ])

    await wrapper.find('[data-testid="filing-filter"]').setValue('*')
    await settle()

    expect(rowsOn(wrapper)).toHaveLength(2)
  })

  it('narrows to one category, for moving what was filed wrong', async () => {
    const { wrapper } = await mountScreen([
      { id: 'a', description: 'Metro', categoryKey: 'dining' },
      { id: 'b', description: 'Resto', categoryKey: 'dining' },
      { id: 'c', description: 'Bus' },
    ])

    await wrapper.find('[data-testid="filing-filter"]').setValue('dining')
    await settle()

    expect(rowsOn(wrapper)).toHaveLength(2)
    expect(textOf(wrapper)).not.toContain('Bus')
  })

  it('searches by name, accents and case aside', async () => {
    const { wrapper } = await mountScreen([
      { id: 'a', description: 'Épicerie du coin' },
      { id: 'b', description: 'Bus pass' },
    ])

    await wrapper.find('[data-testid="filing-search"]').setValue('EPICERIE')
    await settle()

    expect(rowsOn(wrapper)).toHaveLength(1)
    expect(textOf(wrapper)).toContain('Épicerie du coin')
  })

  it('says nothing matches when the search finds none', async () => {
    const { wrapper } = await mountScreen([{ id: 'a', description: 'Metro' }])

    await wrapper.find('[data-testid="filing-search"]').setValue('zzz')
    await settle()

    expect(textOf(wrapper)).toContain('Nothing matches that.')
  })

  it('says so when there is no backlog at all', async () => {
    const { wrapper } = await mountScreen([{ id: 'a', categoryKey: 'dining' }])

    expect(textOf(wrapper)).toContain('Everything here is filed.')
  })

  describe('filing the ticked ones', () => {
    it('offers nothing until something is ticked', async () => {
      const { wrapper } = await mountScreen([{ id: 'a', description: 'Metro' }])

      expect(wrapper.find('[data-testid="filing-bar"]').exists()).toBe(false)
    })

    it('files them all under the one category', async () => {
      const { wrapper } = await mountScreen([
        { id: 'a', description: 'Metro' },
        { id: 'b', description: 'IGA' },
      ])

      await wrapper.find('[data-testid="select-all"]').trigger('click')
      await settle()
      await wrapper.find('[data-testid="filing-target"]').setValue('groceries')
      await wrapper.find('[data-testid="file-selected"]').trigger('click')
      await waitFor(() => saidOnScreen().length > 0)

      expect((await db.expenses.get('a'))?.categoryKey).toBe('groceries')
      expect((await db.expenses.get('b'))?.categoryKey).toBe('groceries')
      expect(saidOnScreen().join(' ')).toContain('2 filed under Groceries')
    })

    it('empties the list it just cleared', async () => {
      const { wrapper } = await mountScreen([{ id: 'a', description: 'Metro' }])

      await wrapper.find('input[type="checkbox"]').trigger('change')
      await settle()
      await wrapper.find('[data-testid="filing-target"]').setValue('groceries')
      await wrapper.find('[data-testid="file-selected"]').trigger('click')
      await waitFor(() => rowsOn(wrapper).length === 0)

      expect(rowsOn(wrapper)).toHaveLength(0)
      expect(wrapper.find('[data-testid="filing-bar"]').exists()).toBe(false)
    })

    it('will not file anywhere until somewhere is chosen', async () => {
      const { wrapper } = await mountScreen([{ id: 'a', description: 'Metro' }])

      await wrapper.find('input[type="checkbox"]').trigger('change')
      await settle()

      expect(
        wrapper.find('[data-testid="file-selected"]').attributes('disabled'),
      ).toBeDefined()
    })

    it('ticks and unticks the lot', async () => {
      const { wrapper } = await mountScreen([{ id: 'a' }, { id: 'b' }])

      await wrapper.find('[data-testid="select-all"]').trigger('click')
      await settle()
      expect(wrapper.find('[data-testid="selected-count"]').text()).toContain('2')

      await wrapper.find('[data-testid="select-all"]').trigger('click')
      await settle()
      expect(wrapper.find('[data-testid="selected-count"]').text()).toContain('0')
    })

    it('unticks one that was ticked', async () => {
      const { wrapper } = await mountScreen([{ id: 'a' }])

      await wrapper.find('input[type="checkbox"]').trigger('change')
      await settle()
      await wrapper.find('input[type="checkbox"]').trigger('change')
      await settle()

      expect(wrapper.find('[data-testid="filing-bar"]').exists()).toBe(false)
    })

    it('never files a row the filter has hidden', async () => {
      const { wrapper } = await mountScreen([
        { id: 'a', description: 'Metro', categoryKey: 'dining' },
        { id: 'b', description: 'Bus' },
      ])

      await wrapper.find('[data-testid="filing-filter"]').setValue('*')
      await settle()
      await wrapper.find('[data-testid="select-all"]').trigger('click')
      await settle()

      await wrapper.find('[data-testid="filing-filter"]').setValue('dining')
      await settle()
      expect(wrapper.find('[data-testid="selected-count"]').text()).toContain('1')

      await wrapper.find('[data-testid="filing-target"]').setValue('groceries')
      await wrapper.find('[data-testid="file-selected"]').trigger('click')
      await waitFor(() => saidOnScreen().length > 0)

      expect((await db.expenses.get('a'))?.categoryKey).toBe('groceries')
      expect((await db.expenses.get('b'))?.categoryKey ?? null).toBeNull()
    })

    it('unfiles them again when nothing is chosen', async () => {
      const { wrapper } = await mountScreen([{ id: 'a', description: 'Metro', categoryKey: 'dining' }])

      await wrapper.find('[data-testid="filing-filter"]').setValue('dining')
      await settle()
      await wrapper.find('input[type="checkbox"]').trigger('change')
      await settle()
      await wrapper.find('[data-testid="filing-target"]').setValue('')
      await wrapper.find('[data-testid="file-selected"]').trigger('click')
      await waitFor(() => saidOnScreen().length > 0)

      expect((await db.expenses.get('a'))?.categoryKey).toBeNull()
      expect(saidOnScreen().join(' ')).toContain('Not filed')
    })

    it('says when there was nothing to change', async () => {
      const { wrapper } = await mountScreen([{ id: 'a', description: 'Metro', categoryKey: 'dining' }])

      await wrapper.find('[data-testid="filing-filter"]').setValue('dining')
      await settle()
      await wrapper.find('input[type="checkbox"]').trigger('change')
      await settle()
      await wrapper.find('[data-testid="filing-target"]').setValue('dining')
      await wrapper.find('[data-testid="file-selected"]').trigger('click')
      await waitFor(() => saidOnScreen().length > 0)

      expect(saidOnScreen().join(' ')).toContain('Nothing to change')
    })

    it('reports a failure rather than leaving the press silent', async () => {
      const { wrapper, expensesStore } = await mountScreen([{ id: 'a', description: 'Metro' }])

      expensesStore.refile = vi.fn(async () => {
        throw new Error('That group is not on this device.')
      })

      await wrapper.find('input[type="checkbox"]').trigger('change')
      await settle()
      await wrapper.find('[data-testid="filing-target"]').setValue('groceries')
      await wrapper.find('[data-testid="file-selected"]').trigger('click')
      await waitFor(() => saidOnScreen().length > 0)

      expect(saidOnScreen().join(' ')).toContain('not on this device')
    })
  })

  describe('the pass the keywords can do on their own', () => {
    it('files each one where its own words point', async () => {
      const { wrapper } = await mountScreen([
        { id: 'a', description: 'METRO PLUS 4021' },
        { id: 'b', description: 'Resto chez Ti-Guy' },
        { id: 'c', description: 'Something nobody has a word for' },
      ])

      expect(wrapper.find('[data-testid="file-by-keywords"]').text()).toContain('2')

      await wrapper.find('[data-testid="file-by-keywords"]').trigger('click')
      await waitFor(() => saidOnScreen().length > 0)

      expect((await db.expenses.get('a'))?.categoryKey).toBe('groceries')
      expect((await db.expenses.get('b'))?.categoryKey).toBe('dining')
      expect((await db.expenses.get('c'))?.categoryKey ?? null).toBeNull()
      expect(saidOnScreen().join(' ')).toContain('2 filed by their names')
    })

    it('leaves a category somebody chose by hand alone', async () => {
      const { wrapper } = await mountScreen([
        { id: 'a', description: 'Metro', categoryKey: 'dining' },
        { id: 'b', description: 'IGA' },
      ])

      await wrapper.find('[data-testid="file-by-keywords"]').trigger('click')
      await waitFor(() => saidOnScreen().length > 0)

      expect((await db.expenses.get('a'))?.categoryKey).toBe('dining')
      expect((await db.expenses.get('b'))?.categoryKey).toBe('groceries')
    })

    it('is not offered when the words know nothing here', async () => {
      const { wrapper } = await mountScreen([{ id: 'a', description: 'Something else entirely' }])

      expect(wrapper.find('[data-testid="file-by-keywords"]').exists()).toBe(false)
    })

    it('is not offered when the group has no categories at all', async () => {
      const { wrapper } = await mountScreen([{ id: 'a', description: 'Metro' }], [])

      expect(wrapper.find('[data-testid="file-by-keywords"]').exists()).toBe(false)
    })

    it('reports a failure', async () => {
      const { wrapper, expensesStore } = await mountScreen([{ id: 'a', description: 'Metro' }])

      expensesStore.refile = vi.fn(async () => {
        throw new Error('That group is not on this device.')
      })

      await wrapper.find('[data-testid="file-by-keywords"]').trigger('click')
      await waitFor(() => saidOnScreen().length > 0)

      expect(saidOnScreen().join(' ')).toContain('not on this device')
    })
  })

  it('lists a page at a time', async () => {
    const many = Array.from({ length: 60 }, (_, index) => ({
      id: `expense-${index}`,
      description: `Row ${index}`,
      spentAt: `2026-01-${String((index % 28) + 1).padStart(2, '0')}T12:00:00Z`,
    }))

    const { wrapper } = await mountScreen(many)

    expect(rowsOn(wrapper)).toHaveLength(50)
    expect(textOf(wrapper)).toContain('Show 10 more')

    await wrapper.find('[data-testid="show-more-filing"]').trigger('click')
    await settle()

    expect(rowsOn(wrapper)).toHaveLength(60)
  })

  it('starts the page again when the list changes underneath', async () => {
    const many = Array.from({ length: 60 }, (_, index) => ({
      id: `expense-${index}`,
      description: index < 55 ? `Row ${index}` : `Metro ${index}`,
    }))

    const { wrapper } = await mountScreen(many)

    await wrapper.find('[data-testid="show-more-filing"]').trigger('click')
    await settle()
    await wrapper.find('[data-testid="filing-search"]').setValue('Row')
    await settle()

    expect(rowsOn(wrapper)).toHaveLength(50)
  })
})
