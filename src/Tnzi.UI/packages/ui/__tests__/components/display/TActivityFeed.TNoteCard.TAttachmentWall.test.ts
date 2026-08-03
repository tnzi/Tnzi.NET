import { describe, it, expect, vi } from 'vitest'
import { mount } from '@vue/test-utils'

vi.mock('naive-ui', () => ({
  NImage: { name: 'NImage', props: ['src', 'width', 'height', 'objectFit'], template: '<img class="nimg" :src="src" />' },
  NImageGroup: { name: 'NImageGroup', template: '<div class="nimggroup"><slot /></div>' },
}))
vi.mock('@iconify/vue', () => ({
  Icon: { name: 'Icon', props: ['icon'], template: '<i class="icon" :data-icon="icon" />' },
}))

import TNoteCard from '../../../src/components/display/TNoteCard.vue'
import TActivityFeed from '../../../src/components/display/TActivityFeed.vue'
import TAttachmentWall from '../../../src/components/display/TAttachmentWall.vue'

const stubs = { TAvatar: { name: 'TAvatar', props: ['name', 'src', 'size', 'seed'], template: '<span class="tavatar" />' } }

describe('TNoteCard', () => {
  it('renders author, time and content', () => {
    const w = mount(TNoteCard, { props: { author: 'Alice', time: '2m ago', content: 'Hello' }, global: { stubs } })
    expect(w.text()).toContain('Alice')
    expect(w.text()).toContain('2m ago')
    expect(w.text()).toContain('Hello')
  })

  it('prefers the default slot over content', () => {
    const w = mount(TNoteCard, { props: { author: 'A', content: 'ignored' }, slots: { default: 'slotted body' }, global: { stubs } })
    expect(w.text()).toContain('slotted body')
  })
})

describe('TActivityFeed', () => {
  it('renders one #item slot per item', () => {
    const w = mount(TActivityFeed, {
      props: { items: [{ id: 1 }, { id: 2 }] },
      slots: { item: '<div class="feed-item">row</div>' },
    })
    expect(w.findAll('.feed-item')).toHaveLength(2)
  })

  it('shows the empty text when there are no items', () => {
    const w = mount(TActivityFeed, { props: { items: [], emptyText: 'Nothing here' } })
    expect(w.text()).toContain('Nothing here')
  })
})

describe('TAttachmentWall', () => {
  it('renders images as NImage and files as chips (isImage inference)', () => {
    const w = mount(TAttachmentWall, {
      props: {
        attachments: [
          { url: '/a.png', name: 'a.png' },
          { url: '/b.pdf', name: 'b.pdf' },
        ],
      },
    })
    expect(w.findAll('.nimg')).toHaveLength(1)
    expect(w.findAll('.t-attachment-wall__file')).toHaveLength(1)
  })

  it('emits remove for a tile and add for the add tile', async () => {
    const w = mount(TAttachmentWall, {
      props: { attachments: [{ url: '/x.pdf', name: 'x.pdf' }], removable: true, addable: true },
    })
    await w.find('.t-attachment-wall__remove').trigger('click')
    expect(w.emitted('remove')?.[0]?.[0]).toEqual({ url: '/x.pdf', name: 'x.pdf' })
    await w.find('.t-attachment-wall__add').trigger('click')
    expect(w.emitted('add')).toBeTruthy()
  })

  it('shows empty text when no attachments and not addable', () => {
    const w = mount(TAttachmentWall, { props: { attachments: [], emptyText: 'None' } })
    expect(w.text()).toContain('None')
  })

  it('sanitizes a javascript: file URL in the link href', () => {
    const w = mount(TAttachmentWall, { props: { attachments: [{ url: 'javascript:alert(1)', name: 'x.pdf' }] } })
    expect(w.find('.t-attachment-wall__file').attributes('href')).toBe('#')
  })
})

describe('TAttachmentWall - #tile slot (private files)', () => {
  it('lets the host render a tile, keeping the frame and the remove button', async () => {
    // 私有文件没有可直接渲染的 URL,宿主用自己的解析组件补上那一格。
    const w = mount(TAttachmentWall, {
      props: { attachments: [{ id: 'f1', name: 'contract.pdf' }], removable: true },
      slots: { tile: '<span class="host-tile">host</span>' },
    })

    expect(w.find('.host-tile').exists()).toBe(true)
    // 默认渲染必须让位,否则会同时出现一个断掉的链接。
    expect(w.find('.t-attachment-wall__file').exists()).toBe(false)
    expect(w.findAll('.nimg')).toHaveLength(0)

    await w.find('.t-attachment-wall__remove').trigger('click')
    expect(w.emitted('remove')?.[0]?.[0]).toEqual({ id: 'f1', name: 'contract.pdf' })
  })

  it('emits no href at all when the attachment has no URL', () => {
    // 私有文件且宿主没填 #tile 槽:没有 href 的 <a> 既不导航也不可聚焦,
    // 而 href="#" 会把页面滚回顶部、href="" 会重载整个应用。
    const w = mount(TAttachmentWall, { props: { attachments: [{ id: 'f1', name: 'x.pdf' }] } })
    expect(w.find('.t-attachment-wall__file').attributes('href')).toBeUndefined()
  })
})
