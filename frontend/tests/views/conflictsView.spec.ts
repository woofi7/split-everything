import { describe, expect, it, vi } from 'vitest'
import { RouterLinkStub } from '@vue/test-utils'
import ConflictsView from '@/views/ConflictsView.vue'
import { db } from '@/offline/db'
import {
  fakeApi,
  mountView,
  settle,
  testConflict,
  testRejectedOperation,
  textOf,
} from '../support/viewHarness'

vi.mock('vue-router', () => ({
  useRoute: () => ({ params: {}, query: {} }),
  useRouter: () => ({ push: vi.fn(), replace: vi.fn() }),
  RouterLink: RouterLinkStub,
}))

describe('ConflictsView', () => {
  it('says nothing needs attention when the lists are empty', async () => {
    const { wrapper } = await mountView(ConflictsView)

    expect(textOf(wrapper)).toContain('Nothing needs your attention')
  })

  it('shows both versions of a conflicting edit', async () => {
    const { wrapper } = await mountView(ConflictsView, { conflicts: [testConflict()] })

    const text = textOf(wrapper)
    expect(text).toContain('Edited on two devices at once')
    expect(text).toContain('Server version')
    expect(text).toContain('My version')
  })

  it('names the fields that actually differ', async () => {
    const { wrapper } = await mountView(ConflictsView, { conflicts: [testConflict()] })

    expect(textOf(wrapper)).toContain('description')
  })

  it('reassures that nothing was overwritten', async () => {
    const { wrapper } = await mountView(ConflictsView, { conflicts: [testConflict()] })

    expect(textOf(wrapper)).toContain('nothing was overwritten')
  })

  it('keeps the server version when asked', async () => {
    const api = fakeApi()
    const { wrapper } = await mountView(ConflictsView, { api, conflicts: [testConflict()] })

    await wrapper
      .findAll('button')
      .find((button) => button.text().includes('Keep the server version'))!
      .trigger('click')
    await settle()

    expect(api.post).toHaveBeenCalledWith(
      '/sync/conflicts/resolve',
      expect.objectContaining({ conflictId: 'conflict-1', resolution: 'KeepLocal' }),
    )
  })

  it('keeps my version when asked', async () => {
    const api = fakeApi()
    const { wrapper } = await mountView(ConflictsView, { api, conflicts: [testConflict()] })

    await wrapper
      .findAll('button')
      .find((button) => button.text() === 'Keep mine')!
      .trigger('click')
    await settle()

    expect(api.post).toHaveBeenCalledWith(
      '/sync/conflicts/resolve',
      expect.objectContaining({ resolution: 'KeepRemote' }),
    )
  })

  it('drops a resolved conflict from the list', async () => {
    const { wrapper } = await mountView(ConflictsView, { conflicts: [testConflict()] })

    await wrapper
      .findAll('button')
      .find((button) => button.text() === 'Keep mine')!
      .trigger('click')
    await settle()

    expect(await db.conflicts.count()).toBe(0)
    expect(textOf(wrapper)).toContain('Nothing needs your attention')
  })

  it('reports a resolution the server refused and keeps the conflict', async () => {
    const api = fakeApi()
    api.post.mockRejectedValue(new Error('Conflict was not found.'))

    const { wrapper } = await mountView(ConflictsView, { api, conflicts: [testConflict()] })

    await wrapper
      .findAll('button')
      .find((button) => button.text() === 'Keep mine')!
      .trigger('click')
    await settle()

    expect(textOf(wrapper)).toContain('not found')
    // It stays on the list, so the person can try again.
    expect(await db.conflicts.count()).toBe(1)
  })

  it('renders a payload it cannot parse without breaking', async () => {
    const { wrapper } = await mountView(ConflictsView, {
      conflicts: [testConflict({ storedPayloadJson: 'not json' })],
    })

    expect(textOf(wrapper)).toContain('unreadable')
  })

  it('shows an empty field rather than nothing at all', async () => {
    const { wrapper } = await mountView(ConflictsView, {
      conflicts: [testConflict({ storedPayloadJson: '{}', incomingPayloadJson: '{}' })],
    })

    expect(textOf(wrapper)).toContain('(empty)')
  })

  it('falls back to naming the whole record when no field is identified', async () => {
    const { wrapper } = await mountView(ConflictsView, {
      conflicts: [testConflict({ conflictingFields: [] })],
    })

    expect(textOf(wrapper)).toContain('whole record')
  })

  it('lists a change the server refused, with the reason', async () => {
    const { wrapper } = await mountView(ConflictsView, {
      outbox: [testRejectedOperation()],
    })

    const text = textOf(wrapper)
    expect(text).toContain('Changes the server refused')
    expect(text).toContain('needs a description')
  })

  it('discards a refused change when the user gives up on it', async () => {
    const { wrapper } = await mountView(ConflictsView, {
      outbox: [testRejectedOperation()],
    })

    await wrapper
      .findAll('button')
      .find((button) => button.text().includes('Discard'))!
      .trigger('click')
    await settle()

    expect(await db.outbox.count()).toBe(0)
    expect(textOf(wrapper)).toContain('Nothing needs your attention')
  })

  it('shows a still-queued change as waiting, not as a problem', async () => {
    const { wrapper } = await mountView(ConflictsView, {
      outbox: [testRejectedOperation({ status: 'pending', lastError: null })],
    })

    // It used to say nothing needs attention while the header counted it as
    // waiting to sync. A number with nothing behind it is not an answer.
    expect(textOf(wrapper)).not.toContain('Nothing needs your attention')
    expect(wrapper.findAll('[data-testid="waiting-operation"]')).toHaveLength(1)
    expect(textOf(wrapper)).toContain('waiting to be sent')

    // Still not presented as a refusal: there is nothing to discard.
    expect(textOf(wrapper)).not.toContain('the server refused')
  })

  it('says why a queued change has not gone, when it knows', async () => {
    const { wrapper } = await mountView(ConflictsView, {
      outbox: [testRejectedOperation({ status: 'pending', lastError: 'Could not reach the server.' })],
    })

    expect(wrapper.find('[data-testid="waiting-operation"]').text())
      .toContain('Could not reach the server.')
  })

  it('says nothing needs attention when the queue is empty', async () => {
    const { wrapper } = await mountView(ConflictsView, { outbox: [] })

    expect(textOf(wrapper)).toContain('Nothing needs your attention')
  })

  /**
   * The last resort for a replica that has diverged.
   *
   * Every screen reads from the local replica, so when it is wrong there is
   * nothing else to look at and no way to argue with it from inside the app.
   */
  describe('reloading everything from the server', () => {
    it('does not throw the replica away on one tap', async () => {
      const { wrapper, expensesStore } = await mountView(ConflictsView)
      const reset = vi.spyOn(expensesStore, 'resetToServer')

      await wrapper.find('[data-testid="reset-replica"]').trigger('click')
      await settle(1)

      expect(reset).not.toHaveBeenCalled()
      expect(wrapper.find('[data-testid="reset-replica-confirm"]').exists()).toBe(true)
    })

    it('says how much unsent work would be lost', async () => {
      const { wrapper } = await mountView(ConflictsView, {
        outbox: [
          testRejectedOperation({ status: 'pending', lastError: null }),
          testRejectedOperation({ operationId: 'op-2', status: 'pending', lastError: null }),
        ],
      })

      await wrapper.find('[data-testid="reset-replica"]').trigger('click')
      await settle(1)

      // The one thing the server cannot give back.
      expect(textOf(wrapper)).toContain('2 change(s) that have not reached the server')
    })

    it('brings the groups back too, not only the expenses', async () => {
      const { wrapper, groupsStore } = await mountView(ConflictsView)
      const loadAll = vi.spyOn(groupsStore, 'loadAll')

      await wrapper.find('[data-testid="reset-replica"]').trigger('click')
      await settle(1)
      await wrapper.find('[data-testid="reset-replica-confirm"]').trigger('click')
      await settle()

      // Groups come from their own endpoint rather than from the sync log, so a
      // pull alone leaves every expense with no group to hang on.
      expect(loadAll).toHaveBeenCalled()
    })

    it('reloads once confirmed', async () => {
      const { wrapper, expensesStore } = await mountView(ConflictsView)
      const reset = vi.spyOn(expensesStore, 'resetToServer').mockResolvedValue()

      await wrapper.find('[data-testid="reset-replica"]').trigger('click')
      await settle(1)
      await wrapper.find('[data-testid="reset-replica-confirm"]').trigger('click')
      await settle()

      expect(reset).toHaveBeenCalled()
    })

    it('can be backed out of', async () => {
      const { wrapper, expensesStore } = await mountView(ConflictsView)
      const reset = vi.spyOn(expensesStore, 'resetToServer')

      await wrapper.find('[data-testid="reset-replica"]').trigger('click')
      await settle(1)
      await wrapper.findAll('button').find((b) => b.text() === 'Cancel')!.trigger('click')
      await settle(1)

      expect(reset).not.toHaveBeenCalled()
      expect(wrapper.find('[data-testid="reset-replica-confirm"]').exists()).toBe(false)
    })
  })

  /**
   * Sending what is waiting, on purpose.
   *
   * The queue drains by itself when a connection returns, but "by itself" is not
   * something somebody staring at a count of three can watch happen.
   */
  describe('sending the queue', () => {
    const waiting = () => ({
      outbox: [testRejectedOperation({ operationId: 'op-waiting', status: 'pending' })],
    })

    it('offers to send when something is waiting', async () => {
      const { wrapper } = await mountView(ConflictsView, waiting())

      expect(wrapper.find('[data-testid="sync-now"]').text()).toContain('Send now')
    })

    it('offers nothing to send when the queue is empty', async () => {
      const { wrapper } = await mountView(ConflictsView)

      expect(wrapper.find('[data-testid="sync-now"]').exists()).toBe(false)
    })

    it('drains the queue when asked', async () => {
      const { wrapper, expensesStore } = await mountView(ConflictsView, waiting())
      const sync = vi.spyOn(expensesStore, 'sync').mockResolvedValue(undefined)

      await wrapper.find('[data-testid="sync-now"]').trigger('click')
      await settle()

      expect(sync).toHaveBeenCalled()
    })

    it('says so when it could not send, rather than throwing', async () => {
      const { wrapper, expensesStore } = await mountView(ConflictsView, waiting())
      vi.spyOn(expensesStore, 'sync').mockRejectedValue(new Error('offline'))

      await wrapper.find('[data-testid="sync-now"]').trigger('click')
      await settle()

      // Offline is why the queue exists in the first place.
      expect(textOf(wrapper)).toContain('offline')
    })

    it('reads the list back afterwards, so what went through disappears', async () => {
      const { wrapper, expensesStore } = await mountView(ConflictsView, waiting())
      vi.spyOn(expensesStore, 'sync').mockImplementation(async () => {
        await db.outbox.clear()
      })

      await wrapper.find('[data-testid="sync-now"]').trigger('click')
      await settle()

      expect(wrapper.find('[data-testid="waiting-operation"]').exists()).toBe(false)
    })
  })
})
