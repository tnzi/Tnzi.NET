import { describe, it, expect } from 'vitest'
import { resolveHubConfigs } from '../../src/plugin/hub-config'
import type { AdminSettingsConfig } from '../../src/plugin/settings-config'

const settings = (over: Partial<AdminSettingsConfig> = {}): AdminSettingsConfig => ({ ...over })

describe('resolveHubConfigs', () => {
  it('no apiBase → configs unchanged (opt-in)', () => {
    const r = resolveHubConfigs(undefined, { enabled: true }, settings())
    expect(r.chat).toEqual({ enabled: true })
    expect(r.settings).toEqual({})
  })

  it('apiBase derives both chat and settings hub URLs', () => {
    const r = resolveHubConfigs('/api', { enabled: true }, undefined)
    expect(r.chat).toEqual({ enabled: true, hubUrl: '/api/hubs/chat' })
    expect(r.settings).toEqual({ hubUrl: '/api/hubs/settings' })
  })

  it('strips a trailing slash from apiBase', () => {
    const r = resolveHubConfigs('/api/', { enabled: true }, undefined)
    expect(r.chat?.hubUrl).toBe('/api/hubs/chat')
    expect(r.settings?.hubUrl).toBe('/api/hubs/settings')
  })

  it('explicit hubUrl wins over apiBase', () => {
    const r = resolveHubConfigs('/api', { hubUrl: '/custom/chat' }, settings({ hubUrl: '/custom/settings' }))
    expect(r.chat?.hubUrl).toBe('/custom/chat')
    expect(r.settings?.hubUrl).toBe('/custom/settings')
  })

  it('does NOT enable chat when the consumer did not opt in', () => {
    const r = resolveHubConfigs('/api', undefined, undefined)
    expect(r.chat).toBeUndefined() // a bare { hubUrl } would enable chat - must stay undefined
    expect(r.settings).toEqual({ hubUrl: '/api/hubs/settings' }) // settings hub runs regardless
  })
})
