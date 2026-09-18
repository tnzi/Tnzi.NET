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
