import { describe, it, expect, vi, beforeEach } from 'vitest'

const installAppUpdate = vi.fn((options: unknown) => ({ options }))
vi.mock('@tnzi/core/app-update', () => ({ installAppUpdate: (o: unknown) => installAppUpdate(o) }))

const confirm = vi.fn(async () => true)
vi.mock('@tnzi/core/adapters', () => ({ useDialog: () => ({ confirm }) }))

import { installAdminAppUpdate, confirmAdminAppUpdate } from '../../src/plugin/app-update-config'

describe('installAdminAppUpdate', () => {
  beforeEach(() => {
    installAppUpdate.mockClear()
    confirm.mockClear()
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

  it('asks with the dialog adapter and English fallbacks', async () => {
    await expect(confirmAdminAppUpdate()).resolves.toBe(true)
    expect(confirm).toHaveBeenCalledWith(
      expect.stringContaining('new version'),
      expect.objectContaining({ title: 'New version available', confirmText: 'Reload', cancelText: 'Later' }),
    )
  })
})
