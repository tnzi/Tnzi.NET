import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import TThemeSwitcher from '../../../src/components/control/TThemeSwitcher.vue'
import TLocaleSwitch from '../../../src/components/control/TLocaleSwitch.vue'

describe('TThemeSwitcher', () => {
  it('renders with initial light mode', () => {
    const wrapper = mount(TThemeSwitcher, { props: { modelValue: 'light' } })
    expect(wrapper.find('button').exists()).toBe(true)
  })

  it('emits update:modelValue when cycling through light → dark → auto → light', async () => {
    const wrapper = mount(TThemeSwitcher, { props: { modelValue: 'light' } })
    await wrapper.find('button').trigger('click')
    expect(wrapper.emitted('update:modelValue')?.[0]).toEqual(['dark'])

    await wrapper.setProps({ modelValue: 'dark' })
    await wrapper.find('button').trigger('click')
    expect(wrapper.emitted('update:modelValue')?.[1]).toEqual(['auto'])

    await wrapper.setProps({ modelValue: 'auto' })
    await wrapper.find('button').trigger('click')
    expect(wrapper.emitted('update:modelValue')?.[2]).toEqual(['light'])
  })

  it('renders sun icon for light mode', () => {
    const wrapper = mount(TThemeSwitcher, { props: { modelValue: 'light' } })
    expect(wrapper.find('.t-theme-switcher__icon--light').exists()).toBe(true)
  })

  it('renders moon icon for dark mode', () => {
    const wrapper = mount(TThemeSwitcher, { props: { modelValue: 'dark' } })
    expect(wrapper.find('.t-theme-switcher__icon--dark').exists()).toBe(true)
  })

  it('renders auto icon for auto mode', () => {
    const wrapper = mount(TThemeSwitcher, { props: { modelValue: 'auto' } })
    expect(wrapper.find('.t-theme-switcher__icon--auto').exists()).toBe(true)
  })

  it('has aria-label for accessibility', () => {
    const wrapper = mount(TThemeSwitcher, { props: { modelValue: 'light' } })
    expect(wrapper.find('button').attributes('aria-label')).toBeTruthy()
  })
})

describe('TLocaleSwitch', () => {
  it('renders current locale', () => {
    const wrapper = mount(TLocaleSwitch, {
      props: {
        modelValue: 'en',
        options: [
          { value: 'en', label: 'English' },
          { value: 'zh-cn', label: '中文' },
        ],
      },
    })
    expect(wrapper.text()).toContain('English')
  })

  it('emits update:modelValue when option selected', async () => {
    const wrapper = mount(TLocaleSwitch, {
      props: {
        modelValue: 'en',
        options: [
          { value: 'en', label: 'English' },
          { value: 'zh-cn', label: '中文' },
        ],
      },
    })
    await wrapper.find('.t-locale-switch__trigger').trigger('click')
    const option = wrapper.findAll('.t-locale-switch__option')[1]
    if (option) {
      await option.trigger('click')
      expect(wrapper.emitted('update:modelValue')?.[0]).toEqual(['zh-cn'])
    }
  })

  it('applies globe icon prefix', () => {
    const wrapper = mount(TLocaleSwitch, {
      props: {
        modelValue: 'en',
        options: [{ value: 'en', label: 'English' }],
      },
    })
    expect(wrapper.find('.t-locale-switch__icon').exists()).toBe(true)
  })
})
