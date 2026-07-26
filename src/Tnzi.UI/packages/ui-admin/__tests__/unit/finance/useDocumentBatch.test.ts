import { describe, it, expect, vi } from 'vitest'
import { ref } from 'vue'
import { useDocumentBatch } from '../../../src/pages/finance/useDocumentBatch'
import type { FinanceDocRow } from '../../../src/pages/finance/document-config'

/**
 * 批量过账的三条行为约束（见 useDocumentBatch 的 doc comment）。
 */
const ITEMS: FinanceDocRow[] = [
  { id: 'd1', number: null, status: 'Draft' as never },
  { id: 'd2', number: null, status: 'Draft' as never },
  { id: 'p1', number: 'INV-000001', status: 'Posted' as never },
  { id: 'v1', number: 'INV-000002', status: 'Voided' as never },
]

function setup(post: (id: string) => Promise<unknown>) {
  const message = { success: vi.fn(), error: vi.fn(), warning: vi.fn() }
  const refresh = vi.fn(async () => undefined)
  const clearSelection = vi.fn()
  const batch = useDocumentBatch({
    items: ref(ITEMS),
    post,
    translate: (key, params) => `${key}${params ? ':' + JSON.stringify(params) : ''}`,
    message,
    refresh,
    clearSelection,
  })
  return { batch, message, refresh, clearSelection }
}

describe('useDocumentBatch', () => {
  /** 用户通常先筛后全选，选中集合里混着已过账的——对它们调 post 只会收获一串 409。 */
  it('only posts the drafts in the selection', async () => {
    const post = vi.fn(async () => undefined)
    const { batch, clearSelection } = setup(post)

    await batch.postMany(['d1', 'd2', 'p1', 'v1'])

    expect(post).toHaveBeenCalledTimes(2)
    expect(post.mock.calls.map((c) => c[0])).toEqual(['d1', 'd2'])
    expect(clearSelection).toHaveBeenCalled()
  })

  it('reports how many of the selection are actually postable', () => {
    const { batch } = setup(async () => undefined)
    expect(batch.countFor(['d1', 'd2', 'p1'])).toBe(2)
    expect(batch.countFor(['p1', 'v1'])).toBe(0)
  })

  /** 过账要分配连续单据号——并行会在号段行锁上互相等待。 */
  it('posts serially, not in parallel', async () => {
    let inFlight = 0
    let maxConcurrent = 0
    const post = vi.fn(async () => {
      inFlight++
      maxConcurrent = Math.max(maxConcurrent, inFlight)
      await new Promise((r) => setTimeout(r, 5))
      inFlight--
    })
    const { batch } = setup(post)

    await batch.postMany(['d1', 'd2'])

    expect(maxConcurrent).toBe(1)
  })

  /**
   * ★批量操作最糟的失败模式是"看起来成功了，其实少了两张"——那两张要到对账时
   * 才被发现。部分失败必须报出来，且带上是哪几张。
   */
  it('surfaces partial failure instead of reporting success', async () => {
    const post = vi.fn(async (id: string) => {
      if (id === 'd2') throw new Error('period is closed')
      return undefined
    })
    const { batch, message } = setup(post)

    await batch.postMany(['d1', 'd2'])

    expect(message.success).not.toHaveBeenCalled()
    expect(message.error).toHaveBeenCalledTimes(1)
    const text = message.error.mock.calls[0]![0] as string
    expect(text).toContain('batch.partial')
    // 失败的那一张要被点名，否则用户不知道该去补哪一张。
    expect(text).toContain('period is closed')
  })

  it('refuses a selection with no drafts instead of firing doomed requests', async () => {
    const post = vi.fn(async () => undefined)
    const { batch, message } = setup(post)

    await batch.postMany(['p1', 'v1'])

    expect(post).not.toHaveBeenCalled()
    expect(message.error).toHaveBeenCalledWith('batch.noDrafts')
  })

  /** 一条抛错也要刷新 + 清选择，否则列表停在过期状态。 */
  it('still refreshes when a document fails', async () => {
    const { batch, refresh, clearSelection } = setup(async () => {
      throw new Error('boom')
    })

    await batch.postMany(['d1'])

    expect(refresh).toHaveBeenCalled()
    expect(clearSelection).toHaveBeenCalled()
  })
})
