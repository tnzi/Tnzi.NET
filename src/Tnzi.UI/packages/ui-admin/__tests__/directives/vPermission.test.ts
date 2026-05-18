import { describe, it, expect, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { defineComponent, h } from 'vue'
import { createPinia, setActivePinia } from 'pinia'
import { vPermission } from '../../src/directives/vPermission'
import { useAdminAuthStore } from '../../src/stores/useAdminAuthStore'

function makeHost(permission: string | string[], modifier: '' | 'any' | 'hide' = '') {
  return defineComponent({
    directives: { permission: vPermission },
    setup() {
      return () =>
        h('div', { class: 'host' }, [
          modifier === 'any'
            ? h('span', { class: 'guarded', ['v-permission.any']: permission }, 'x')
            : modifier === 'hide'
              ? h('span', { class: 'guarded', ['v-permission.hide']: permission }, 'x')
              : h('span', { class: 'guarded', ['v-permission']: permission }, 'x'),
        ])
    },
  })
}

function seedAuth(opts: { isSuperUser?: boolean; permissions?: string[] }) {
  const auth = useAdminAuthStore()
  auth.userInfo = {
    id: '1',
    username: 'u',
    displayName: 'u',
    roles: [],
    permissions: opts.permissions ?? [],
  }
  if (opts.isSuperUser !== undefined) {
    auth.isSuperUser = opts.isSuperUser
  }
}

// Render the directive via a programmatic test fixture (DirectiveBinding has
// no template form when using setup-render functions, so we drive vPermission
// directly).
function probe(value: string | string[], modifier: 'any' | 'hide' | undefined): HTMLElement {
  const el = document.createElement('div')
  el.textContent = 'x'
  document.body.appendChild(el)
  vPermission.mounted!(el, {
    value,
    modifiers: modifier ? { [modifier]: true } : {},
  } as never, null as never, null as never)
  return el
}

describe('vPermission directive', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    document.body.innerHTML = ''
  })

  it('shows element when user has the permission', () => {
    seedAuth({ permissions: ['user.delete'] })
    const el = probe('user.delete', undefined)
    expect(el.style.display).toBe('')
  })

  it('hides element when user lacks the permission (display:none)', () => {
    seedAuth({ permissions: ['user.view'] })
    const el = probe('user.delete', undefined)
    expect(el.style.display).toBe('none')
  })

  it('uses visibility:hidden when .hide modifier is set', () => {
    seedAuth({ permissions: [] })
    const el = probe('user.delete', 'hide')
    expect(el.style.visibility).toBe('hidden')
  })

  it('superuser bypass — always allowed', () => {
    seedAuth({ isSuperUser: true, permissions: [] })
    const el = probe('user.delete', undefined)
    expect(el.style.display).toBe('')
  })

  it('array value requires ALL permissions by default', () => {
    seedAuth({ permissions: ['a'] })
    const el = probe(['a', 'b'], undefined)
    expect(el.style.display).toBe('none')
  })

  it('.any modifier requires only ONE permission', () => {
    seedAuth({ permissions: ['a'] })
    const el = probe(['a', 'b'], 'any')
    expect(el.style.display).toBe('')
  })

  it('reflects subsequent updates via updated() hook', () => {
    seedAuth({ permissions: [] })
    const el = probe('a', undefined)
    expect(el.style.display).toBe('none')

    seedAuth({ permissions: ['a'] })
    vPermission.updated!(el, {
      value: 'a',
      modifiers: {},
    } as never, null as never, null as never)
    expect(el.style.display).toBe('')
  })
})
