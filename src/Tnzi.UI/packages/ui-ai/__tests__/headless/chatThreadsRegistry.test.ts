/**
 * `pickChatApprovals`: which threads instance's approval queue a `TChatApp`
 * shows when not handed one. The one showing the shell's messages (same
 * array), looked for from the nearest component up; else the only one; else
 * none, said to be ambiguous.
 */
import { describe, expect, it } from 'vitest'
import { shallowRef } from 'vue'
import { pickChatApprovals, type ChatThreadsBinding, type ChatThreadsRegistry } from '../../src/headless/chat-approvals-key'
import type { ApprovalQueue } from '../../src/headless/approval-queue'

const queue = (name: string) => ({ name }) as unknown as ApprovalQueue
const binding = (messages: unknown[], approvals: ApprovalQueue): ChatThreadsBinding =>
  ({ messages: shallowRef(messages), approvals }) as ChatThreadsBinding
const registry = (bindings: ChatThreadsBinding[], parent: ChatThreadsRegistry | null = null): ChatThreadsRegistry => ({
  bindings: shallowRef(bindings),
  parent,
})

describe('pickChatApprovals', () => {
  it('picks the instance whose messages are the very array shown, in this component or an ancestor', () => {
    const chatMessages: unknown[] = []
    const codeMessages: unknown[] = []
    const chat = queue('chat')
    const code = queue('code')
    const outer = registry([binding(chatMessages, chat)])
    const inner = registry([binding(codeMessages, code)], outer)
    expect(pickChatApprovals(inner, codeMessages)).toEqual({ approvals: code, ambiguous: false })
    expect(pickChatApprovals(inner, chatMessages)).toEqual({ approvals: chat, ambiguous: false })
  })

  it('falls back to the only instance; with several and no match there is none, and it says so', () => {
    const only = queue('only')
    expect(pickChatApprovals(registry([binding([], only)]), [])).toEqual({ approvals: only, ambiguous: false })
    expect(pickChatApprovals(registry([binding([], only), binding([], queue('b'))]), [])).toEqual({ approvals: null, ambiguous: true })
    expect(pickChatApprovals(null, [])).toEqual({ approvals: null, ambiguous: false })
  })
})
