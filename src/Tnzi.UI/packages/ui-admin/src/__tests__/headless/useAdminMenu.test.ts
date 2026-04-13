import { describe, it, expect } from 'vitest'
import { useAdminMenu, type AdminMenuItem } from '../../headless/useAdminMenu'

const sampleItems: AdminMenuItem[] = [
  { key: 'dashboard', label: 'Dashboard', path: '/dashboard' },
  { key: 'users', label: 'Users', path: '/users', permission: 'admin.users' },
  { key: 'settings', label: 'Settings', path: '/settings', hidden: true },
  {
    key: 'content',
    label: 'Content',
    children: [
      { key: 'articles', label: 'Articles', path: '/articles', permission: 'content.articles' },
      { key: 'media', label: 'Media', path: '/media' },
    ],
  },
]

describe('useAdminMenu', () => {
  it('initializes with empty activeKey and openKeys', () => {
    const menu = useAdminMenu({ items: sampleItems })
    expect(menu.activeKey.value).toBe('')
    expect(menu.openKeys.value).toEqual([])
  })

  it('filters hidden items', () => {
    const menu = useAdminMenu({ items: sampleItems })
    const keys = menu.visibleItems.value.map((i) => i.key)
    expect(keys).not.toContain('settings')
    expect(keys).toContain('dashboard')
  })

  it('filters items by permission', () => {
    const menu = useAdminMenu({ items: sampleItems, hasPermission: () => false })
    const keys = menu.visibleItems.value.map((i) => i.key)
    expect(keys).not.toContain('users')
    expect(keys).toContain('dashboard')
  })

  it('shows items when permission is granted', () => {
    const menu = useAdminMenu({ items: sampleItems, hasPermission: () => true })
    const keys = menu.visibleItems.value.map((i) => i.key)
    expect(keys).toContain('users')
  })

  it('filters nested children by permission', () => {
    const menu = useAdminMenu({
      items: sampleItems,
      hasPermission: (p) => p !== 'content.articles',
    })
    const content = menu.visibleItems.value.find((i) => i.key === 'content')
    expect(content?.children?.map((c) => c.key)).not.toContain('articles')
    expect(content?.children?.map((c) => c.key)).toContain('media')
  })

  it('setActive updates activeKey', () => {
    const menu = useAdminMenu({ items: sampleItems })
    menu.setActive('users')
    expect(menu.activeKey.value).toBe('users')
  })

  it('toggleOpen opens a key', () => {
    const menu = useAdminMenu({ items: sampleItems })
    menu.toggleOpen('content')
    expect(menu.openKeys.value).toContain('content')
  })

  it('toggleOpen closes an already open key', () => {
    const menu = useAdminMenu({ items: sampleItems })
    menu.toggleOpen('content')
    menu.toggleOpen('content')
    expect(menu.openKeys.value).not.toContain('content')
  })

  it('toggleOpen is immutable (no mutation)', () => {
    const menu = useAdminMenu({ items: sampleItems })
    menu.toggleOpen('dashboard')
    const snapshot = menu.openKeys.value
    menu.toggleOpen('users')
    expect(menu.openKeys.value).not.toBe(snapshot)
  })

  it('findItemByPath finds top-level item', () => {
    const menu = useAdminMenu({ items: sampleItems })
    const item = menu.findItemByPath('/dashboard')
    expect(item?.key).toBe('dashboard')
  })

  it('findItemByPath finds nested item', () => {
    const menu = useAdminMenu({ items: sampleItems })
    const item = menu.findItemByPath('/articles')
    expect(item?.key).toBe('articles')
  })

  it('findItemByPath returns undefined for unknown path', () => {
    const menu = useAdminMenu({ items: sampleItems })
    expect(menu.findItemByPath('/unknown')).toBeUndefined()
  })

  it('works with no items', () => {
    const menu = useAdminMenu({ items: [] })
    expect(menu.visibleItems.value).toEqual([])
    expect(menu.findItemByPath('/anything')).toBeUndefined()
  })
})
