import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import SpendPie from '@/components/ui/SpendPie.vue'

/**
 * Spend per group, as a pie.
 *
 * A list of totals says how much each group cost; a pie says how the spending is
 * distributed, which is the question a dashboard exists to answer. Drawn as plain
 * SVG arcs: a chart library would be a large dependency for one figure, in an app
 * that has to work offline.
 */

const slices = [
  { id: 'a', label: 'Roommates', amount: 600, colorHex: '#4f46e5' },
  { id: 'b', label: 'Ski trip', amount: 300, colorHex: '#0ea5e9' },
  { id: 'c', label: 'World tour', amount: 100, colorHex: '#f97316' },
]

const mountPie = (props = {}) =>
  mount(SpendPie, { props: { slices, currency: 'CAD', ...props } })

describe('SpendPie', () => {
  it('draws one wedge per group', () => {
    const wrapper = mountPie()

    expect(wrapper.findAll('[data-testid="wedge"]')).toHaveLength(3)
  })

  it('gives each group its own colour', () => {
    const wrapper = mountPie()

    const fills = wrapper.findAll('[data-testid="wedge"]').map((w) => w.attributes('fill'))
    expect(fills).toEqual(['#4f46e5', '#0ea5e9', '#f97316'])
  })

  it('sizes each wedge by its share of the total', () => {
    const wrapper = mountPie()

    // 600 of 1000 is a little over half, so the largest wedge has to cross the
    // halfway point, which is what the large-arc flag records.
    const largest = wrapper.findAll('[data-testid="wedge"]')[0]
    expect(largest.attributes('d')).toContain('A')
  })

  it('names each group and its share in the legend', () => {
    const wrapper = mountPie()

    const text = wrapper.text()
    expect(text).toContain('Roommates')
    expect(text).toContain('60%')
    expect(text).toContain('Ski trip')
    expect(text).toContain('30%')
  })

  it('shows the total in the middle', () => {
    const wrapper = mountPie()

    expect(wrapper.text()).toContain('1,000.00')
  })

  it('draws a single group as a full circle rather than an arc', () => {
    // An arc of exactly 360 degrees collapses to nothing, because its start and
    // end points are the same.
    const wrapper = mountPie({ slices: [slices[0]] })

    expect(wrapper.findAll('[data-testid="wedge"]')).toHaveLength(0)
    expect(wrapper.find('[data-testid="whole"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="whole"]').attributes('fill')).toBe('#4f46e5')
  })

  it('says there is nothing to show rather than drawing an empty circle', () => {
    const wrapper = mountPie({ slices: [] })

    expect(wrapper.find('svg').exists()).toBe(false)
    expect(wrapper.text()).toContain('Nothing spent yet')
  })

  it('ignores a group that has spent nothing', () => {
    const wrapper = mountPie({
      slices: [...slices, { id: 'd', label: 'Empty', amount: 0, colorHex: '#000000' }],
    })

    expect(wrapper.findAll('[data-testid="wedge"]')).toHaveLength(3)
    expect(wrapper.text()).not.toContain('Empty')
  })

  it('treats every group spending nothing as nothing to show', () => {
    const wrapper = mountPie({
      slices: [{ id: 'a', label: 'Roommates', amount: 0, colorHex: '#4f46e5' }],
    })

    expect(wrapper.text()).toContain('Nothing spent yet')
  })

  it('describes itself for a screen reader, which cannot read a wedge', () => {
    const wrapper = mountPie()

    const svg = wrapper.find('svg')
    expect(svg.attributes('role')).toBe('img')
    expect(svg.attributes('aria-label')).toContain('Roommates')
  })

  /**
   * The layout of the card.
   *
   * Names under the heading on the left, chart on the right, beside both. The
   * chart is the tallest thing in the card, so putting the words next to it is
   * what stops the card growing to hold a heading above them.
   */
  describe('its arrangement', () => {
    it('renders the heading it is given, above the names', () => {
      const wrapper = mount(SpendPie, {
        props: { slices, currency: 'CAD' },
        slots: { heading: 'Who paid' },
      })

      const column = wrapper.find('div > div')
      expect(column.text()).toContain('Who paid')
      expect(column.find('ul').exists()).toBe(true)
      expect(column.text().indexOf('Who paid')).toBeLessThan(column.text().indexOf('Roommates'))
    })

    it('puts the chart after the names, so it sits on the right', () => {
      const wrapper = mount(SpendPie, {
        props: { slices, currency: 'CAD' },
        slots: { heading: 'Who paid' },
      })

      const html = wrapper.html()
      expect(html.indexOf('<ul')).toBeLessThan(html.indexOf('<svg'))
    })

    it('keeps the heading when there is nothing to chart', () => {
      const wrapper = mount(SpendPie, {
        props: { slices: [], currency: 'CAD' },
        slots: { heading: 'Who paid' },
      })

      // The card would otherwise lose its title exactly when it needs explaining.
      expect(wrapper.text()).toContain('Who paid')
      expect(wrapper.text()).toContain('Nothing spent yet')
      expect(wrapper.find('svg').exists()).toBe(false)
    })

    it('needs no heading', () => {
      const wrapper = mount(SpendPie, { props: { slices, currency: 'CAD' } })

      expect(wrapper.find('p').exists()).toBe(false)
      expect(wrapper.find('ul').exists()).toBe(true)
    })
  })
})
