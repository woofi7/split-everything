import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import MoneyAmount from '@/components/ui/MoneyAmount.vue'
import SyncIndicator from '@/components/ui/SyncIndicator.vue'

describe('MoneyAmount', () => {
  it('formats the amount for its currency', () => {
    const wrapper = mount(MoneyAmount, { props: { amount: 1234.5, currency: 'CAD' } })

    expect(wrapper.text()).toContain('1,234.50')
  })

  it('shows no decimals for a zero-decimal currency', () => {
    const wrapper = mount(MoneyAmount, { props: { amount: 1234, currency: 'JPY' } })

    expect(wrapper.text()).not.toContain('.')
  })

  it('marks a positive signed amount as owed to you', () => {
    const wrapper = mount(MoneyAmount, { props: { amount: 50, currency: 'CAD', signed: true } })

    expect(wrapper.classes().join(' ')).toContain('text-owed')
    expect(wrapper.text()).toContain('+')
  })

  it('marks a negative signed amount as owing', () => {
    const wrapper = mount(MoneyAmount, { props: { amount: -50, currency: 'CAD', signed: true } })

    expect(wrapper.classes().join(' ')).toContain('text-owing')
    expect(wrapper.text()).toContain('-')
  })

  it('shows a settled balance in neutral, with no sign', () => {
    const wrapper = mount(MoneyAmount, { props: { amount: 0, currency: 'CAD', signed: true } })

    // Colouring zero red or green would imply a debt that does not exist.
    expect(wrapper.attributes('data-settled')).toBe('true')
    expect(wrapper.text()).not.toContain('+')
    expect(wrapper.text()).not.toContain('-')
  })

  it('treats a sub-cent balance as settled', () => {
    const wrapper = mount(MoneyAmount, { props: { amount: 0.004, currency: 'CAD', signed: true } })

    expect(wrapper.attributes('data-settled')).toBe('true')
  })

  it('never colours an unsigned amount', () => {
    const wrapper = mount(MoneyAmount, { props: { amount: -50, currency: 'CAD' } })

    expect(wrapper.classes().join(' ')).not.toContain('text-owing')
  })
})

describe('SyncIndicator', () => {
  const props = { pendingCount: 0, rejectedCount: 0, isOffline: false, isSyncing: false }

  it('says everything is synced when nothing is waiting', () => {
    const wrapper = mount(SyncIndicator, { props })

    expect(wrapper.attributes('data-state')).toBe('synced')
    expect(wrapper.text()).toContain('All synced')
  })

  it('reports how many changes are waiting', () => {
    const wrapper = mount(SyncIndicator, { props: { ...props, pendingCount: 3 } })

    expect(wrapper.text()).toContain('3 waiting')
  })

  it('says offline alongside the waiting count', () => {
    const wrapper = mount(SyncIndicator, {
      props: { ...props, pendingCount: 2, isOffline: true },
    })

    // Offline-first only earns trust if the app says the work is safe.
    expect(wrapper.text()).toContain('Offline')
    expect(wrapper.text()).toContain('2 waiting')
  })

  it('says offline on its own when nothing is queued', () => {
    const wrapper = mount(SyncIndicator, { props: { ...props, isOffline: true } })

    expect(wrapper.attributes('data-state')).toBe('offline')
  })

  it('shows syncing while a flush is in flight', () => {
    const wrapper = mount(SyncIndicator, { props: { ...props, isSyncing: true } })

    expect(wrapper.attributes('data-state')).toBe('syncing')
  })

  it('puts anything needing attention above every other state', () => {
    const wrapper = mount(SyncIndicator, {
      props: { pendingCount: 5, rejectedCount: 1, isOffline: true, isSyncing: true },
    })

    // A rejected change will never resolve on its own, so it must not hide behind
    // a spinner or an offline badge.
    expect(wrapper.attributes('data-state')).toBe('rejected')
    expect(wrapper.text()).toContain('attention')
  })

  it('uses the singular for one rejected change', () => {
    const wrapper = mount(SyncIndicator, { props: { ...props, rejectedCount: 1 } })

    expect(wrapper.text()).toContain('1 change needs attention')
  })

  it('announces itself to a screen reader', () => {
    const wrapper = mount(SyncIndicator, { props })

    expect(wrapper.attributes('role')).toBe('status')
    expect(wrapper.attributes('aria-live')).toBe('polite')
  })
})
