import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import TMessageList from '../../../src/components/chat/TMessageList.vue'
import TMessageBubble from '../../../src/components/chat/TMessageBubble.vue'
import { MessageContentType } from '@tnzi/core/services/chat'
import type { ChatMessageDto } from '@tnzi/core/services/chat'

// ── Stubs ──────────────────────────────────────────────────────────────────
// NScrollbar: stub to avoid virtual-scroll overhead; items must still be
// rendered so slot content is visible.
const NScrollbarStub = {
  name: 'NScrollbar',
  template: '<div class="n-scrollbar-stub" ref="scrollRef"><slot /></div>',
}

const IconStub = {
  name: 'Icon',
  props: ['icon', 'width'],
  template: '<span class="icon-stub" :data-icon="icon" />',
}

// ── Fixtures ──────────────────────────────────────────────────────────────
const MY_ID = 'user-me'
const OTHER_ID = 'user-other'

function makeMessages(): ChatMessageDto[] {
  return [
    {
      id: 'msg-1',
      conversationId: 'conv-1',
      senderId: MY_ID,
      senderName: 'Me',
      contentType: MessageContentType.Text,
      content: 'Hello from me',
      fileId: null,
      fileName: null,
      fileSize: null,
      sentAt: '2024-03-15T09:00:00Z',
    },
    {
      id: 'msg-2',
      conversationId: 'conv-1',
      senderId: OTHER_ID,
      senderName: 'Alice',
      contentType: MessageContentType.Text,
      content: 'Hello from Alice',
      fileId: null,
      fileName: null,
      fileSize: null,
      sentAt: '2024-03-15T09:01:00Z',
    },
    {
      id: 'msg-3',
      conversationId: 'conv-1',
      senderId: null,
      senderName: null,
      contentType: MessageContentType.System,
      content: 'Alice joined the group',
      fileId: null,
      fileName: null,
      fileSize: null,
      sentAt: '2024-03-15T09:02:00Z',
    },
  ]
}

function mountList(messages = makeMessages(), myId = MY_ID, isGroup = false) {
  return mount(TMessageList, {
    props: { messages, myId, isGroup },
    global: {
      stubs: {
        NScrollbar: NScrollbarStub,
        Icon: IconStub,
      },
    },
  })
}

// ── Tests ─────────────────────────────────────────────────────────────────
describe('TMessageList', () => {
  it('renders one TMessageBubble per message', () => {
    const wrapper = mountList()
    expect(wrapper.findAllComponents(TMessageBubble)).toHaveLength(3)
  })

  it('passes mine=true to the bubble whose senderId matches myId', () => {
    const wrapper = mountList()
    const bubbles = wrapper.findAllComponents(TMessageBubble)
    const myBubble = bubbles.find(
      (w) => (w.props('message') as ChatMessageDto).id === 'msg-1',
    )
    expect(myBubble).toBeDefined()
    expect(myBubble!.props('mine')).toBe(true)
  })

  it('passes mine=false to a bubble from another sender', () => {
    const wrapper = mountList()
    const bubbles = wrapper.findAllComponents(TMessageBubble)
    const otherBubble = bubbles.find(
      (w) => (w.props('message') as ChatMessageDto).id === 'msg-2',
    )
    expect(otherBubble).toBeDefined()
    expect(otherBubble!.props('mine')).toBe(false)
  })

  it('System message bubble renders the system-notice class, not the bubble class', () => {
    const wrapper = mountList()
    // The system message notice element carries t-bubble-system class
    expect(wrapper.find('.t-bubble-system').exists()).toBe(true)
    // And does NOT produce a regular bubble row
    const sysBubble = wrapper.findAllComponents(TMessageBubble).find(
      (w) => (w.props('message') as ChatMessageDto).contentType === MessageContentType.System,
    )
    expect(sysBubble).toBeDefined()
    // Inside the stub the system message should not have a bubble-row element
    expect(sysBubble!.find('.t-bubble-row').exists()).toBe(false)
  })

  it('mine bubble carries right-alignment class and other bubble does not', () => {
    const wrapper = mountList()
    // My message row should be right-aligned
    expect(wrapper.find('.t-bubble-row--mine').exists()).toBe(true)
    // Alice's message row exists but is not mine
    const rows = wrapper.findAll('.t-bubble-row')
    const mineRows = wrapper.findAll('.t-bubble-row--mine')
    // Only 2 bubble-rows (mine + other), system has no bubble-row
    expect(rows).toHaveLength(2)
    expect(mineRows).toHaveLength(1)
  })
})

describe('TMessageBubble — rich system notification', () => {
  const richNotice: ChatMessageDto = {
    id: 'n1',
    conversationId: 'sys',
    senderId: null,
    senderName: null,
    contentType: MessageContentType.System,
    content: 'Your order #1001 has shipped.',
    title: 'Order shipped',
    linkUrl: 'https://example.com/orders/1001',
    category: 'order',
    fileId: null,
    fileName: null,
    fileSize: null,
    sentAt: '2024-03-15T09:00:00Z',
  }

  function mountBubble(message: ChatMessageDto) {
    return mount(TMessageBubble, {
      props: { message, mine: false, showSender: false, isSystem: true },
      global: { stubs: { Icon: IconStub, NImage: true, TChatAvatar: true } },
    })
  }

  it('renders title, body, category and a click-through link', () => {
    const w = mountBubble(richNotice)
    expect(w.find('.t-bubble-notice').exists()).toBe(true)
    expect(w.find('.t-bubble-notice__title').text()).toBe('Order shipped')
    expect(w.find('.t-bubble-notice__category').text()).toBe('order')
    expect(w.find('.t-bubble-notice__body').text()).toBe('Your order #1001 has shipped.')
    const link = w.find('.t-bubble-notice__link')
    expect(link.exists()).toBe(true)
    expect(link.attributes('href')).toBe('https://example.com/orders/1001')
  })

  it('omits title/link/category for a plain system notification', () => {
    const w = mountBubble({ ...richNotice, title: null, linkUrl: null, category: null })
    expect(w.find('.t-bubble-notice').exists()).toBe(true)
    expect(w.find('.t-bubble-notice__title').exists()).toBe(false)
    expect(w.find('.t-bubble-notice__link').exists()).toBe(false)
    expect(w.find('.t-bubble-notice__category').exists()).toBe(false)
    expect(w.find('.t-bubble-notice__body').text()).toBe('Your order #1001 has shipped.')
  })
})
