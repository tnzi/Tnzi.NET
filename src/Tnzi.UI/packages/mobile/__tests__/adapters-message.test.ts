import { describe, it, expect, vi, beforeEach } from 'vitest'

// Vant merges toast options with Object.assign, so a key that is present with
// the value `undefined` overrides a default just like a real value would - and
// the Toast prop then falls back to Vue's default (2000 ms). These tests pin
// what the adapter hands to Vant, key by key.
vi.mock('vant', async (importOriginal) => {
  const actual = await importOriginal<typeof import('vant')>()
  return {
    ...actual,
    showToast: vi.fn(() => ({ close: vi.fn() })),
    showSuccessToast: vi.fn(() => ({ close: vi.fn() })),
    showFailToast: vi.fn(() => ({ close: vi.fn() })),
    showLoadingToast: vi.fn(() => ({ close: vi.fn() })),
  }
})

import { showLoadingToast, showSuccessToast, showToast } from 'vant'
import { createVantMessageAdapter } from '../src/adapters/message'

const loadingMock = vi.mocked(showLoadingToast)
const successMock = vi.mocked(showSuccessToast)
const toastMock = vi.mocked(showToast)

function lastCallOptions(mock: { mock: { calls: unknown[][] } }): Record<string, unknown> {
  const calls = mock.mock.calls
  return calls[calls.length - 1]![0] as Record<string, unknown>
}

describe('createVantMessageAdapter', () => {
  beforeEach(() => {
    loadingMock.mockClear()
    successMock.mockClear()
    toastMock.mockClear()
  })

  it('keeps a loading toast open until the caller closes it, even when options are passed', () => {
    const adapter = createVantMessageAdapter()
    adapter.loading('Uploading', { closable: false })

    const opts = lastCallOptions(loadingMock)
    expect(opts.duration).toBe(0)
    expect(opts.closeOnClick).toBe(false)
    expect(opts.forbidClick).toBe(true)
  })

  it('keeps a loading toast open when an empty options object is passed', () => {
    createVantMessageAdapter().loading('Working', {})
    expect(lastCallOptions(loadingMock).duration).toBe(0)
  })

  it('lets the caller shorten a loading toast explicitly', () => {
    createVantMessageAdapter().loading('Brief', { duration: 800 })
    expect(lastCallOptions(loadingMock).duration).toBe(800)
  })

  it('never passes an undefined duration or closeOnClick key to Vant', () => {
    const adapter = createVantMessageAdapter()
    adapter.success('Saved', {})
    adapter.info('Note', { closable: true })

    const success = lastCallOptions(successMock)
    expect('duration' in success).toBe(false)
    expect('closeOnClick' in success).toBe(false)

    const info = lastCallOptions(toastMock)
    expect('duration' in info).toBe(false)
    expect(info.closeOnClick).toBe(true)
  })

  it('returns a close handle bound to the toast instance', () => {
    const close = vi.fn()
    loadingMock.mockReturnValueOnce({ close } as unknown as ReturnType<typeof showLoadingToast>)
    const stop = createVantMessageAdapter().loading('x')
    stop()
    expect(close).toHaveBeenCalledTimes(1)
  })
})
