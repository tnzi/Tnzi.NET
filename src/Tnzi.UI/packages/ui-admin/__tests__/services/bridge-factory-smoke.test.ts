import { describe, it, expect, vi } from 'vitest'
import type { HttpClient } from '@tnzi/core/http'
import { createChannelsBridge } from '../../src/services/bridges/channels-bridge'
import { createDiagnosticsBridge } from '../../src/services/bridges/diagnostics-bridge'
import { createInvoiceBridge } from '../../src/services/bridges/invoice-bridge'
import { createLocalizationBridge } from '../../src/services/bridges/localization-bridge'
import { createLoggingBridge } from '../../src/services/bridges/logging-bridge'
import { createLoginSecurityBridge } from '../../src/services/bridges/login-security-bridge'
import { createPaymentStatisticsBridge } from '../../src/services/bridges/payment-statistics-bridge'
import { createPerformanceBridge } from '../../src/services/bridges/performance-bridge'
import { createPermissionBridge } from '../../src/services/bridges/permission-bridge'
import { createPromotionBridge } from '../../src/services/bridges/promotion-bridge'
import { createSandboxBridge } from '../../src/services/bridges/sandbox-bridge'
import { createSignalRBridge } from '../../src/services/bridges/signalr-bridge'

/**
 * Until 2026-09-12 twelve of the twenty-nine bridge factories had no test that
 * executed them: every page test `vi.mock`s the whole bridge, so a factory could
 * throw at construction, or hand a refused envelope back as `undefined`, and
 * nothing in the suite would notice. The source-scan gate
 * (`bridge-write-unwrap.test.ts`) covers the shape of each call; this file
 * covers the runtime contract by constructing each factory against a client
 * that refuses everything and asserting what a page would see.
 *
 * Writes must reject with the server's reason. Reads are allowed to degrade
 * to the tolerant default the contract declares (`[]` / `null`) - that is the
 * documented read-side rule - but they must not throw a TypeError and must
 * not hand back the raw envelope.
 */

const REFUSAL = 'Refused by the server for this test'

/** Every method resolves a failed envelope - the shape HttpClient produces for a 4xx/5xx. */
function refusingClient(): HttpClient {
  const refused = () => ({ data: null, succeeded: false, success: false, code: 403, message: REFUSAL })
  return {
    get: vi.fn(async () => refused()),
    post: vi.fn(async () => refused()),
    put: vi.fn(async () => refused()),
    patch: vi.fn(async () => refused()),
    delete: vi.fn(async () => refused()),
    download: vi.fn(async () => ({ ...refused(), data: undefined })),
  } as unknown as HttpClient
}

describe('bridge factories that no page test executes', () => {
  const client = refusingClient()

  describe('writes reject with the server reason on a refused envelope', () => {
    const writes: Array<[string, () => Promise<unknown>]> = [
      ['diagnostics.exceptions.clear', () => createDiagnosticsBridge({ client }).exceptions.clear()],
      ['invoice.send', () => createInvoiceBridge({ client }).send('i1', 'a@b.c')],
      ['invoice.markAsPaid', () => createInvoiceBridge({ client }).markAsPaid('i1', {} as never)],
      ['invoice.cancel', () => createInvoiceBridge({ client }).cancel('i1', 'duplicate')],
      ['localization.clearMissing', () => createLocalizationBridge({ client }).clearMissing()],
      ['localization.exportMissing', () => createLocalizationBridge({ client }).exportMissing('fr')],
      // Not a write, but a read whose declared result is non-optional: the page
      // has nothing to render without it, so the server's reason must reach it.
      ['logging.logFiles.getLevels', () => createLoggingBridge({ client }).logFiles.getLevels()],
      ['performance.clear', () => createPerformanceBridge({ client }).clear()],
      ['permission.createPersistedRule', () => createPermissionBridge({ client }).createPersistedRule({} as never)],
      ['permission.updatePersistedRule', () => createPermissionBridge({ client }).updatePersistedRule('r1', {} as never)],
      ['permission.deletePersistedRule', () => createPermissionBridge({ client }).deletePersistedRule('r1')],
      ['promotion.create', () => createPromotionBridge({ client }).create({ code: 'X' })],
      ['promotion.update', () => createPromotionBridge({ client }).update('p1', { code: 'X' })],
      ['promotion.deactivate', () => createPromotionBridge({ client }).deactivate('p1')],
      ['signalr.disconnectUser', () => createSignalRBridge({ client }).disconnectUser('u1')],
    ]

    it.each(writes)('%s', async (_name, call) => {
      await expect(call()).rejects.toThrow(REFUSAL)
    })
  })

  describe('reads degrade to the declared tolerant default, never to the raw envelope', () => {
    const reads: Array<[string, () => Promise<unknown>, unknown]> = [
      ['channels.channels.getAdapters', () => createChannelsBridge({ client }).channels.getAdapters(), []],
      ['channels.channels.getStatus', () => createChannelsBridge({ client }).channels.getStatus(), null],
      ['channels.gateway.getConnections', () => createChannelsBridge({ client }).gateway.getConnections(), []],
      ['diagnostics.modules.list', () => createDiagnosticsBridge({ client }).modules.list(), []],
      ['loginSecurity.getOverview', () => createLoginSecurityBridge({ client }).getOverview(), null],
      ['loginSecurity.getFrequentFailures', () => createLoginSecurityBridge({ client }).getFrequentFailures(), []],
      ['paymentStatistics.getOverview', () => createPaymentStatisticsBridge({ client }).getOverview(), null],
      ['paymentStatistics.getRevenueTrend', () => createPaymentStatisticsBridge({ client }).getRevenueTrend('2026-01-01', '2026-01-31', 'day' as never), []],
      ['performance.getEndpoints', () => createPerformanceBridge({ client }).getEndpoints(), []],
      ['permission.getPersistedRules', () => createPermissionBridge({ client }).getPersistedRules(), []],
      ['sandbox.getStatus', () => createSandboxBridge({ client }).getStatus(), null],
      ['signalr.getOnlineUsers', () => createSignalRBridge({ client }).getOnlineUsers(), []],
      ['signalr.isUserOnline', () => createSignalRBridge({ client }).isUserOnline('u1'), false],
    ]

    it.each(reads)('%s', async (_name, call, expected) => {
      const result = await call()
      expect(result).toEqual(expected)
      // The envelope itself must never leak through as if it were data.
      if (result && typeof result === 'object') expect(result).not.toHaveProperty('succeeded')
    })
  })

  it('every factory can be built without a client (pages mount in tests that way)', () => {
    const factories: Array<[string, () => unknown]> = [
      ['channels', () => createChannelsBridge()],
      ['diagnostics', () => createDiagnosticsBridge()],
      ['invoice', () => createInvoiceBridge()],
      ['localization', () => createLocalizationBridge()],
      ['logging', () => createLoggingBridge()],
      ['loginSecurity', () => createLoginSecurityBridge()],
      ['paymentStatistics', () => createPaymentStatisticsBridge()],
      ['performance', () => createPerformanceBridge()],
      ['permission', () => createPermissionBridge()],
      ['promotion', () => createPromotionBridge()],
      ['sandbox', () => createSandboxBridge()],
      ['signalr', () => createSignalRBridge()],
    ]
    for (const [name, build] of factories) expect(build, name).not.toThrow()
  })

  it('a client-less write rejects instead of resolving as if it had been sent', async () => {
    await expect(createInvoiceBridge().send('i1')).rejects.toThrow()
    await expect(createPromotionBridge().deactivate('p1')).rejects.toThrow()
    await expect(createPermissionBridge().deletePersistedRule('r1')).rejects.toThrow()
    await expect(createSignalRBridge().disconnectUser('u1')).rejects.toThrow()
  })
})
