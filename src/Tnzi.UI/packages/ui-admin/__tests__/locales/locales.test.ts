import { describe, it, expect } from 'vitest'
import { en } from '../../src/locales/en'
import { zhCn } from '../../src/locales/zh-cn'
import { moduleLabels } from '../../src/locales/module-labels'

describe('locales', () => {
  it('en has admin.* namespace', () => {
    expect(en.admin.crud.create).toBe('Create')
    expect(en.admin.crud.refresh).toBe('Refresh')
    expect(en.admin.tabs.closeCurrent).toBeTruthy()
    expect(en.admin.theme.title).toBe('Theme settings')
    expect(en.admin.theme.tabs.appearance).toBe('Appearance')
    expect(en.admin.theme.layout.topHybridHeaderFirst).toBeTruthy()
    expect(en.admin.theme.watermark.enabled).toBe('Show watermark')
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

  it('settings groups/fields dictionaries have identical keys across locales', () => {
    // Every runtime-setting group/field ships an i18nKey pointing here; a key
    // present in one locale but missing in the other silently falls back to the
    // humanised English key. This gate keeps the two settings dictionaries in
    // lock-step so a new group/field can't ship half-translated.
    const enSettings = (en.admin.modules.system.settings ?? {}) as Record<string, Record<string, string>>
    const zhSettings = (zhCn.admin.modules.system.settings ?? {}) as Record<string, Record<string, string>>
    for (const dict of ['groups', 'fields'] as const) {
      expect(Object.keys(enSettings[dict] ?? {}).sort()).toEqual(Object.keys(zhSettings[dict] ?? {}).sort())
    }
  })

  it('moduleLabels covers 9 modules in both locales', () => {
    const modules = ['identity', 'authorization', 'storage', 'system', 'audit', 'notification', 'chat', 'payment', 'template']
    for (const m of modules) {
      expect(moduleLabels.en[m]).toBeTruthy()
      expect(moduleLabels['zh-cn'][m]).toBeTruthy()
    }
  })
})
