import { describe, it, expect, vi } from 'vitest'
import { defineComponent, h } from 'vue'
import { mount } from '@vue/test-utils'
import {
  provideAdminShellActions,
  useAdminShellActions,
} from '../../src/headless/admin-shell-actions'

const Consumer = defineComponent({
  setup() {
    const shell = useAdminShellActions()
    return () =>
      h('div', [
        h('button', { class: 'search', onClick: () => shell.openSearch() }),
        h('button', { class: 'theme', onClick: () => shell.openThemeDrawer() }),
      ])
  },
})

describe('useAdminShellActions', () => {
  it('reaches the shell that provided them, through the default slot', () => {
    const openSearch = vi.fn()
    const openThemeDrawer = vi.fn()

    const Shell = defineComponent({
      setup(_, { slots }) {
        provideAdminShellActions({ openSearch, openThemeDrawer })
        // Two levels down: the desktop taskbar is not a direct child of the
        // shell, which is why this is provide/inject and not a prop.
        return () => h('div', [h('div', [slots.default?.()])])
      },
    })

    const wrapper = mount(Shell, { slots: { default: () => h(Consumer) } })

    wrapper.find('.search').trigger('click')
    wrapper.find('.theme').trigger('click')

    expect(openSearch).toHaveBeenCalledOnce()
    expect(openThemeDrawer).toHaveBeenCalledOnce()
  })

  it('no-ops outside a shell instead of throwing', () => {
    // A component that offers a search button is still renderable in a story or
    // a bare unit test; a dead button beats a mount that explodes.
    const wrapper = mount(Consumer)
    expect(() => wrapper.find('.search').trigger('click')).not.toThrow()
  })
})
