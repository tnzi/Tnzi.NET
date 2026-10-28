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

  it('derives the chat hub when the consumer omits the chat option (chat is on by default)', () => {
    // An omitted `chat` is the default chat, enabled: the shell provides
    // `options.chat ?? {}` and only `enabled: false` turns the launcher off.
    // Leaving it undefined here sent the chat hub to the root-relative path
    // the apiBase exists to replace.
    const r = resolveHubConfigs('/api', undefined, undefined)
    expect(r.chat).toEqual({ hubUrl: '/api/hubs/chat' })
    expect(r.settings).toEqual({ hubUrl: '/api/hubs/settings' })
  })

  it('a disabled chat stays disabled when its hub URL is derived', () => {
    const r = resolveHubConfigs('/api', { enabled: false }, undefined)
    expect(r.chat).toEqual({ enabled: false, hubUrl: '/api/hubs/chat' })
  })

  it('no apiBase and no chat option leaves chat undefined', () => {
    expect(resolveHubConfigs(undefined, undefined, undefined).chat).toBeUndefined()
  })
})
