import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { resetDialogAdapter } from '@tnzi/core/adapters'

const installAppUpdate = vi.fn((options: unknown) => ({ options }))
vi.mock('@tnzi/core/app-update', () => ({ installAppUpdate: (o: unknown) => installAppUpdate(o) }))

import { installAdminAppUpdate, confirmAdminAppUpdate } from '../../src/plugin/app-update-config'

type DialogOptions = Record<string, unknown> & {
  onPositiveClick?: () => void
  onNegativeClick?: () => void
}

/** Stand-in for the naive dialog API that `TAdminWindowHandles` publishes as `window.$dialog`. */
function publishDialogHandle() {
  const shown: DialogOptions[] = []
  const show = (options: DialogOptions) => {
    shown.push(options)
  }
  ;(window as unknown as { $dialog?: unknown }).$dialog = {
    info: show,
    warning: show,
    success: show,
    error: show,
    create: show,
  }
  return shown
}

describe('installAdminAppUpdate', () => {
  beforeEach(() => {
    installAppUpdate.mockClear()
    resetDialogAdapter()
  })

  afterEach(() => {
    delete (window as unknown as { $dialog?: unknown }).$dialog
    vi.restoreAllMocks()
  })

  it('is on by default and hands the router through', () => {
    const router = { beforeEach: vi.fn(), onError: vi.fn(), resolve: vi.fn() }
    installAdminAppUpdate(undefined, router as never)
    expect(installAppUpdate).toHaveBeenCalledTimes(1)
    const options = installAppUpdate.mock.calls[0][0] as Record<string, unknown>
    expect(options.router).toBe(router)
    expect(options.prompt).toBe(confirmAdminAppUpdate)
  })

  it('installs chunk recovery even without a router', () => {
    installAdminAppUpdate(undefined)
    expect(installAppUpdate).toHaveBeenCalledTimes(1)
  })

  it('does nothing when switched off', () => {
    expect(installAdminAppUpdate(false)).toBeNull()
    expect(installAppUpdate).not.toHaveBeenCalled()
  })

  it('lets the consumer override mode and prompt', () => {
    const prompt = vi.fn(async () => false)
    installAdminAppUpdate({ mode: 'prompt', prompt, checkInterval: 0 })
    const options = installAppUpdate.mock.calls[0][0] as Record<string, unknown>
    expect(options).toMatchObject({ mode: 'prompt', prompt, checkInterval: 0 })
  })

  it('asks with the admin dialog handle and English fallbacks', async () => {
    const shown = publishDialogHandle()
    const answer = confirmAdminAppUpdate()
    expect(shown).toHaveLength(1)
    expect(shown[0]).toMatchObject({
      title: 'New version available',
      positiveText: 'Reload',
      negativeText: 'Later',
    })
    expect(String(shown[0].content)).toContain('new version')
    shown[0].onPositiveClick?.()
    await expect(answer).resolves.toBe(true)
  })

  it('reports "Later" as a refusal, so the page is not reloaded', async () => {
    const shown = publishDialogHandle()
    const answer = confirmAdminAppUpdate()
    shown[0].onNegativeClick?.()
    await expect(answer).resolves.toBe(false)
  })

  // The admin shell never installs anything into core's dialog slot, so a
  // prompt routed through core `useDialog()` reached the console fallback,
  // which used to answer yes without showing anything: 'prompt' mode reloaded
  // the page unasked and discarded unsaved input.
  it('never reloads unasked when no dialog handle is published', async () => {
    // happy-dom has no native confirm; a browser always does.
    const nativeConfirm = vi.fn(() => false)
    const w = window as unknown as { confirm?: unknown }
    const original = w.confirm
    w.confirm = nativeConfirm
    try {
      await expect(confirmAdminAppUpdate()).resolves.toBe(false)
      expect(nativeConfirm).toHaveBeenCalledTimes(1)
    } finally {
      w.confirm = original
    }
  })
})
