import { describe, expect, it, vi } from 'vitest'
import { RouterLinkStub } from '@vue/test-utils'
import ExpenseView from '@/views/ExpenseView.vue'
import { db } from '@/offline/db'
import { ALICE, BOB, GROUP_ID, fakeApi, mountView, settle, testExpense, testGroup, textOf, waitFor } from '../support/viewHarness'

const replace = vi.fn()

vi.mock('vue-router', () => ({
  useRoute: () => ({ params: { groupId: GROUP_ID, expenseId: 'expense-1' }, query: {} }),
  useRouter: () => ({ push: vi.fn(), replace }),
  RouterLink: RouterLinkStub,
}))

const api = () => fakeApi({ '/groups': () => testGroup() })

describe('ExpenseView', () => {
  it('shows the amount, payer and date', async () => {
    const { wrapper } = await mountView(ExpenseView, {
      api: api(),
      expenses: [testExpense()],
    })

    const text = textOf(wrapper)
    expect(text).toContain('Dinner')
    expect(text).toContain('60.00')
    expect(text).toContain('Alice paid on')
  })

  it('names both people when two of them paid', async () => {
    const { wrapper } = await mountView(ExpenseView, {
      api: api(),
      expenses: [
        testExpense({
          amount: 65,
          amountInBaseCurrency: 65,
          payers: [
            { memberId: ALICE, amount: 40, amountInBaseCurrency: 40 },
            { memberId: BOB, amount: 25, amountInBaseCurrency: 25 },
          ],
          splits: [
            { memberId: ALICE, amount: 32.5, amountInBaseCurrency: 32.5, inputValue: null },
            { memberId: BOB, amount: 32.5, amountInBaseCurrency: 32.5, inputValue: null },
          ],
        }),
      ],
    })

    // Not "Alice paid": the pans were bought by the two of them.
    expect(textOf(wrapper)).toContain('Alice and Bob paid on')
  })

  it('breaks down what each of them put in', async () => {
    const { wrapper } = await mountView(ExpenseView, {
      api: api(),
      expenses: [
        testExpense({
          amount: 65,
          amountInBaseCurrency: 65,
          payers: [
            { memberId: ALICE, amount: 40, amountInBaseCurrency: 40 },
            { memberId: BOB, amount: 25, amountInBaseCurrency: 25 },
          ],
          splits: [
            { memberId: ALICE, amount: 32.5, amountInBaseCurrency: 32.5, inputValue: null },
            { memberId: BOB, amount: 32.5, amountInBaseCurrency: 32.5, inputValue: null },
          ],
        }),
      ],
    })

    // What each paid is a different question from what each owes, and the answer to
    // the second is already on screen below.
    const breakdown = wrapper.find('[data-testid="payer-breakdown"]').text()
    expect(breakdown).toContain('40.00')
    expect(breakdown).toContain('25.00')
  })

  it('says one name when one person paid', async () => {
    const { wrapper } = await mountView(ExpenseView, {
      api: api(),
      expenses: [testExpense()],
    })

    expect(wrapper.find('[data-testid="payer-breakdown"]').exists()).toBe(false)
  })

  it('lists the split per member', async () => {
    const { wrapper } = await mountView(ExpenseView, {
      api: api(),
      expenses: [testExpense()],
    })

    const text = textOf(wrapper)
    expect(text).toContain('Split')
    expect(text).toContain('Alice')
    expect(text).toContain('Bob')
    expect(text).toContain('30.00')
  })

  it('marks an expense still waiting to sync', async () => {
    const { wrapper } = await mountView(ExpenseView, {
      api: api(),
      expenses: [testExpense({ pending: true })],
    })

    expect(textOf(wrapper)).toContain('Waiting to sync')
  })

  it('warns that a foreign currency is converted on sync', async () => {
    const { wrapper } = await mountView(ExpenseView, {
      api: api(),
      expenses: [testExpense({ currency: 'EUR' })],
    })

    expect(textOf(wrapper)).toContain('Converted to the group currency')
  })

  it('does not warn about the group currency', async () => {
    const { wrapper } = await mountView(ExpenseView, {
      api: api(),
      expenses: [testExpense()],
    })

    expect(textOf(wrapper)).not.toContain('Converted to the group currency')
  })

  it('shows the notes when there are any', async () => {
    const { wrapper } = await mountView(ExpenseView, {
      api: api(),
      expenses: [testExpense({ notes: 'Paid in cash' })],
    })

    expect(textOf(wrapper)).toContain('Paid in cash')
  })

  it('lists the items of an itemized expense with who had what', async () => {
    const { wrapper } = await mountView(ExpenseView, {
      api: api(),
      expenses: [
        testExpense({
          splitType: 'Itemized',
          items: [
            { id: null, description: 'Starter', amount: 10, quantity: 1, sortOrder: 0, memberIds: [BOB] },
            { id: null, description: 'Mains', amount: 25, quantity: 2, sortOrder: 1, memberIds: [ALICE] },
          ],
        }),
      ],
    })

    const text = textOf(wrapper)
    expect(text).toContain('Items')
    expect(text).toContain('Starter')
    // Quantity is multiplied in the line total.
    expect(text).toContain('50.00')
  })

  it('says a line is for everyone when nobody is named', async () => {
    const { wrapper } = await mountView(ExpenseView, {
      api: api(),
      expenses: [
        testExpense({
          items: [
            { id: null, description: 'Tax', amount: 5, quantity: 1, sortOrder: 0, memberIds: [] },
          ],
        }),
      ],
    })

    expect(textOf(wrapper)).toContain('Everyone')
  })

  it('posts a comment and shows it', async () => {
    const { wrapper } = await mountView(ExpenseView, {
      api: api(),
      expenses: [testExpense()],
    })

    await wrapper.find('input[type="text"]').setValue('Was this the taxi?')
    await wrapper.find('form').trigger('submit')
    await waitFor(() => textOf(wrapper).includes('Was this the taxi?'))

    expect(textOf(wrapper)).toContain('Was this the taxi?')
    expect(textOf(wrapper)).toContain('Comments (1)')
  })

  it('clears the comment box after posting', async () => {
    const { wrapper } = await mountView(ExpenseView, {
      api: api(),
      expenses: [testExpense()],
    })

    const input = wrapper.find('input[type="text"]')
    await input.setValue('Noted')
    await wrapper.find('form').trigger('submit')
    // Posting queues the comment and kicks a background drain, so the box clears
    // a few turns later. Waiting on the box itself rather than counting turns.
    await waitFor(() => (input.element as HTMLInputElement).value === '')

    expect((input.element as HTMLInputElement).value).toBe('')
  })

  it('reports an empty comment rather than posting it', async () => {
    const { wrapper } = await mountView(ExpenseView, {
      api: api(),
      expenses: [testExpense()],
    })

    await wrapper.find('input[type="text"]').setValue('   ')
    await wrapper.find('form').trigger('submit')
    await settle()

    expect(wrapper.find('[role="alert"]').exists()).toBe(true)
    expect(textOf(wrapper)).toContain('Comments (0)')
  })

  it('asks before deleting the expense', async () => {
    const { wrapper } = await mountView(ExpenseView, {
      api: api(),
      expenses: [testExpense()],
    })

    await wrapper.find('[data-testid="delete-expense"]').trigger('click')
    await settle(1)

    // An expense is somebody else's balance as well as yours, and the only way
    // back is to add it again from memory.
    expect((await db.expenses.get('expense-1'))?.isDeleted).toBe(false)
    expect(replace).not.toHaveBeenCalled()
    expect(textOf(wrapper)).toContain('Delete this expense?')
  })

  it('names the expense in the question, so it is clear which one', async () => {
    const { wrapper } = await mountView(ExpenseView, {
      api: api(),
      expenses: [testExpense({ description: 'Groceries at Metro' })],
    })

    await wrapper.find('[data-testid="delete-expense"]').trigger('click')
    await settle(1)

    expect(textOf(wrapper)).toContain('Groceries at Metro')
  })

  it('says what it does to the balances', async () => {
    const { wrapper } = await mountView(ExpenseView, {
      api: api(),
      expenses: [testExpense()],
    })

    await wrapper.find('[data-testid="delete-expense"]').trigger('click')
    await settle(1)

    expect(textOf(wrapper)).toContain("everyone's balance")
  })

  it('deletes the expense once confirmed and goes back to the group', async () => {
    const { wrapper } = await mountView(ExpenseView, {
      api: api(),
      expenses: [testExpense()],
    })

    await wrapper.find('[data-testid="delete-expense"]').trigger('click')
    await settle(1)
    await wrapper.find('[data-testid="confirm-delete"]').trigger('click')
    // The redirect is the last thing to happen, so it is the only safe signal
    // that the whole action finished.
    await waitFor(() => replace.mock.calls.length > 0)

    expect((await db.expenses.get('expense-1'))?.isDeleted).toBe(true)
    expect(replace).toHaveBeenCalledWith({ name: 'group', params: { groupId: GROUP_ID } })
  })

  it('can back out of deleting', async () => {
    const { wrapper } = await mountView(ExpenseView, {
      api: api(),
      expenses: [testExpense()],
    })

    await wrapper.find('[data-testid="delete-expense"]').trigger('click')
    await settle(1)
    await wrapper.find('[data-testid="cancel-delete"]').trigger('click')
    await settle(1)

    expect((await db.expenses.get('expense-1'))?.isDeleted).toBe(false)
    expect(wrapper.find('[data-testid="confirm-delete"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="delete-expense"]').exists()).toBe(true)
  })

  it('says so when the expense is not on this device', async () => {
    const { wrapper } = await mountView(ExpenseView, { api: api(), expenses: [] })

    expect(textOf(wrapper)).toContain('not on this device')
  })
})

describe('ExpenseView comments', () => {
  it('offers to delete a comment you wrote', async () => {
    const { wrapper } = await mountView(ExpenseView, {
      api: api(),
      expenses: [testExpense()],
    })

    await wrapper.find('input[type="text"]').setValue('Mine')
    await wrapper.find('form').trigger('submit')
    await waitFor(() => textOf(wrapper).includes('Mine'))

    expect(wrapper.find('[data-testid="delete-comment"]').exists()).toBe(true)
  })

  it('deletes it', async () => {
    const { wrapper } = await mountView(ExpenseView, {
      api: api(),
      expenses: [testExpense()],
    })

    await wrapper.find('input[type="text"]').setValue('Mine')
    await wrapper.find('form').trigger('submit')
    await waitFor(() => textOf(wrapper).includes('Mine'))

    await wrapper.find('[data-testid="delete-comment"]').trigger('click')
    await waitFor(() => !textOf(wrapper).includes('Mine'))

    expect(textOf(wrapper)).toContain('Comments (0)')
  })

  it('does not offer to delete someone else comment', async () => {
    const { wrapper } = await mountView(ExpenseView, {
      api: api(),
      expenses: [testExpense()],
      comments: [
        {
          id: 'comment-other',
          expenseId: 'expense-1',
          groupId: GROUP_ID,
          authorMemberId: BOB,
          parentCommentId: null,
          body: 'Not mine',
          createdAt: '2026-01-05T13:00:00.000Z',
          isDeleted: false,
          vectorClock: {},
          pending: false,
        },
      ],
    })
    await settle()

    expect(textOf(wrapper)).toContain('Not mine')
    expect(wrapper.find('[data-testid="delete-comment"]').exists()).toBe(false)
  })
})

describe('ExpenseView card colour', () => {
  it('carries the same payer colour the list card had', async () => {
    const { wrapper } = await mountView(ExpenseView, {
      api: api(),
      expenses: [testExpense({ paidByMemberId: ALICE })],
    })
    await settle()

    // Opening an expense should not change whose it appears to be.
    const style = wrapper.find('[data-testid="expense-card"]').attributes('style') ?? ''
    expect(style).toContain('color-mix')
    expect(style).toContain('--surface-raised')
  })
})

describe('ExpenseView split type', () => {
  it('says how the expense was split, not just how much', async () => {
    const { wrapper } = await mountView(ExpenseView, {
      api: api(),
      expenses: [testExpense({ splitType: 'Percentage' })],
    })
    await settle()

    // Two expenses with identical shares can have been divided by quite different
    // rules, and the rule is what someone checks when the numbers look wrong.
    expect(wrapper.find('[data-testid="split-type"]').text()).toBe('By percentage')
  })

  it('names an equal split too', async () => {
    const { wrapper } = await mountView(ExpenseView, {
      api: api(),
      expenses: [testExpense({ splitType: 'Equal' })],
    })
    await settle()

    expect(wrapper.find('[data-testid="split-type"]').text()).toBe('Equally')
  })

  it('shows the percentage behind each share', async () => {
    const { wrapper } = await mountView(ExpenseView, {
      api: api(),
      expenses: [
        testExpense({
          splitType: 'Percentage',
          amount: 100,
          splits: [
            { memberId: ALICE, amount: 70, amountInBaseCurrency: 70, inputValue: 70 },
            { memberId: BOB, amount: 30, amountInBaseCurrency: 30, inputValue: 30 },
          ],
        }),
      ],
    })
    await settle()

    const text = textOf(wrapper)
    expect(text).toContain('70%')
    expect(text).toContain('30%')
  })

  it('counts shares as shares', async () => {
    const { wrapper } = await mountView(ExpenseView, {
      api: api(),
      expenses: [
        testExpense({
          splitType: 'Shares',
          amount: 90,
          splits: [
            { memberId: ALICE, amount: 60, amountInBaseCurrency: 60, inputValue: 2 },
            { memberId: BOB, amount: 30, amountInBaseCurrency: 30, inputValue: 1 },
          ],
        }),
      ],
    })
    await settle()

    const text = textOf(wrapper)
    expect(text).toContain('2 shares')
    expect(text).toContain('1 share')
  })

  it('says nothing about weights for an equal split', async () => {
    const { wrapper } = await mountView(ExpenseView, {
      api: api(),
      expenses: [testExpense({ splitType: 'Equal' })],
    })
    await settle()

    // Equal has no weight to show; an empty label beside each name would be noise.
    expect(textOf(wrapper)).not.toContain('shares')
  })
})
