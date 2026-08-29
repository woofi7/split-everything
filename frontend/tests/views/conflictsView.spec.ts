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

  it('leaves a still-queued change alone', async () => {
    const { wrapper } = await mountView(ConflictsView, {
      outbox: [testRejectedOperation({ status: 'pending', lastError: null })],
    })

    // Pending work is not a problem to solve; it will drain on its own.
    expect(textOf(wrapper)).toContain('Nothing needs your attention')
  })
})
