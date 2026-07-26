import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import TConversationItem from '../../../src/components/chat/TConversationItem.vue'
import { ConversationType, UserPresenceStatus } from '@tnzi/core/services/chat'
import type { ConversationListItemDto } from '@tnzi/core/services/chat'

// Passthrough stub so the avatar's #badge slot renders without naive's NBadge chrome.
const NBadgeStub = {
  name: 'NBadge',
  props: ['value', 'show', 'max'],
  template: '<div class="n-badge-stub"><slot /></div>',
}

function makeItem(overrides: Partial<ConversationListItemDto> = {}): ConversationListItemDto {
  return {
    id: 'conv-1',
    type: ConversationType.Direct,
    title: 'Alice',
    avatarFileId: null,
    lastMessagePreview: 'Hello!',
    lastMessageAt: '2024-03-15T09:00:00Z',
    unreadCount: 0,
    isMuted: false,
    memberCount: 2,
    peerStatus: UserPresenceStatus.Online,
    isSticky: false,
    ...overrides,
  }
}

function mountItem(item: ConversationListItemDto) {
  // `presence: true` mirrors how TConversationList renders each item (a typed
  // boolean prop left absent is coerced to false by Vue, which would hide the
  // presence dot). The disabled marker is independent of this toggle.
  return mount(TConversationItem, {
    props: { item, active: false, presence: true },
    global: { stubs: { NBadge: NBadgeStub } },
  })
}

describe('TConversationItem - disabled peer marker', () => {
  it('renders the "unavailable" marker for a Direct peer that lost chat.use', () => {
    const wrapper = mountItem(makeItem({ peerDisabled: true }))
    // The distinct grey marker replaces the normal presence dot.
    expect(wrapper.find('.t-chat-avatar__disabled').exists()).toBe(true)
    expect(wrapper.find('.t-presence-dot').exists()).toBe(false)
  })

  it('renders the normal presence dot (no marker) for an available peer', () => {
    const wrapper = mountItem(makeItem({ peerDisabled: false }))
    expect(wrapper.find('.t-chat-avatar__disabled').exists()).toBe(false)
    expect(wrapper.find('.t-presence-dot').exists()).toBe(true)
  })

  it('never shows the marker on a Group conversation even if the flag is set', () => {
    const wrapper = mountItem(
      makeItem({ type: ConversationType.Group, peerStatus: null, peerDisabled: true }),
    )
    expect(wrapper.find('.t-chat-avatar__disabled').exists()).toBe(false)
  })
})
