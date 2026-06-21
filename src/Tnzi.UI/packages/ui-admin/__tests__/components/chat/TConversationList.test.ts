import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { nextTick } from 'vue'
import TConversationList from '../../../src/components/chat/TConversationList.vue'
import TConversationItem from '../../../src/components/chat/TConversationItem.vue'
import { ConversationType } from '@tnzi/core/services/chat'
import type { ConversationListItemDto } from '@tnzi/core/services/chat'

// ── Stubs ─────────────────────────────────────────────────────────────────
// naive-ui components are NOT reliably matched by string-name stubs when
// imported as named exports from 'naive-ui'. Strategy: stub NScrollbar so
// items are visible without virtual scroll overhead; let NInput render real
// (it still renders an <input> element internally) and drive keyword via vm
// ref rather than DOM input events which can't trigger v-model:value on the
// naive-ui stub.
const NScrollbarStub = {
  name: 'NScrollbar',
  template: '<div class="n-scrollbar-stub"><slot /></div>',
}

const NBadgeStub = {
  name: 'NBadge',
  props: ['value', 'show', 'max'],
  template: '<div class="n-badge-stub" :data-value="value" :data-show="show"><slot /></div>',
}

const IconStub = {
  name: 'Icon',
  props: ['icon', 'width'],
  template: '<span class="icon-stub" :data-icon="icon" />',
}

// ── Fixtures ──────────────────────────────────────────────────────────────
function makeConversations(): ConversationListItemDto[] {
  return [
    {
      id: 'conv-1',
      type: ConversationType.Direct,
      title: 'Alice',
      avatarFileId: null,
      lastMessagePreview: 'Hello!',
      lastMessageAt: '2024-03-15T09:00:00Z',
      unreadCount: 2,
      isMuted: false,
      memberCount: 2,
    },
    {
      id: 'conv-2',
      type: ConversationType.Group,
      title: 'Team Chat',
      avatarFileId: null,
      lastMessagePreview: 'Let me know',
      lastMessageAt: '2024-03-15T08:00:00Z',
      unreadCount: 0,
      isMuted: true,
      memberCount: 5,
    },
    {
      id: 'conv-sys',
      type: ConversationType.System,
      title: 'System Notifications',
      avatarFileId: null,
      lastMessagePreview: 'Welcome!',
      lastMessageAt: '2024-03-15T07:00:00Z',
      unreadCount: 1,
      isMuted: false,
      memberCount: 0,
    },
  ]
}

// ── Helpers ───────────────────────────────────────────────────────────────
function mountList(conversations = makeConversations(), activeId: string | null = null) {
  return mount(TConversationList, {
    props: { conversations, activeId },
    global: {
      stubs: {
        // Stub NScrollbar (virtual-scroll) and heavy render deps.
        // NInput is left real — it renders a native <input> internally.
        NScrollbar: NScrollbarStub,
        NBadge: NBadgeStub,
        Icon: IconStub,
      },
    },
  })
}

describe('TConversationList', () => {
  it('renders one item per conversation', () => {
    const wrapper = mountList()
    expect(wrapper.findAllComponents(TConversationItem)).toHaveLength(3)
  })

  it('clicking an item emits select with the conversation id', async () => {
    const wrapper = mountList()
    const items = wrapper.findAllComponents(TConversationItem)
    // items are sorted: system pinned first. conv-sys goes first.
    // We need to find items and click a known one. Find by checking props.
    const aliceItem = items.find((w) => (w.props('item') as ConversationListItemDto).id === 'conv-1')
    expect(aliceItem).toBeDefined()
    await aliceItem!.trigger('click')
    const emitted = wrapper.emitted('select')
    expect(emitted).toBeTruthy()
    expect(emitted![0]).toEqual(['conv-1'])
  })

  it('typing in the search box filters items by title', async () => {
    const wrapper = mountList()
    // NInput is rendered real; drive the reactive keyword ref via the exposed
    // vm property to avoid dependency on NInput's internal DOM structure.
    ;(wrapper.vm as unknown as { keyword: string }).keyword = 'Alice'
    await nextTick()
    const items = wrapper.findAllComponents(TConversationItem)
    expect(items).toHaveLength(1)
    expect((items[0].props('item') as ConversationListItemDto).title).toBe('Alice')
  })

  it('clicking the + button emits new-chat', async () => {
    const wrapper = mountList()
    await wrapper.find('.t-conv-list__add').trigger('click')
    expect(wrapper.emitted('new-chat')).toBeTruthy()
  })

  it('System-type conversation is sorted to the top', () => {
    const wrapper = mountList()
    const items = wrapper.findAllComponents(TConversationItem)
    const firstItem = items[0].props('item') as ConversationListItemDto
    expect(firstItem.type).toBe(ConversationType.System)
    expect(firstItem.id).toBe('conv-sys')
  })

  it('shows empty state when no conversations match search', async () => {
    const wrapper = mountList()
    ;(wrapper.vm as unknown as { keyword: string }).keyword = 'zzz-no-match'
    await nextTick()
    expect(wrapper.findAllComponents(TConversationItem)).toHaveLength(0)
    expect(wrapper.find('.t-conv-list__empty').exists()).toBe(true)
  })

  it('marks the active item with active prop', () => {
    const wrapper = mountList(makeConversations(), 'conv-1')
    const items = wrapper.findAllComponents(TConversationItem)
    const activeItem = items.find((w) => (w.props('item') as ConversationListItemDto).id === 'conv-1')
    expect(activeItem?.props('active')).toBe(true)
    // others should not be active
    const inactiveItem = items.find((w) => (w.props('item') as ConversationListItemDto).id === 'conv-2')
    expect(inactiveItem?.props('active')).toBe(false)
  })
})
