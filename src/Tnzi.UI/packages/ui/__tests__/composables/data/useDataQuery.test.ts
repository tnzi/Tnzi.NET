import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { ref } from 'vue'
import { useDataQuery } from '../../../src/composables/data/useDataQuery'

describe('useDataQuery', () => {
  beforeEach(() => {
    vi.useFakeTimers()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('initial state is empty', () => {
    const params = ref('')
    const queryFn = vi.fn().mockResolvedValue('result')
    const q = useDataQuery({ params, queryFn, immediate: false })
    expect(q.data.value).toBeNull()
    expect(q.loading.value).toBe(false)
    expect(q.error.value).toBeNull()
  })

  it('runs immediately by default (after default debounce)', async () => {
    const params = ref('a')
    const queryFn = vi.fn().mockResolvedValue('R')
    const q = useDataQuery({ params, queryFn })
    // default debounce 300ms
    await vi.advanceTimersByTimeAsync(300)
    expect(queryFn).toHaveBeenCalledWith('a')
    expect(q.data.value).toBe('R')
  })

  it('debounces param changes — only last value wins', async () => {
    const params = ref('a')
    const queryFn = vi.fn(async (p: string) => `res-${p}`)
    const q = useDataQuery({ params, queryFn, debounce: 100, immediate: false })
    params.value = 'b'
    await vi.advanceTimersByTimeAsync(50)
    params.value = 'c'
    await vi.advanceTimersByTimeAsync(50)
    params.value = 'd'
    await vi.advanceTimersByTimeAsync(100)
    // Only last should execute
    expect(queryFn).toHaveBeenCalledTimes(1)
    expect(queryFn).toHaveBeenCalledWith('d')
    expect(q.data.value).toBe('res-d')
  })

  it('discards stale responses on race', async () => {
    const params = ref('a')
    let resolveFirst: (v: string) => void = () => {}
    const queryFn = vi.fn()
      .mockImplementationOnce(() => new Promise<string>((r) => { resolveFirst = r }))
      .mockImplementationOnce(async () => 'second')
    const q = useDataQuery({ params, queryFn, debounce: 0 })
    await vi.advanceTimersByTimeAsync(0)
    // First in-flight; now trigger second
    params.value = 'b'
    await vi.advanceTimersByTimeAsync(0)
    // Second resolves first (it's sync-ish), then first resolves late with stale value
    resolveFirst('first-late')
    await vi.advanceTimersByTimeAsync(0)
    // Second result should be retained
    expect(q.data.value).toBe('second')
  })

  it('handles queryFn rejection and sets error', async () => {
    const onError = vi.fn()
    const params = ref('a')
    const queryFn = vi.fn().mockRejectedValue(new Error('fail'))
    const q = useDataQuery({ params, queryFn, debounce: 0, onError })
    await vi.advanceTimersByTimeAsync(0)
    expect(q.error.value?.message).toBe('fail')
    expect(onError).toHaveBeenCalled()
    expect(q.loading.value).toBe(false)
  })

  it('wraps non-Error rejection', async () => {
    const params = ref('a')
    const queryFn = vi.fn().mockRejectedValue('oops')
    const q = useDataQuery({ params, queryFn, debounce: 0 })
    await vi.advanceTimersByTimeAsync(0)
    expect(q.error.value?.message).toBe('oops')
  })

  it('immediate=false defers first run until params change', async () => {
    const params = ref('a')
    const queryFn = vi.fn().mockResolvedValue('R')
    useDataQuery({ params, queryFn, debounce: 0, immediate: false })
    await vi.advanceTimersByTimeAsync(0)
    expect(queryFn).not.toHaveBeenCalled()
    params.value = 'b'
    await vi.advanceTimersByTimeAsync(0)
    expect(queryFn).toHaveBeenCalledWith('b')
  })
})
