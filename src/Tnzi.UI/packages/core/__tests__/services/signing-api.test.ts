import { describe, it, expect, vi } from 'vitest'
import { useAdminSigningApi, useSigningRecipientApi } from '../../src/services/signing/api'
import {
  EnvelopeStatus,
  SigningRecipientStatus,
  TemplateSource,
  isTerminalEnvelopeStatus,
} from '../../src/services/signing/metadata'

function mockClient() {
  return {
    get: vi.fn(async () => ({ success: true, code: 200, data: null })),
    post: vi.fn(async () => ({ success: true, code: 200, data: null })),
    put: vi.fn(async () => ({ success: true, code: 200, data: null })),
    delete: vi.fn(async () => ({ success: true, code: 200, data: undefined })),
  }
}

describe('useAdminSigningApi', () => {
  it('lists requests against the admin route with the query as params', async () => {
    const c = mockClient()
    await useAdminSigningApi(c as never).getRequests({
      pageIndex: 1,
      pageSize: 20,
      status: EnvelopeStatus.Sent,
    })
    expect(c.get).toHaveBeenCalledWith('/admin/signing/requests', {
      params: { pageIndex: 1, pageSize: 20, status: 'Sent' },
    })
  })

  it('sends a request by posting to its send sub-route', async () => {
    const c = mockClient()
    await useAdminSigningApi(c as never).sendRequest('req-1')
    expect(c.post).toHaveBeenCalledWith('/admin/signing/requests/req-1/send')
  })

  it('voids a request by posting to its void sub-route', async () => {
    const c = mockClient()
    await useAdminSigningApi(c as never).voidRequest('req-1')
    expect(c.post).toHaveBeenCalledWith('/admin/signing/requests/req-1/void')
  })

  it('reaches templates on their own base, not under requests', async () => {
    const c = mockClient()
    const api = useAdminSigningApi(c as never)
    await api.getTemplates()
    await api.getTemplate('tpl-1')
    await api.deleteTemplate('tpl-1')
    expect(c.get).toHaveBeenCalledWith('/admin/signing/templates', { params: undefined })
    expect(c.get).toHaveBeenCalledWith('/admin/signing/templates/tpl-1')
    expect(c.delete).toHaveBeenCalledWith('/admin/signing/templates/tpl-1')
  })
})

describe('useSigningRecipientApi', () => {
  it('addresses the anonymous route by token, without an admin prefix', async () => {
    const c = mockClient()
    await useSigningRecipientApi(c as never).getPacket('tok-abc')
    expect(c.get).toHaveBeenCalledWith('/signing/tok-abc')
  })

  it('encodes the token into the path', async () => {
    // Tokens are URL-safe base64 today, but the path segment is the only thing
    // standing between a stray character and a broken lookup.
    const c = mockClient()
    await useSigningRecipientApi(c as never).getPacket('a/b+c')
    expect(c.get).toHaveBeenCalledWith('/signing/a%2Fb%2Bc')
  })

  it('declines with an explicit null reason rather than omitting the body', async () => {
    const c = mockClient()
    await useSigningRecipientApi(c as never).decline('tok-abc')
    expect(c.post).toHaveBeenCalledWith('/signing/tok-abc/decline', { reason: null })
  })
})

/**
 * The backend registers a global JsonStringEnumConverter, so every enum that
 * appears on a RESPONSE arrives as its member name. A numeric mirror would make
 * `dto.status === EnvelopeStatus.Sent` silently never match.
 */
describe('signing wire enums', () => {
  it('EnvelopeStatus mirrors the backend member names', () => {
    expect(Object.values(EnvelopeStatus)).toEqual([
      'Draft',
      'Sent',
      'InProgress',
      'Completed',
      'Declined',
      'Expired',
      'Voided',
    ])
  })

  it('SigningRecipientStatus mirrors the backend member names', () => {
    expect(Object.values(SigningRecipientStatus)).toEqual([
      'Pending',
      'Sent',
      'Viewed',
      'Signed',
      'Declined',
    ])
  })

  it('TemplateSource mirrors the backend member names', () => {
    expect(TemplateSource.Composed).toBe('Composed')
    expect(TemplateSource.Uploaded).toBe('Uploaded')
  })

  it('matches a raw request payload without coercion', () => {
    const wire = JSON.parse('{"status":"InProgress","recipients":[{"status":"Viewed"}]}')
    expect(wire.status).toBe(EnvelopeStatus.InProgress)
    expect(wire.recipients[0].status).toBe(SigningRecipientStatus.Viewed)
  })
})

describe('isTerminalEnvelopeStatus', () => {
  it('treats expiry as terminal even though it is derived, not stored', () => {
    expect(isTerminalEnvelopeStatus(EnvelopeStatus.Expired)).toBe(true)
  })

  it('leaves requests that can still move as non-terminal', () => {
    expect(isTerminalEnvelopeStatus(EnvelopeStatus.Draft)).toBe(false)
    expect(isTerminalEnvelopeStatus(EnvelopeStatus.Sent)).toBe(false)
    expect(isTerminalEnvelopeStatus(EnvelopeStatus.InProgress)).toBe(false)
  })

  it.each([EnvelopeStatus.Completed, EnvelopeStatus.Declined, EnvelopeStatus.Voided])(
    'treats %s as terminal',
    (status) => {
      expect(isTerminalEnvelopeStatus(status)).toBe(true)
    },
  )
})
