import { describe, it, expect } from 'vitest'
import type { HttpClient } from '@tnzi/core/http'
import { buildOAuthProviders } from '../../../src/headless/auth/oauth-providers'

// Minimal HttpClient stub - only `resolveUrl` is used by oauthLoginUrl.
function stubClient(): HttpClient {
  return {
    resolveUrl: (url: string, params?: Record<string, unknown> | object) =>
      params ? `/api${url}?${new URLSearchParams(params as Record<string, string>)}` : `/api${url}`,
  } as unknown as HttpClient
}

describe('buildOAuthProviders', () => {
  it('maps known providers to their brand icon + color + label', () => {
    const out = buildOAuthProviders(
      [
        { provider: 'github', displayName: 'GitHub' },
        { provider: 'google', displayName: 'Google' },
      ],
      stubClient(),
    )
    expect(out).toHaveLength(2)
    expect(out[0]).toMatchObject({ key: 'github', icon: 'mdi:github', color: '#181717', label: 'GitHub' })
    expect(out[1]).toMatchObject({ key: 'google', icon: 'mdi:google', label: 'Google' })
    expect(typeof out[0]!.onClick).toBe('function')
  })

  it('falls back to a generic icon (no color) for unknown providers', () => {
    const out = buildOAuthProviders([{ provider: 'weibo', displayName: 'Weibo' }], stubClient())
    expect(out[0]).toMatchObject({ key: 'weibo', icon: 'mdi:login-variant', label: 'Weibo' })
    expect(out[0]!.color).toBeUndefined()
  })

  it('returns an empty array for no providers', () => {
    expect(buildOAuthProviders([], stubClient())).toEqual([])
  })
})
