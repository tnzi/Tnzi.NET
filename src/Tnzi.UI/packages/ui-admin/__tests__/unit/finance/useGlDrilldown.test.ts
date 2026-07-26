import { describe, it, expect } from 'vitest'
import { useGlDrilldown } from '../../../src/headless/useGlDrilldown'
import type { FinanceBridge, GeneralLedgerReportDto } from '../../../src/services/bridges/finance-bridge'

function report(tag: string, totalCount: number): GeneralLedgerReportDto {
  return {
    lines: { items: [{ memo: tag }], totalCount, pageIndex: 1, pageSize: 25, totalPages: 1 },
  } as unknown as GeneralLedgerReportDto
}

/**
 * A bridge whose responses resolve in an order we control, so "slow request
 * started first" is deterministic instead of a timing accident.
 */
function deferredBridge() {
  const resolvers: Array<(r: GeneralLedgerReportDto) => void> = []
  const bridge = {
    reports: {
      generalLedger: () =>
        new Promise<GeneralLedgerReportDto>((resolve) => {
          resolvers.push(resolve)
        }),
    },
  } as unknown as FinanceBridge
  return { bridge, resolvers }
}

const period = () => ({ from: '2026-01-01', to: '2026-12-31' })

describe('useGlDrilldown', () => {
  /**
   * Type a keyword, then immediately page. The keyword request is slower, so it
   * lands last; without a sequence token it overwrites the page-2 result while
   * `pageIndex` still reads 2 - the rows and the pager then disagree, which is
   * exactly what makes a reader stop trusting the numbers.
   */
  it('ignores a stale response that resolves after a newer one', async () => {
    const { bridge, resolvers } = deferredBridge()
    const drill = useGlDrilldown({ bridge, period })

    const first = drill.openFor({ accountId: 'a1' })
    resolvers[0]!(report('initial', 1))
    await first

    const slow = drill.search('coffee')
    const fast = drill.goToPage(2)

    // The newer request answers first, the stale one afterwards.
    resolvers[2]!(report('page-2', 2))
    await fast
    resolvers[1]!(report('stale-search', 99))
    await slow

    expect(drill.report.value?.lines.items[0]?.memo).toBe('page-2')
    expect(drill.total.value).toBe(2)
    expect(drill.pageIndex.value).toBe(2)
    expect(drill.loading.value).toBe(false)
  })

  it('does not let a stale rejection clear a good page', async () => {
    const { bridge, resolvers } = deferredBridge()
    const drill = useGlDrilldown({ bridge, period })

    const first = drill.openFor({ accountId: 'a1' })
    resolvers[0]!(report('initial', 1))
    await first

    const slow = drill.search('coffee')
    const fast = drill.goToPage(2)

    resolvers[2]!(report('page-2', 2))
    await fast
    // The stale request fails; it must not blank the page the user is looking at.
    resolvers[1]!(Promise.reject(new Error('boom')) as unknown as GeneralLedgerReportDto)
    await slow

    expect(drill.report.value?.lines.items[0]?.memo).toBe('page-2')
    expect(drill.error.value).toBeNull()
  })
})
