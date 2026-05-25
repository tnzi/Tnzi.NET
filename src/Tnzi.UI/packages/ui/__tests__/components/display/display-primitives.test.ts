import { describe, it, expect, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { nextTick } from 'vue'
import TSvgIcon from '../../../src/components/display/TSvgIcon.vue'
import TCountTo from '../../../src/components/display/TCountTo.vue'
import TWaveBg from '../../../src/components/display/TWaveBg.vue'

describe('TSvgIcon', () => {
  it('renders Iconify when icon prop is set', () => {
    const wrapper = mount(TSvgIcon, { props: { icon: 'mdi:home' } })
    // @iconify/vue renders an <svg> root
    expect(wrapper.find('svg').exists()).toBe(true)
  })

  it('renders local <use href> when localIcon is set', () => {
    const wrapper = mount(TSvgIcon, { props: { localIcon: 'logo' } })
    const useEl = wrapper.find('use')
    expect(useEl.exists()).toBe(true)
    expect(useEl.attributes('href')).toBe('#logo')
  })

  it('renders nothing when neither prop is provided', () => {
    const wrapper = mount(TSvgIcon, { props: {} })
    expect(wrapper.find('svg').exists()).toBe(false)
  })

  it('applies size as both width and height', () => {
    const wrapper = mount(TSvgIcon, { props: { localIcon: 'x', size: 24 } })
    const style = wrapper.find('svg').attributes('style') ?? ''
    expect(style).toContain('width: 24px')
    expect(style).toContain('height: 24px')
  })

  it('accepts string size like "1.5em"', () => {
    const wrapper = mount(TSvgIcon, { props: { localIcon: 'x', size: '1.5em' } })
    const style = wrapper.find('svg').attributes('style') ?? ''
    expect(style).toContain('width: 1.5em')
  })
})

describe('TCountTo', () => {
  it('renders starting value when autoplay disabled', () => {
    const wrapper = mount(TCountTo, {
      props: { startValue: 100, endValue: 200, autoplay: false },
    })
    expect(wrapper.find('.t-count-to__value').text()).toBe('100')
  })

  it('emits start/end with autoplay', async () => {
    vi.useFakeTimers()
    const wrapper = mount(TCountTo, {
      props: { startValue: 0, endValue: 50, duration: 100, autoplay: true },
    })
    // Allow microtasks; rAF is mocked via fake timers
    await nextTick()
    expect(wrapper.emitted('start')?.[0]).toEqual([0])
    vi.useRealTimers()
  })

  it('formats with separator + decimal + prefix + suffix', () => {
    const wrapper = mount(TCountTo, {
      props: {
        startValue: 1234567.89,
        endValue: 1234567.89,
        decimals: 2,
        separator: ',',
        decimal: '.',
        prefix: '$',
        suffix: ' USD',
        autoplay: false,
      },
    })
    expect(wrapper.find('.t-count-to__value').text()).toBe('1,234,567.89')
    expect(wrapper.find('.t-count-to__prefix').text()).toBe('$')
    expect(wrapper.find('.t-count-to__suffix').text()).toBe('USD')
  })

  it('disables thousands separator when separator is empty', () => {
    const wrapper = mount(TCountTo, {
      props: {
        startValue: 1000,
        endValue: 1000,
        separator: '',
        autoplay: false,
      },
    })
    expect(wrapper.find('.t-count-to__value').text()).toBe('1000')
  })

  it('exposes start/pause via defineExpose', () => {
    const wrapper = mount(TCountTo, {
      props: { startValue: 0, endValue: 10, autoplay: false },
    })
    const vm = wrapper.vm as unknown as { start: () => void; pause: () => void }
    expect(typeof vm.start).toBe('function')
    expect(typeof vm.pause).toBe('function')
  })
})

describe('TWaveBg', () => {
  // Phase I.7.2 rewrite: TWaveBg is now a soybean-parity SVG with two
  // organic blob paths (top-right + bottom-left) gradient-filled from the
  // theme primary. The old height/reverse/opacity props are gone.

  it('renders two SVG blobs', () => {
    const wrapper = mount(TWaveBg)
    expect(wrapper.findAll('svg')).toHaveLength(2)
  })

  it('marks itself aria-hidden', () => {
    const wrapper = mount(TWaveBg)
    expect(wrapper.attributes('aria-hidden')).toBe('true')
  })

  it('produces gradient stops derived from themeColor prop', () => {
    const wrapper = mount(TWaveBg, { props: { themeColor: '#646cff' } })
    const stops = wrapper.findAll('stop')
    expect(stops.length).toBeGreaterThanOrEqual(4)
    for (const stop of stops) {
      const color = stop.attributes('stop-color') ?? ''
      // Each stop should be a 7-character hex string the palette helper produced.
      expect(color).toMatch(/^#[0-9a-f]{6}$/i)
    }
  })
})
