import { describe, it, expect } from 'vitest'
import {
  isValidSnapshot,
  parseSnapshot,
  snapshotToJson,
  type AdminThemeSnapshot,
} from '../../src/theme/admin-config'

function fixture(): AdminThemeSnapshot {
  return {
    version: 1,
    exportedAt: '2026-05-15T00:00:00.000Z',
    admin: {
      layoutMode: 'vertical',
      headerVisible: true,
      tabVisible: true,
      footerVisible: true,
      breadcrumbVisible: true,
      siderWidth: 220,
      siderCollapsedWidth: 64,
      mixSiderWidth: 80,
      headerHeight: 56,
      tabHeight: 44,
      tabStyle: 'chrome',
      pageTransition: 'fade',
      pageAnimate: true,
      invertSider: false,
      fixedHeader: true,
      fixedTab: true,
      fixedFooter: false,
      watermark: {
        enabled: false,
        text: 'Tnzi Admin',
        includeUserName: true,
        includeDate: true,
        opacity: 0.15,
        fontSize: 16,
      },
    },
    ui: {
      mode: 'light',
      colors: { primary: '#2080F0' },
    },
  }
}

describe('admin-config snapshot helpers', () => {
  it('snapshotToJson + parseSnapshot round-trips losslessly', () => {
    const original = fixture()
    const json = snapshotToJson(original)
    const reparsed = parseSnapshot(json)
    expect(reparsed).toEqual(original)
  })

  it('parseSnapshot throws on invalid JSON', () => {
    expect(() => parseSnapshot('{ not json')).toThrow()
  })

  it('parseSnapshot rejects snapshots with wrong version', () => {
    const bad = { ...fixture(), version: 99 }
    expect(() => parseSnapshot(JSON.stringify(bad))).toThrow(/version/i)
  })

  it('parseSnapshot rejects snapshots missing admin section', () => {
    const bad = { version: 1, ui: { mode: 'light', colors: {} } }
    expect(() => parseSnapshot(JSON.stringify(bad))).toThrow()
  })

  it('isValidSnapshot returns false for null / primitive / wrong shape', () => {
    expect(isValidSnapshot(null)).toBe(false)
    expect(isValidSnapshot('')).toBe(false)
    expect(isValidSnapshot(42)).toBe(false)
    expect(isValidSnapshot({ version: 1 })).toBe(false)
    expect(isValidSnapshot({ version: 2, admin: {}, ui: {} })).toBe(false)
    expect(isValidSnapshot({ version: 1, admin: {}, ui: {} })).toBe(true)
  })

  it('snapshotToJson produces stable indented output', () => {
    const json = snapshotToJson(fixture())
    expect(json).toMatch(/\n {2}"version": 1,/)
    expect(json).toMatch(/"layoutMode": "vertical"/)
  })
})
