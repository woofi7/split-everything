import { describe, expect, it, vi } from 'vitest'
import { RouterLinkStub } from '@vue/test-utils'
import AdminView from '@/views/AdminView.vue'
import { fakeApi, mountView, saidOnScreen, settle, textOf } from '../support/viewHarness'

vi.mock('vue-router', () => ({
  useRoute: () => ({ params: {}, query: {} }),
  useRouter: () => ({ push: vi.fn(), replace: vi.fn() }),
  RouterLink: RouterLinkStub,
}))

/**
 * Every group on the server, for whoever runs it.
 *
 * Six groups called the same thing, five of them archived leftovers of a transfer,
 * and no way to be rid of them: that is what this screen is for. It is also the
 * only screen in the application that destroys anything, so most of what follows
 * is about how hard that is to do by accident.
 */
describe('the server groups screen', () => {
  const live = {
    id: 'group-live',
    name: 'Nicoco & Emmouuu',
    baseCurrency: 'CAD',
    iconName: null,
    colorHex: '#4f46e5',
    isArchived: false,
    archivedAt: null,
    createdAt: '2026-01-01T00:00:00Z',
    lastActivityAt: '2026-09-14T00:00:00Z',
    createdByName: 'Nicolas',
    memberCount: 2,
    expenseCount: 412,
    totalSpend: 38_204.12,
    isMine: true,
  }

  const leftover = {
    ...live,
    id: 'group-old',
    name: 'World tour',
    isArchived: true,
    archivedAt: '2026-08-01T00:00:00Z',
    expenseCount: 9,
    totalSpend: 214.5,
    isMine: false,
  }

  async function mountAdmin(options: { isAdmin?: boolean } = {}) {
    const api = fakeApi({ '/admin/groups': () => [live, leftover] })
    const mounted = await mountView(AdminView, {
      api,
      user: { isAdmin: options.isAdmin ?? true },
    })
    await settle()
    return mounted
  }

  it('lists every group on the server, its own and other people’s', async () => {
    const { wrapper } = await mountAdmin()

    const rows = wrapper.findAll('[data-testid="admin-group"]')
    expect(rows.map((row) => row.attributes('data-group-id'))).toEqual(['group-live', 'group-old'])
    expect(rows[0].text()).toContain('412 expenses')
  })

  it('offers to delete an archived group and nothing else', async () => {
    const { wrapper } = await mountAdmin()

    const rows = wrapper.findAll('[data-testid="admin-group"]')
    expect(rows[0].find('[data-testid="delete-group"]').exists()).toBe(false)
    expect(rows[1].find('[data-testid="delete-group"]').exists()).toBe(true)
  })

  it('will not delete until the name is typed', async () => {
    const { wrapper, api } = await mountAdmin()

    await wrapper.find('[data-testid="delete-group"]').trigger('click')
    await settle()

    const confirm = wrapper.find('[data-testid="confirm-delete"]')
    expect(confirm.attributes('disabled')).toBeDefined()

    await wrapper.find('[data-testid="confirm-name"]').setValue('World')
    await settle()
    expect(wrapper.find('[data-testid="confirm-delete"]').attributes('disabled')).toBeDefined()

    await wrapper.find('[data-testid="confirm-name"]').setValue('World tour')
    await settle()
    expect(wrapper.find('[data-testid="confirm-delete"]').attributes('disabled')).toBeUndefined()

    // Nothing has been asked of the server on the way here.
    expect(api.delete).not.toHaveBeenCalled()
  })

  it('deletes it once the name matches, and drops it from the list', async () => {
    const { wrapper, api } = await mountAdmin()

    await wrapper.find('[data-testid="delete-group"]').trigger('click')
    await wrapper.find('[data-testid="confirm-name"]').setValue('World tour')
    await wrapper.find('[data-testid="confirm-delete"]').trigger('click')
    await settle()

    expect(api.delete).toHaveBeenCalledWith('/admin/groups/group-old')
    expect(wrapper.findAll('[data-testid="admin-group"]')).toHaveLength(1)
    expect(saidOnScreen().join(' ')).toContain('World tour')
  })

  it('says plainly that it is not for everybody, and asks the server nothing', async () => {
    const { wrapper, api } = await mountAdmin({ isAdmin: false })

    expect(textOf(wrapper)).toContain('whoever runs this server')
    expect(wrapper.find('[data-testid="admin-group"]').exists()).toBe(false)
    expect(api.get).not.toHaveBeenCalledWith('/admin/groups')
  })
})
