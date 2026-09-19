import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import SpendPie from '@/components/ui/SpendPie.vue'

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
    expect(svg.attributes('role')).toBe('group')
    expect(svg.attributes('aria-label')).toContain('Roommates')
  })

  it('names every wedge, since each one can be pressed', () => {
    const wrapper = mountPie()

    const labels = wrapper
      .findAll('[data-testid="wedge"]')
      .map((wedge) => wedge.attributes('aria-label'))

    expect(labels[0]).toContain('Roommates')
    expect(labels[0]).toContain('600.00')
    expect(labels[0]).toContain('60%')
  })

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

  describe('asking about a slice', () => {
    it('shows the total until something is picked', () => {
      const wrapper = mountPie()

      expect(wrapper.find('[data-testid="centre-total"]').text()).toContain('1,000.00')
      expect(wrapper.find('[data-testid="centre-amount"]').exists()).toBe(false)
    })

    it('answers with the amount and the share when a wedge is hovered', async () => {
      const wrapper = mountPie()

      await wrapper.findAll('[data-testid="wedge"]')[0].trigger('mouseenter')

      expect(wrapper.find('[data-testid="centre-amount"]').text()).toContain('600.00')
      expect(wrapper.find('[data-testid="centre-share"]').text()).toBe('60%')
      expect(wrapper.find('[data-testid="centre-total"]').exists()).toBe(false)
    })

    it('names the slice by highlighting it, and shows its amount there too', async () => {
      const wrapper = mountPie()

      await wrapper.findAll('[data-testid="wedge"]')[1].trigger('mouseenter')

      const rows = wrapper.findAll('[data-testid="legend-row"]')
      expect(rows[1].attributes('aria-pressed')).toBe('true')
      expect(rows[1].find('[data-testid="legend-amount"]').text()).toContain('300.00')
      expect(rows[0].find('[data-testid="legend-amount"]').exists()).toBe(false)
    })

    it('puts the total back when the pointer leaves', async () => {
      const wrapper = mountPie()
      const wedge = wrapper.findAll('[data-testid="wedge"]')[0]

      await wedge.trigger('mouseenter')
      await wedge.trigger('mouseleave')

      expect(wrapper.find('[data-testid="centre-total"]').exists()).toBe(true)
    })

    it('dims the others, so the one asked about is obvious', async () => {
      const wrapper = mountPie()

      await wrapper.findAll('[data-testid="wedge"]')[0].trigger('mouseenter')

      const opacities = wrapper.findAll('[data-testid="wedge"]').map((w) => w.attributes('opacity'))
      expect(opacities[0]).toBe('1')
      expect(opacities[1]).toBe('0.35')
    })

    it('answers a tap, which is all a phone has', async () => {
      const wrapper = mountPie()

      await wrapper.findAll('[data-testid="wedge"]')[2].trigger('click')

      expect(wrapper.find('[data-testid="centre-amount"]').text()).toContain('100.00')
    })

    it('switches to another wedge when that one is clicked', async () => {
      const wrapper = mountPie()
      const wedges = wrapper.findAll('[data-testid="wedge"]')

      await wedges[0].trigger('mouseenter')
      await wedges[0].trigger('click')
      await wedges[0].trigger('mouseleave')
      await wedges[1].trigger('mouseenter')
      await wedges[1].trigger('click')

      expect(wrapper.find('[data-testid="centre-amount"]').text()).toContain('300.00')
      expect(wrapper.find('[data-testid="centre-share"]').text()).toBe('30%')
    })

    it('switches between names the same way', async () => {
      const wrapper = mountPie()
      const rows = wrapper.findAll('[data-testid="legend-row"]')

      await rows[0].trigger('mouseenter')
      await rows[0].trigger('click')
      await rows[0].trigger('mouseleave')
      await rows[1].trigger('mouseenter')
      await rows[1].trigger('click')

      expect(wrapper.find('[data-testid="centre-amount"]').text()).toContain('300.00')
    })

    it('keeps a tapped slice on show after the pointer leaves', async () => {
      const wrapper = mountPie()
      const wedge = wrapper.findAll('[data-testid="wedge"]')[0]

      await wedge.trigger('mouseenter')
      await wedge.trigger('click')
      await wedge.trigger('mouseleave')

      expect(wrapper.find('[data-testid="centre-amount"]').text()).toContain('600.00')
    })

    it('lets a second tap put the total back', async () => {
      const wrapper = mountPie()
      const wedge = wrapper.findAll('[data-testid="wedge"]')[2]

      await wedge.trigger('mouseenter')
      await wedge.trigger('click')
      await wedge.trigger('click')

      expect(wrapper.find('[data-testid="centre-total"]').exists()).toBe(true)
    })

    it('answers the name beside the wedge as well', async () => {
      const wrapper = mountPie()

      await wrapper.findAll('[data-testid="legend-row"]')[1].trigger('click')

      expect(wrapper.find('[data-testid="centre-amount"]').text()).toContain('300.00')
    })

    it('answers keyboard focus on a name', async () => {
      const wrapper = mountPie()

      await wrapper.findAll('[data-testid="legend-row"]')[0].trigger('focus')

      expect(wrapper.find('[data-testid="centre-amount"]').text()).toContain('600.00')
    })

    it('answers a single-slice chart, which is a circle rather than a wedge', async () => {
      const wrapper = mountPie({ slices: [slices[0]] })

      await wrapper.find('[data-testid="whole"]').trigger('mouseenter')

      expect(wrapper.find('[data-testid="centre-amount"]').text()).toContain('600.00')
      expect(wrapper.find('[data-testid="centre-share"]').text()).toBe('100%')
    })

    it('draws no focus ring around a tapped slice', () => {
      const wrapper = mountPie()

      const marks = [
        ...wrapper.findAll('[data-testid="wedge"]'),
        ...wrapper.findAll('[data-testid="whole"]'),
      ]

      expect(marks.length).toBeGreaterThan(0)
      expect(marks.every((mark) => mark.classes().includes('wedge'))).toBe(true)
    })
  })
})
