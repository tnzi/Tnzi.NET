import { describe, it, expect, beforeEach, vi } from 'vitest'
import { flushPromises } from '@vue/test-utils'
import { createVantThemeAdapter } from '../src/adapters/theme'
import { createVantDialogAdapter } from '../src/adapters/dialog'

describe('createVantThemeAdapter', () => {
  beforeEach(() => {
    document.documentElement.className = ''
  })

  it('flips the Vant dark class on the document root', () => {
    const adapter = createVantThemeAdapter()

    adapter.applyTheme('dark')
    expect(document.documentElement.classList.contains('van-theme-dark')).toBe(true)
    expect(document.documentElement.classList.contains('dark')).toBe(true)
    expect(adapter.getResolvedTheme()).toBe('dark')

    adapter.applyTheme('light')
    expect(document.documentElement.classList.contains('van-theme-dark')).toBe(false)
    expect(document.documentElement.classList.contains('van-theme-light')).toBe(true)
    expect(adapter.getResolvedTheme()).toBe('light')
  })

  it('resolves auto mode against the media query', () => {
    const matchMedia = vi.fn().mockReturnValue({ matches: true, addEventListener: vi.fn(), removeEventListener: vi.fn() })
    vi.stubGlobal('matchMedia', matchMedia)

    createVantThemeAdapter().applyTheme('auto')

    expect(document.documentElement.classList.contains('van-theme-dark')).toBe(true)
    vi.unstubAllGlobals()
  })

  it('sets the Vant primary color variable', () => {
    createVantThemeAdapter().setPrimaryColor?.('rgb(7, 193, 96)')
    expect(document.documentElement.style.getPropertyValue('--van-primary-color')).toBe('rgb(7, 193, 96)')
  })
})

describe('createVantDialogAdapter', () => {
  it('prompts through a Vant dialog field rather than window.prompt', async () => {
    const windowPrompt = vi.fn()
    vi.stubGlobal('prompt', windowPrompt)

    const adapter = createVantDialogAdapter()
    const pending = adapter.prompt('Your name?')
    await flushPromises()

    const input = document.querySelector('.van-dialog input') as HTMLInputElement | null
    expect(input).not.toBeNull()

    input!.value = 'alice'
    input!.dispatchEvent(new Event('input'))
    await flushPromises()

    const confirmButton = document.querySelector('.van-dialog__confirm') as HTMLElement | null
    expect(confirmButton).not.toBeNull()
    confirmButton!.click()
    await flushPromises()

    await expect(pending).resolves.toBe('alice')
    expect(windowPrompt).not.toHaveBeenCalled()
    vi.unstubAllGlobals()
  })

  it('tags severity dialogs with a class that has styling behind it', async () => {
    const adapter = createVantDialogAdapter()
    void adapter.alert('Boom', { type: 'error' })
    await flushPromises()

    expect(document.querySelector('.van-dialog')?.className).toContain('t-dialog-error')
  })
})
