/**
 * The plan form's create / edit requests as they go on the wire: the real
 * write mapper, the real bridge and the real `@tnzi/core` admin API, with only
 * the HTTP client faked (it records the URL and the JSON body).
 *
 * The page tests mock the bridge, so nothing caught that a new plan posted
 * `"id": ""`: the server's Guid cannot bind an empty string, the whole body
 * fails to bind, and every create from the UI answered 400 "The dto field is
 * required.".
 */
import { describe, it, expect, vi } from 'vitest'
import { useAdminSubscriptionApi } from '@tnzi/core/services/payment'
import { createSubscriptionPlanBridge } from '../../../src/services/bridges/subscription-plan-bridge'
import { planCreateDefaults, toPlanWrite } from '../../../src/pages/payment/subscription-plan-config'

const ok = <T>(data: T) => ({ succeeded: true, success: true, code: 200, data, message: null })

function recordingClient() {
  const sent: { method: string; url: string; body: unknown }[] = []
  const record = (method: string) =>
    vi.fn(async (url: string, body?: unknown) => {
      // What fetch would send: the body serialised to JSON and read back
      sent.push({ method, url, body: body === undefined ? undefined : JSON.parse(JSON.stringify(body)) })
      return ok(method === 'POST' ? { ...(body as object), id: 'plan-new' } : method === 'GET' ? { currency: 'CAD' } : null)
    })
  const client = { get: record('GET'), post: record('POST'), put: record('PUT'), delete: record('DELETE') }
  return { client, sent }
}

describe('subscription plan requests on the wire', () => {
  it('creates a plan without an id key, so the server can bind the body', async () => {
    const { client, sent } = recordingClient()
    const bridge = createSubscriptionPlanBridge({ adminSubscriptionApi: useAdminSubscriptionApi(client as never) })

    // What the create form holds: defaults, then what the operator typed. The form's record carries an empty id.
    await bridge.create(toPlanWrite({ ...planCreateDefaults, id: '', currency: 'CAD', planCode: 'FIRM-M', planName: 'Firm', price: 100 }))

    const post = sent.find((r) => r.method === 'POST')!
    expect(post.url).toBe('/admin/subscriptions/plans')
    expect(post.body).not.toHaveProperty('id')
    expect(post.body).toMatchObject({ planCode: 'FIRM-M', planName: 'Firm', price: 100, currency: 'CAD', cycleType: 'Month', cycleValue: 1, isActive: true })
  })

  it('edits a plan with its id in the path and the body', async () => {
    const { client, sent } = recordingClient()
    const bridge = createSubscriptionPlanBridge({ adminSubscriptionApi: useAdminSubscriptionApi(client as never) })
    const id = '3fa85f64-5717-4562-b3fc-2c963f66afa6'

    await bridge.update(id, toPlanWrite({ planName: 'Firm Plus' }, { id, planCode: 'FIRM-M', planName: 'Firm', price: 100, currency: 'CAD', cycleType: 'Month' as never, cycleValue: 1, sortOrder: 2, isActive: true }))

    const put = sent.find((r) => r.method === 'PUT')!
    expect(put.url).toBe(`/admin/subscriptions/plans/${id}`)
    expect(put.body).toMatchObject({ id, planName: 'Firm Plus', currency: 'CAD', sortOrder: 2 })
  })

  it('reads the default currency for a new plan from the server', async () => {
    const { client, sent } = recordingClient()
    const bridge = createSubscriptionPlanBridge({ adminSubscriptionApi: useAdminSubscriptionApi(client as never) })

    expect(await bridge.getDefaultCurrency()).toBe('CAD')
    expect(sent[0]).toMatchObject({ method: 'GET', url: '/admin/subscriptions/plans/defaults' })
    expect(planCreateDefaults).not.toHaveProperty('currency')
  })
})
