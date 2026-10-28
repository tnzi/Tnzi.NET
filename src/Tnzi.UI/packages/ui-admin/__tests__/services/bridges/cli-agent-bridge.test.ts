import { describe, it, expect, vi } from 'vitest'
import { createCliAgentBridge } from '../../../src/services/bridges/cli-agent-bridge'
import type { HttpClient } from '@tnzi/core/http'

// ---- 拒绝信封必须抛，不能被当成成功 ----
//
// ★ `runtimes.probe` 是 `POST /admin/ai/cli-runtimes/probe`：后端会插入/更新运行时行并
//   把找不到可执行文件的 provider 标记离线；模块关着（默认 `AI:Cli:Enabled=false`）时回
//   501 + 一句解释。`HttpClient` 对这类拒绝返回失败信封而不 reject，裸 `unwrap` 就把
//   `data: null` 原样交出去，页面随即在 `result.runtimes.length` 上抛 TypeError ——
//   操作员看到的是 "Cannot read properties of undefined" 而不是服务器那句话。
//   页面测试 `vi.mock` 整个 bridge，永远看不见这一层，所以门禁在这里。

const REFUSAL = 'External CLI agent execution is disabled (AI:Cli:Enabled=false).'

/** Mock HttpClient: reads succeed, every write answers a refused envelope. */
function refusingClient() {
  const ok = <T>(data: T) => ({ data, succeeded: true, success: true, code: 200, message: '' })
  const refused = () => ({ data: null, succeeded: false, success: false, code: 501, message: REFUSAL })
  return {
    get: vi.fn(async () => ok([])),
    post: vi.fn(async () => refused()),
    put: vi.fn(async () => refused()),
    delete: vi.fn(async () => refused()),
  } as unknown as HttpClient
}

describe('cli-agent-bridge write paths', () => {
  const bridge = createCliAgentBridge({ client: refusingClient() })

  const writes: Array<[string, () => Promise<unknown>]> = [
    ['runtimes.probe', () => bridge.runtimes.probe()],
    ['runtimes.update', () => bridge.runtimes.update('r1', {} as never)],
    ['runtimes.remove', () => bridge.runtimes.remove('r1')],
    ['bindings.upsert', () => bridge.bindings.upsert('a1', {} as never)],
    ['bindings.remove', () => bridge.bindings.remove('a1')],
    ['runs.cancel', () => bridge.runs.cancel('run1')],
  ]

  it.each(writes)('%s rejects with the backend reason on a refused envelope', async (_name, call) => {
    await expect(call()).rejects.toThrow(REFUSAL)
  })

  it('a successful probe still unwraps to the payload', async () => {
    const client = refusingClient()
    ;(client.post as unknown as ReturnType<typeof vi.fn>).mockResolvedValue({
      data: { runtimes: [{ id: 'r1' }], notFound: ['qwen'] },
      succeeded: true,
      success: true,
      code: 200,
      message: '',
    })
    const okBridge = createCliAgentBridge({ client })
    await expect(okBridge.runtimes.probe()).resolves.toEqual({ runtimes: [{ id: 'r1' }], notFound: ['qwen'] })
  })

  it('without a client, probe answers an empty result instead of throwing (module absent)', async () => {
    const noClient = createCliAgentBridge({})
    await expect(noClient.runtimes.probe()).resolves.toEqual({ runtimes: [], notFound: [] })
  })
})

// ---- 读：只有「模块不在」才读作空 ----
//
// 501（模块关着）与 404（路由不在）是部署事实，列表页读作「这里没有」。其余拒绝
// （400 绑定失败、403 无权、500）以前也被读成空列表：运行记录页筛「昨天失败的」
// 拿到 400，表格显示「暂无数据」，读起来就是「那段时间没有运行」。
describe('cli-agent-bridge reads', () => {
  function answering(code: number, message = 'Refused') {
    const envelope = { data: null, succeeded: false, success: false, code, message }
    return {
      get: vi.fn(async () => envelope),
      post: vi.fn(async () => envelope),
      put: vi.fn(async () => envelope),
      delete: vi.fn(async () => envelope),
    } as unknown as HttpClient
  }

  const reads: Array<[string, (b: ReturnType<typeof createCliAgentBridge>) => Promise<unknown>]> = [
    ['runs.list', (b) => b.runs.list({ pageIndex: 1, pageSize: 20 } as never)],
    ['runs.messages', (b) => b.runs.messages('run1')],
    ['runtimes.list', (b) => b.runtimes.list()],
    ['runtimes.providers', (b) => b.runtimes.providers()],
    ['bindings.get', (b) => b.bindings.get('a1')],
  ]

  it.each(reads)('%s rejects with the server reason on a refusal that is not "module absent"', async (_name, call) => {
    await expect(call(createCliAgentBridge({ client: answering(400, 'The value is not valid for StartTime.') }))).rejects.toThrow(
      'The value is not valid for StartTime.',
    )
    await expect(call(createCliAgentBridge({ client: answering(403, 'Forbidden') }))).rejects.toThrow('Forbidden')
  })

  it.each(reads)('%s still reads a 501 or 404 as "not available here"', async (_name, call) => {
    await expect(call(createCliAgentBridge({ client: answering(501) }))).resolves.not.toBeUndefined()
    await expect(call(createCliAgentBridge({ client: answering(404) }))).resolves.not.toBeUndefined()
  })
})
