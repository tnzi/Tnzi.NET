import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { THEME_CONTEXT_KEY, createThemeContext, mergeThemeSettings } from '@tnzi/ui'
import THeaderBanner from '../../../src/components/dashboard/THeaderBanner.vue'
import TProjectTimeline from '../../../src/components/dashboard/TProjectTimeline.vue'
import TDashboardPage from '../../../src/components/pages/TDashboardPage.vue'

function themeProvide() {
  const ctx = createThemeContext(mergeThemeSettings({}))
  return { [THEME_CONTEXT_KEY as unknown as symbol]: ctx }
}

describe('THeaderBanner', () => {
  it('renders default time-of-day greeting + username', () => {
    const wrapper = mount(THeaderBanner, {
      props: { userName: 'admin' },
      global: { provide: themeProvide() },
    })
    const text = wrapper.find('.t-header-banner__greeting').text()
    expect(text).toContain(', admin')
    // Must match one of the four fallbacks.
    expect(/Good (morning|afternoon|evening)|Working late/.test(text)).toBe(true)
  })

  it('honors `greeting` override', () => {
    const wrapper = mount(THeaderBanner, {
      props: { userName: 'admin', greeting: 'Welcome' },
      global: { provide: themeProvide() },
    })
    expect(wrapper.find('.t-header-banner__greeting').text()).toBe('Welcome, admin')
  })

  it('hides the time line when hideTime=true', () => {
    const wrapper = mount(THeaderBanner, {
      props: { hideTime: true },
      global: { provide: themeProvide() },
    })
    expect(wrapper.find('.t-header-banner__time').exists()).toBe(false)
  })

  it('renders subtitle when supplied', () => {
    const wrapper = mount(THeaderBanner, {
      props: { subtitle: 'Welcome back to the dashboard' },
      global: { provide: themeProvide() },
    })
    expect(wrapper.find('.t-header-banner__subtitle').text()).toBe(
      'Welcome back to the dashboard',
    )
  })
})

describe('TProjectTimeline', () => {
  it('renders timeline items when supplied', () => {
    const wrapper = mount(TProjectTimeline, {
      props: {
        items: [
          { key: '1', title: 'Deploy v1.0', tone: 'success', time: '10:00' },
          { key: '2', title: 'Bug fix', tone: 'warning', time: '11:30' },
        ],
      },
      global: { provide: themeProvide() },
    })
    expect(wrapper.find('.t-project-timeline').exists()).toBe(true)
    expect(wrapper.find('.t-project-timeline__empty').exists()).toBe(false)
  })

  it('shows empty state when no items', () => {
    const wrapper = mount(TProjectTimeline, {
      global: { provide: themeProvide() },
    })
    expect(wrapper.find('.t-project-timeline__empty').text()).toBe('No recent activity')
  })

  it('uses translate function for empty label', () => {
    const wrapper = mount(TProjectTimeline, {
      props: { translate: (k: string) => `[T:${k}]` },
      global: { provide: themeProvide() },
    })
    expect(wrapper.find('.t-project-timeline__empty').text()).toBe('[T:admin.timeline.empty]')
  })
})

describe('TDashboardPage KPI gradient', () => {
  it('renders gradient KPI card with white text when gradient supplied', () => {
    const wrapper = mount(TDashboardPage, {
      props: {
        kpis: [
          {
            key: 'visits',
            title: 'Visits',
            value: 1024,
            icon: 'mdi:eye',
            gradient: { start: '#ec4786', end: '#b955a4' },
          },
        ],
      },
      global: { provide: themeProvide() },
    })
    expect(wrapper.find('.t-dashboard-page__kpi-card--gradient').exists()).toBe(true)
  })

  it('renders unit prefix when supplied', () => {
    const wrapper = mount(TDashboardPage, {
      props: {
        kpis: [
          { key: 'rev', title: 'Revenue', value: 1024, icon: 'mdi:cash', unit: '$' },
        ],
      },
      global: { provide: themeProvide() },
    })
    expect(wrapper.find('.t-dashboard-page__kpi-unit').text()).toBe('$')
  })

  it('renders the header slot', () => {
    const wrapper = mount(TDashboardPage, {
      slots: { header: '<div class="my-banner">Hello</div>' },
      global: { provide: themeProvide() },
    })
    expect(wrapper.find('.my-banner').exists()).toBe(true)
  })
})
