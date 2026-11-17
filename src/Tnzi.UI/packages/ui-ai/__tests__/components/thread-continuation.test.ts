import { describe, expect, it } from 'vitest'
import type { ChatMessage } from '../../src/headless/useChat'
import { continuesAfterBackgroundTask } from '../../src/components/chat/thread-continuation'

const row = (role: ChatMessage['role'], content: string, extra: Partial<ChatMessage> = {}): ChatMessage =>
  ({ id: `${role}-${content}`, role, content, ...extra }) as ChatMessage

describe('continuesAfterBackgroundTask', () => {
  it('marks an answer that follows another answer with no user message between', () => {
    expect(continuesAfterBackgroundTask(row('assistant', 'first'), row('assistant', 'second'))).toBe(true)
  })

  it('does not mark an answer after a user message, nor the first row', () => {
    expect(continuesAfterBackgroundTask(row('user', 'hi'), row('assistant', 'answer'))).toBe(false)
    expect(continuesAfterBackgroundTask(undefined, row('assistant', 'answer'))).toBe(false)
    expect(continuesAfterBackgroundTask(row('assistant', 'answer'), row('user', 'next'))).toBe(false)
  })

  it('waits for the second answer to show something', () => {
    const first = row('assistant', 'first')
    expect(continuesAfterBackgroundTask(first, row('assistant', '', { status: 'waiting' }))).toBe(false)
    expect(continuesAfterBackgroundTask(first, row('assistant', 'text', { status: 'waiting' }))).toBe(false)
    expect(continuesAfterBackgroundTask(first, row('assistant', '  ', { isStreaming: true, status: 'streaming' }))).toBe(false)
    const tool = { id: 'c1', name: 'Bash', arguments: '{}', status: 'running' } as unknown as NonNullable<ChatMessage['toolCalls']>[number]
    expect(continuesAfterBackgroundTask(first, row('assistant', '', { isStreaming: true, toolCalls: [tool] }))).toBe(true)
  })
})
