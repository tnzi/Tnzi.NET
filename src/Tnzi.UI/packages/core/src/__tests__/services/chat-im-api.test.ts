import { describe, it, expect, vi } from 'vitest'
import { useChatImApi, useChatBroadcastApi, usePresenceApi } from '../../services/chat/api'
import { UserPresenceStatus } from '../../services/chat/types'

function mockClient() {
  return {
    get: vi.fn(async () => ({ success: true, code: 200, data: [] })),
    post: vi.fn(async () => ({ success: true, code: 200, data: {} })),
    put: vi.fn(async () => ({ success: true, code: 200, data: {} })),
    delete: vi.fn(async () => ({ success: true, code: 200, data: undefined })),
  }
}

describe('useChatImApi', () => {
  it('getConversations hits GET /conversations', async () => {
    const c = mockClient(); const api = useChatImApi(c as never)
    await api.getConversations()
    expect(c.get).toHaveBeenCalledWith('/conversations')
  })
  it('sendMessage posts to /conversations/{id}/messages', async () => {
    const c = mockClient(); const api = useChatImApi(c as never)
    await api.sendMessage('cid', { contentType: 1, content: 'hi' } as never)
    expect(c.post).toHaveBeenCalledWith('/conversations/cid/messages', { contentType: 1, content: 'hi' })
  })
  it('getMessages passes before+limit params', async () => {
    const c = mockClient(); const api = useChatImApi(c as never)
    await api.getMessages('cid', { before: 'm1', limit: 30 })
    expect(c.get).toHaveBeenCalledWith('/conversations/cid/messages', { params: { before: 'm1', limit: 30 } })
  })
  it('searchContacts hits /chat/contacts/search', async () => {
    const c = mockClient(); const api = useChatImApi(c as never)
    await api.searchContacts('al')
    expect(c.get).toHaveBeenCalledWith('/chat/contacts/search', { params: { keyword: 'al' } })
  })
  it('updateMemberSettings puts to /conversations/{id}/member-settings with data', async () => {
    const c = mockClient(); const api = useChatImApi(c as never)
    await api.updateMemberSettings('cid', { isMuted: true, remark: 'note' })
    expect(c.put).toHaveBeenCalledWith('/conversations/cid/member-settings', { isMuted: true, remark: 'note' })
  })
  it('clearHistory posts to /conversations/{id}/clear', async () => {
    const c = mockClient(); const api = useChatImApi(c as never)
    await api.clearHistory('cid')
    expect(c.post).toHaveBeenCalledWith('/conversations/cid/clear')
  })
  it('searchMessages hits GET /conversations/{id}/messages/search with params', async () => {
    const c = mockClient(); const api = useChatImApi(c as never)
    await api.searchMessages('cid', { keyword: 'hello', before: 'm1', limit: 20 })
    expect(c.get).toHaveBeenCalledWith('/conversations/cid/messages/search', { params: { keyword: 'hello', before: 'm1', limit: 20 } })
  })
  it('updateNotice puts to /conversations/{id}/notice with notice body', async () => {
    const c = mockClient(); const api = useChatImApi(c as never)
    await api.updateNotice('cid', 'new notice')
    expect(c.put).toHaveBeenCalledWith('/conversations/cid/notice', { notice: 'new notice' })
  })
  it('updateNotice supports null notice', async () => {
    const c = mockClient(); const api = useChatImApi(c as never)
    await api.updateNotice('cid', null)
    expect(c.put).toHaveBeenCalledWith('/conversations/cid/notice', { notice: null })
  })
  it('getContactProfile hits GET /chat/contacts/{userId}/profile', async () => {
    const c = mockClient(); const api = useChatImApi(c as never)
    await api.getContactProfile('uid1')
    expect(c.get).toHaveBeenCalledWith('/chat/contacts/uid1/profile')
  })
})

describe('usePresenceApi', () => {
  it('setStatus puts to /presence with status body', async () => {
    const c = mockClient(); const api = usePresenceApi(c as never)
    await api.setStatus(UserPresenceStatus.Away)
    expect(c.put).toHaveBeenCalledWith('/presence', { status: UserPresenceStatus.Away })
  })
  it('getMyStatus hits GET /presence/me', async () => {
    const c = mockClient(); const api = usePresenceApi(c as never)
    await api.getMyStatus()
    expect(c.get).toHaveBeenCalledWith('/presence/me')
  })
  it('getPresence hits GET /presence with userIds param', async () => {
    const c = mockClient(); const api = usePresenceApi(c as never)
    await api.getPresence(['u1', 'u2'])
    expect(c.get).toHaveBeenCalledWith('/presence', { params: { userIds: ['u1', 'u2'] } })
  })
})

describe('useChatBroadcastApi', () => {
  it('broadcast posts to /admin/chat/broadcast', async () => {
    const c = mockClient(); const api = useChatBroadcastApi(c as never)
    await api.broadcast({ content: 'hello' })
    expect(c.post).toHaveBeenCalledWith('/admin/chat/broadcast', { content: 'hello' })
  })
})
