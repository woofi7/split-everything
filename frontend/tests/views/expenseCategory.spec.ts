import { describe, expect, it, vi } from 'vitest'
import { RouterLinkStub } from '@vue/test-utils'
import ExpenseFormView from '@/views/ExpenseFormView.vue'
import DashboardView from '@/views/DashboardView.vue'
import {
  ALICE,
  GROUP_ID,
  fakeApi,
  mountView,
  settle,
  testExpense,
  testGroup,
  textOf,
} from '../support/viewHarness'
import type { Category } from '@/domain/categories'

const push = vi.fn()
const replace = vi.fn()
let routeParams: Record<string, string> = {}

vi.mock('vue-router', () => ({
  useRoute: () => ({ params: routeParams, query: {}, fullPath: '/add' }),
  useRouter: () => ({ push, replace }),
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

describe('filing an expense', () => {
  async function mountForm() {
    routeParams = {}

    const mounted = await mountView(ExpenseFormView, {
      api: fakeApi({
        '/groups/group-1/categories': () => categories,
        '/groups': () => testGroup(),
      }),
      groups: [testGroup()],
    })
    await settle()
    return mounted
  }

  it('guesses from what the expense is called', async () => {
    const { wrapper } = await mountForm()

    await wrapper.find('input[placeholder="Groceries"]').setValue('Metro plus')
    await settle()

    expect(wrapper.find('[data-testid="category"]').attributes('data-category')).toBe('groceries')
    expect(wrapper.find('[data-testid="category"]').text()).toContain('Groceries')
    expect(wrapper.find('[data-testid="category-guess"]').exists()).toBe(true)
  })

  it('stops guessing once somebody has answered', async () => {
    const { wrapper } = await mountForm()

    await wrapper.find('input[placeholder="Groceries"]').setValue('Metro plus')
    await settle()

    await wrapper.find('[data-testid="category"]').trigger('click')
    await settle()
    await wrapper.find('[data-category="dining"][data-testid="category-option"]').trigger('click')
    await settle()

    await wrapper.find('input[placeholder="Groceries"]').setValue('Metro plus express')
    await settle()

    expect(wrapper.find('[data-testid="category"]').attributes('data-category')).toBe('dining')
    expect(wrapper.find('[data-testid="category-guess"]').exists()).toBe(false)
  })

  it('saves what it was filed under', async () => {
    const { wrapper, expensesStore } = await mountForm()

    await wrapper.find('input[inputmode="decimal"]').setValue('62')
    await wrapper.find('input[placeholder="Groceries"]').setValue('Metro plus')
    await settle()
    await wrapper.find('form').trigger('submit')
    await settle()

    expect(expensesStore.forGroup(GROUP_ID)[0].categoryKey).toBe('groceries')
  })

  it('files it under nothing when nothing fits', async () => {
    const { wrapper, expensesStore } = await mountForm()

    await wrapper.find('input[inputmode="decimal"]').setValue('40')
    await wrapper.find('input[placeholder="Groceries"]').setValue('Cadeau pour Emma')
    await settle()
    await wrapper.find('form').trigger('submit')
    await settle()

    expect(expensesStore.forGroup(GROUP_ID)[0].categoryKey).toBeNull()
  })

  it('marks the card with the category it was filed under', async () => {
    routeParams = { groupId: GROUP_ID }

    const { wrapper } = await mountView(DashboardView, {
      api: fakeApi({
        '/groups/group-1/categories': () => categories,
        '/groups': () => testGroup(),
      }),
      groups: [testGroup()],
      expenses: [
        testExpense({ id: 'filed', description: 'Metro', categoryKey: 'groceries' }),
        testExpense({ id: 'unfiled', description: 'Cadeau', paidByMemberId: ALICE }),
      ],
    })
    await settle()

    expect(wrapper.find('[data-category="groceries"]').exists()).toBe(true)
    expect(wrapper.findAll('[data-testid="expense-category"]')).toHaveLength(1)
    expect(textOf(wrapper)).toContain('Metro')
  })
})
