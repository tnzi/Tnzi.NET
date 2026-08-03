import { describe, it, expect, vi, beforeEach } from 'vitest'
import { defineComponent, h, nextTick } from 'vue'
import { mount, flushPromises } from '@vue/test-utils'

const resolve = vi.fn()
const plain = vi.fn((id: string, kind?: string) => `/api/files/${id}/${kind ?? 'preview'}`)

vi.mock('../../src/plugin/client', () => ({ useAdminClient: () => ({ id: 'client' }) }))
vi.mock('../../src/services/file-url-resolver', () => ({
  getFileUrlResolver: () => ({ resolve, resolveMany: vi.fn(), plain, clear: vi.fn() }),
}))

const TFileImage = (await import('../../src/components/display/TFileImage.vue')).default
const TFileLink = (await import('../../src/components/display/TFileLink.vue')).default

describe('TFileImage', () => {
  beforeEach(() => {
    resolve.mockReset()
    plain.mockClear()
  })

  it('renders the signed URL once it resolves', async () => {
    resolve.mockResolvedValue('/api/files/f1/preview?sig=tok')

    const wrapper = mount(TFileImage, { props: { fileId: 'f1' } })
    await flushPromises()

    expect(wrapper.find('img').attributes('src')).toBe('/api/files/f1/preview?sig=tok')
  })

  it('renders the fallback slot instead of a broken image', async () => {
    // 读不了 / 还没到 —— 宁可显示类型字形，也不要一个裂图占位。
    resolve.mockResolvedValue(null)

    const wrapper = mount(TFileImage, {
      props: { fileId: 'f1' },
      slots: { fallback: '<span class="glyph" />' },
    })
    await flushPromises()

    expect(wrapper.find('img').exists()).toBe(false)
    expect(wrapper.find('.glyph').exists()).toBe(true)
  })

  it('skips the round trip for a public file', async () => {
    const wrapper = mount(TFileImage, { props: { fileId: 'avatar', isPublic: true } })
    await flushPromises()

    expect(resolve).not.toHaveBeenCalled()
    expect(wrapper.find('img').attributes('src')).toBe('/api/files/avatar/preview')
  })

  it('每个实例各自解析，所以放进 v-for 是安全的', async () => {
    // 这正是组件层存在的意义：批量合并在 resolver 层，不在调用点，
    // 所以列表里不必改用 useFileUrls。
    resolve.mockImplementation(async (id: string) => `/api/files/${id}/preview?sig=${id}`)
    const List = defineComponent({
      setup: () => () => h('div', ['a', 'b', 'c'].map((id) => h(TFileImage, { key: id, fileId: id }))),
    })

    const wrapper = mount(List)
    await flushPromises()

    expect(wrapper.findAll('img')).toHaveLength(3)
    expect(resolve).toHaveBeenCalledTimes(3)
  })
})

describe('TFileLink', () => {
  beforeEach(() => {
    resolve.mockReset()
    plain.mockClear()
  })

  it('points at the signed download URL', async () => {
    resolve.mockResolvedValue('/api/files/f1/download?sig=tok')

    const wrapper = mount(TFileLink, { props: { fileId: 'f1' }, slots: { default: 'Download' } })
    await flushPromises()

    expect(wrapper.find('a').attributes('href')).toBe('/api/files/f1/download?sig=tok')
    expect(wrapper.text()).toContain('Download')
  })

  it('★ renders NO href while unresolved, never an empty one', async () => {
    // 这条守的是踩过两次的坑：`href=""` 指向当前页，点一下把整个应用重载，
    // 用户正在填的表单一起没了；`href="#"` 跳页首。没有 href 的 <a> 不是链接，
    // 不可聚焦也不导航 —— 那才是想要的惰性状态。
    resolve.mockReturnValue(new Promise(() => {}))

    const wrapper = mount(TFileLink, { props: { fileId: 'f1' }, slots: { default: 'Download' } })
    await nextTick()

    const anchor = wrapper.find('a')
    expect(anchor.attributes('href')).toBeUndefined()
    expect(anchor.attributes('aria-disabled')).toBe('true')
  })

  it('stays inert when the caller may not read the file', async () => {
    resolve.mockResolvedValue(null)

    const wrapper = mount(TFileLink, { props: { fileId: 'f1' }, slots: { default: 'Download' } })
    await flushPromises()

    // 内容照常渲染（布局不跳），只是点不动。
    expect(wrapper.text()).toContain('Download')
    expect(wrapper.find('a').attributes('href')).toBeUndefined()
  })

  it('skips the round trip for a public file', async () => {
    const wrapper = mount(TFileLink, { props: { fileId: 'logo', isPublic: true } })
    await flushPromises()

    expect(resolve).not.toHaveBeenCalled()
    expect(wrapper.find('a').attributes('href')).toBe('/api/files/logo/download')
  })
})
