import { describe, it, expect } from 'vitest'
import { en } from '../../src/locales/en'
import { zhCn } from '../../src/locales/zh-cn'
import { moduleLabels } from '../../src/locales/module-labels'

describe('locales', () => {
  it('en has admin.* namespace', () => {
    expect(en.admin.crud.create).toBe('Create')
    expect(en.admin.crud.refresh).toBe('Refresh')
    expect(en.admin.tabs.closeCurrent).toBeTruthy()
    expect(en.admin.theme.title).toBe('Theme')
    expect(en.admin.search.placeholder).toBeTruthy()
  })

  it('zh-cn has same key structure as en', () => {
    function keys(obj: Record<string, unknown>, prefix = ''): string[] {
      return Object.entries(obj).flatMap(([k, v]) => {
        const full = prefix ? `${prefix}.${k}` : k
        return typeof v === 'object' && v !== null ? keys(v as Record<string, unknown>, full) : [full]
      })
    }
    expect(keys(en).sort()).toEqual(keys(zhCn).sort())
  })

  it('moduleLabels covers 9 modules in both locales', () => {
    const modules = ['identity', 'authorization', 'storage', 'system', 'audit', 'notification', 'chat', 'payment', 'template']
    for (const m of modules) {
      expect(moduleLabels.en[m]).toBeTruthy()
      expect(moduleLabels['zh-cn'][m]).toBeTruthy()
    }
  })
})
