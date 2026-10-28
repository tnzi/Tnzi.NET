/**
 * `disableRemoteIcons` makes registered icons the only source: `@iconify/vue`
 * stops asking the public Iconify API for anything it does not have.
 *
 * Asserted on the call rather than on behaviour, deliberately: the test setup
 * already installs a module that never fetches (so a test cannot leak a
 * request), which means "no request went out" would hold with or without the
 * function. What has to hold is that the function replaces the default
 * provider's module with one that prepares no query at all.
 */
import { afterEach, describe, expect, it, vi } from 'vitest'
import { _api } from '@iconify/vue'
import { disableRemoteIcons } from '../../src/icons'

afterEach(() => vi.restoreAllMocks())

describe('disableRemoteIcons', () => {
  it('replaces the default provider module with one that never queries the API', () => {
    const setModule = vi.spyOn(_api, 'setAPIModule')
    disableRemoteIcons()

    expect(setModule).toHaveBeenCalledTimes(1)
    const [provider, module] = setModule.mock.calls[0]!
    expect(provider).toBe('')
    expect(module.prepare('', 'lucide', ['house', 'x'])).toEqual([])
    const callback = vi.fn()
    module.send('https://api.iconify.design', { type: 'icons', provider: '', prefix: 'lucide', icons: ['house'] }, callback)
    expect(callback).not.toHaveBeenCalled()
  })
})
