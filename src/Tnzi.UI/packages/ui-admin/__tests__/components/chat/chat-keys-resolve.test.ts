import { describe, it, expect } from 'vitest'
import { en } from '../../../src/locales/en'

/**
 * Every literal key a chat component translates is in the dictionary.
 *
 * The chat components translate through `translatePageKey('chat', key)`, which
 * humanises a miss instead of failing ("presence.myStatus" reads as "My
 * status"). The humanised English looks right in English and stays English in
 * every other locale, so a missing key is invisible until someone switches
 * language. This reads the component sources and resolves each literal key the
 * way the translator does.
 */
const sources = import.meta.glob('../../../src/components/chat/*.vue', { query: '?raw', import: 'default', eager: true }) as Record<string, string>

function lookup(path: string): unknown {
  return path.split('.').reduce<unknown>((node, k) => (node && typeof node === 'object' ? (node as Record<string, unknown>)[k] : undefined), en)
}

describe('chat component keys', () => {
  it('reads the chat component sources', () => {
    expect(Object.keys(sources).length).toBeGreaterThan(10)
  })

  it('resolves every literal t(...) key under admin.modules.chat', () => {
    const missing: string[] = []
    for (const [file, src] of Object.entries(sources)) {
      for (const m of src.matchAll(/\bt\(\s*'([^']+)'/g)) {
        const key = m[1]!
        const path = key.startsWith('admin.') ? key : `admin.modules.chat.${key}`
        if (typeof lookup(path) !== 'string') missing.push(`${file.split('/').pop()}: ${key}`)
      }
    }
    expect(missing).toEqual([])
  })
})
