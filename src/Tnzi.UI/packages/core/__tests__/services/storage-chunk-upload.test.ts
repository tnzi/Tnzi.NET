import { afterEach, describe, expect, it, vi } from 'vitest'
import { HttpClient } from '../../src/http/http'
import { useStorageApi } from '../../src/services/storage/api'

/**
 * `POST /files/upload/chunk/{id}` binds `IFormFile chunk`, and ASP.NET Core
 * binds a top-level form file by parameter name. The part used to go out as
 * `file`, the default name `HttpClient.upload` uses, so every chunk came back
 * 400 "Chunk file is required" and no file above the chunk threshold could be
 * uploaded. Driven through a real HttpClient so the assertion is on the
 * multipart body that leaves the browser, not on which helper was called.
 */
describe('useStorageApi().uploadChunk', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('sends the chunk under the multipart name the endpoint binds', async () => {
    let sentUrl = ''
    let sentBody: FormData | null = null
    class FakeXhr {
      status = 200
      responseText = JSON.stringify({ succeeded: true, code: 200, data: { chunkIndex: 3 } })
      timeout = 0
      withCredentials = false
      upload = { onprogress: null as unknown, onload: null as unknown }
      onload: (() => void) | null = null
      onerror: (() => void) | null = null
      ontimeout: (() => void) | null = null
      onabort: (() => void) | null = null
      onprogress: (() => void) | null = null
      open(_method: string, url: string) {
        sentUrl = url
      }
      setRequestHeader() {}
      abort() {}
      send(body: FormData) {
        sentBody = body
        queueMicrotask(() => this.onload?.())
      }
    }
    vi.stubGlobal('XMLHttpRequest', FakeXhr)

    const chunk = new File(['0123456789'], 'big.bin')
    const result = await useStorageApi(new HttpClient({ baseUrl: '/api' })).uploadChunk('s1', 3, chunk)

    expect(result.succeeded).toBe(true)
    expect(sentUrl).toBe('/api/files/upload/chunk/s1?chunkIndex=3')
    const body = sentBody as unknown as FormData
    expect(body.get('chunk')).toBeInstanceOf(File)
    expect((body.get('chunk') as File).size).toBe(10)
    expect(body.has('file')).toBe(false)
  })
})
