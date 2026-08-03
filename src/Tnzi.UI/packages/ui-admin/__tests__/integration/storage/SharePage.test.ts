import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'

const getSharePreview = vi.fn()
const getShareDownloadUrl = vi.fn((token: string, password?: string) =>
  `/api/files/share/${token}/download${password ? `?password=${password}` : ''}`,
)
const verifyShareAccess = vi.fn()

// The page now goes through `storage-bridge` (the layering every other page
// follows) rather than calling `useStorageApi` itself, so the bridge's OTHER
// factory imports must survive the mock - replacing the module wholesale makes
// `createStorageBridge` throw on `useAdminFileApi` before the page ever runs.
// Stubbing at the core boundary (not at the bridge) keeps the assertions below
// meaningful: they are about WHICH endpoint gets hit, which is exactly the
// mapping the bridge owns.
vi.mock('@tnzi/core/services/storage', async (importOriginal) => ({
  ...(await importOriginal<Record<string, unknown>>()),
  useStorageApi: () => ({ getSharePreview, getShareDownloadUrl, verifyShareAccess }),
}))
vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ id: 'client' }),
}))
vi.mock('vue-router', () => ({
  useRoute: () => ({ params: { token: 'tok-123' } }),
}))

const SharePage = (await import('../../../src/pages/share/SharePage.vue')).default

function ok(data: Record<string, unknown>) {
  return { succeeded: true, code: 200, data }
}

async function mountPage() {
  const wrapper = mount(SharePage, {
    global: { stubs: { NSpin: { template: '<div><slot /></div>' } } },
  })
  await flushPromises()
  return wrapper
}

describe('SharePage (share-link recipient)', () => {
  beforeEach(() => {
    getSharePreview.mockReset()
    getShareDownloadUrl.mockClear()
    verifyShareAccess.mockReset()
  })

  it('shows what the recipient is about to open', async () => {
    getSharePreview.mockResolvedValue(
      ok({ fileName: 'quote.pdf', size: 2048, contentType: 'application/pdf', requirePassword: false }),
    )

    const wrapper = await mountPage()

    expect(wrapper.text()).toContain('quote.pdf')
    expect(getSharePreview).toHaveBeenCalledWith('tok-123')
  })

  it('says the same thing for every kind of dead link', async () => {
    // 撤销 / 过期 / 次数用尽 / 不存在都折叠成同一句 —— 区分开就等于告诉试探者
    // 哪些令牌是真的。
    getSharePreview.mockResolvedValue({ succeeded: false, code: 404 })

    const wrapper = await mountPage()

    expect(wrapper.text()).toContain('no longer available')
    expect(wrapper.find('input').exists()).toBe(false)
  })

  it('does not blow up when the preview request itself fails', async () => {
    // 网络问题与"链接不可用"对收件人是同一件事：他什么都做不了。
    getSharePreview.mockRejectedValue(new Error('offline'))

    const wrapper = await mountPage()

    expect(wrapper.text()).toContain('no longer available')
  })

  it('asks for a password only when the link carries one', async () => {
    getSharePreview.mockResolvedValue(ok({ fileName: 'payslip.pdf', size: 10, requirePassword: true }))

    const wrapper = await mountPage()

    expect(wrapper.find('input').exists()).toBe(true)
    // 口令未填时下载钮禁用：省掉一次注定失败的往返。
    expect(wrapper.find('button').attributes('disabled')).toBeDefined()
  })

  it('reports a wrong password in place instead of navigating away', async () => {
    // 让浏览器跳去一个 401 页面，收件人就再也回不到这一屏了。
    getSharePreview.mockResolvedValue(ok({ fileName: 'payslip.pdf', size: 10, requirePassword: true }))
    verifyShareAccess.mockResolvedValue({ succeeded: true, code: 200, data: false })
    vi.stubGlobal('location', { href: '' })

    const wrapper = await mountPage()
    await wrapper.find('input').setValue('nope')
    await wrapper.find('button').trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain('does not match')
    // 没有导航发生 —— 收件人还在这一屏，可以直接重输。
    expect(window.location.href).toBe('')
    vi.unstubAllGlobals()
  })

  it('checks the password through the quota-free endpoint, not the download URL', async () => {
    // ★ 拿下载端点探测会消耗一次访问配额，maxAccessCount = 1 的链接会在真正
    // 下载之前就用完。而且 HEAD 对 [HttpGet] 路由是 405 —— 实测过，那样每个
    // 带口令的链接都会显示口令不正确，正是它专为之写的那个场景。
    getSharePreview.mockResolvedValue(ok({ fileName: 'payslip.pdf', size: 10, requirePassword: true }))
    verifyShareAccess.mockResolvedValue({ succeeded: true, code: 200, data: true })
    const fetchSpy = vi.fn()
    vi.stubGlobal('fetch', fetchSpy)
    vi.stubGlobal('location', { href: '' })

    const wrapper = await mountPage()
    await wrapper.find('input').setValue('letmein')
    await wrapper.find('button').trigger('click')
    await flushPromises()

    expect(verifyShareAccess).toHaveBeenCalledWith('tok-123', 'letmein')
    expect(fetchSpy).not.toHaveBeenCalled()
    expect(window.location.href).toContain('/download')
    vi.unstubAllGlobals()
  })
})
