import { describe, it, expect } from 'vitest'
import { getLocaleMessages, setLocaleMessages, loadLocaleMessages } from '../../src/i18n/messages'
import { translatePageKey } from '../../src/i18n/translate'

/**
 * The global setup file preloads both dictionaries, which is what every other
 * test wants - but it also means the "chunk has not landed yet" state is never
 * exercised anywhere else. These cover the registry directly.
 */
describe('locale message registry', () => {
  it('reports a locale that was never registered as absent', () => {
    expect(getLocaleMessages('de' as never)).toBeUndefined()
  })

  it('falls back to the humanised key while a dictionary is missing', () => {
    // Not "throws" and not "renders the raw dotted key": a miss must degrade to
    // something a human can read, because that is also what a genuinely absent
    // translation does.
    expect(translatePageKey('', 'admin.modules.nosuch.loginLogs')).toBe('Login Logs')
  })

  it('loadLocaleMessages resolves and installs the dictionary', async () => {
    await loadLocaleMessages('en')
    const messages = getLocaleMessages('en') as Record<string, unknown>
    expect(messages).toBeDefined()
    expect(messages.admin).toBeDefined()
  })

  it('is idempotent - a second load for the same locale is a no-op', async () => {
    await loadLocaleMessages('en')
    const first = getLocaleMessages('en')
    await loadLocaleMessages('en')
    expect(getLocaleMessages('en')).toBe(first)
  })

  it('setLocaleMessages makes a dictionary available synchronously', () => {
    setLocaleMessages('zh-cn', { admin: { crud: { create: '新建' } } })
    expect(getLocaleMessages('zh-cn')).toEqual({ admin: { crud: { create: '新建' } } })
  })
})
