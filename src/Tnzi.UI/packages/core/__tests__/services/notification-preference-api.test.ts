import { describe, it, expect, vi } from 'vitest'
import {
  useAdminNotificationPreferenceApi,
  useNotificationPreferenceApi,
} from '../../src/services/notification/api'
import type { SetNotificationPreferenceDto } from '../../src/services/notification/types'
import { isInvalidTimeZoneError, NOTIFICATION_INVALID_TIME_ZONE } from '../../src/services/notification/metadata'
import { HttpError } from '../../src/errors/api-error'

function mockClient() {
  return {
    get: vi.fn(async () => ({ success: true, code: 200, data: [] })),
    post: vi.fn(async () => ({ success: true, code: 200, data: undefined })),
    put: vi.fn(async () => ({ success: true, code: 200, data: {} })),
    delete: vi.fn(async () => ({ success: true, code: 200, data: undefined })),
  }
}

describe('useNotificationPreferenceApi (self-service)', () => {
  it('getMine reads the caller\'s own rows from GET /notification/preferences', async () => {
    const c = mockClient(); const api = useNotificationPreferenceApi(c as never)
    await api.getMine()
    expect(c.get).toHaveBeenCalledWith('/notification/preferences')
  })

  it('set upserts through PUT /notification/preferences with no user id anywhere', async () => {
    const c = mockClient(); const api = useNotificationPreferenceApi(c as never)
    const body: SetNotificationPreferenceDto = {
      channel: 'Email',
      isEnabled: true,
      quietHoursStart: '22:00:00',
      quietHoursEnd: '07:00:00',
      quietHoursTimeZone: 'America/Toronto',
      maxFrequencyPerHour: 5,
    }
    await api.set(body)
    // The user is the session's: a user id in the path or body would let the
    // client name somebody else.
    expect(c.put).toHaveBeenCalledWith('/notification/preferences', body)
  })

  it('delete addresses the row id', async () => {
    const c = mockClient(); const api = useNotificationPreferenceApi(c as never)
    await api.delete('p-1')
    expect(c.delete).toHaveBeenCalledWith('/notification/preferences/p-1')
  })

  it('resetToDefault posts to /notification/preferences/reset', async () => {
    const c = mockClient(); const api = useNotificationPreferenceApi(c as never)
    await api.resetToDefault()
    expect(c.post).toHaveBeenCalledWith('/notification/preferences/reset')
  })

  it('never touches the admin routes', async () => {
    const c = mockClient(); const api = useNotificationPreferenceApi(c as never)
    await api.getMine()
    await api.set({ channel: 'Sms', isEnabled: false })
    await api.delete('p-1')
    await api.resetToDefault()
    const urls = [c.get, c.put, c.delete, c.post].flatMap((fn) => fn.mock.calls.map((call) => String((call as unknown[])[0])))
    expect(urls.some((u) => u.startsWith('/admin'))).toBe(false)
  })
})

describe('useAdminNotificationPreferenceApi', () => {
  it('setPreference passes the time zone through to the admin upsert', async () => {
    const c = mockClient(); const api = useAdminNotificationPreferenceApi(c as never)
    const body: SetNotificationPreferenceDto = { channel: 'Push', isEnabled: true, quietHoursTimeZone: 'Asia/Tokyo' }
    await api.setPreference('u-1', body)
    expect(c.put).toHaveBeenCalledWith('/admin/notification-preferences/user/u-1', body)
  })
})

describe('isInvalidTimeZoneError', () => {
  it('recognises the refusal on a thrown HttpError, a failed envelope and a wrapped cause', () => {
    const envelope = { succeeded: false, success: false, code: 400, message: 'Unknown time zone', errorCode: NOTIFICATION_INVALID_TIME_ZONE }
    expect(isInvalidTimeZoneError(new HttpError(envelope as never))).toBe(true)
    expect(isInvalidTimeZoneError(envelope)).toBe(true)
    expect(isInvalidTimeZoneError({ cause: { errorCode: NOTIFICATION_INVALID_TIME_ZONE } })).toBe(true)
  })

  it('does not claim any other failure', () => {
    expect(isInvalidTimeZoneError(new Error('Unknown time zone'))).toBe(false)
    expect(isInvalidTimeZoneError({ errorCode: 'NOTIFICATION_ERROR' })).toBe(false)
    expect(isInvalidTimeZoneError(null)).toBe(false)
  })
})
