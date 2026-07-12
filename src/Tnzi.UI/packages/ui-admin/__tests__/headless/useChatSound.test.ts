import { describe, it, expect, vi, beforeEach } from 'vitest'
import { ChatSoundEffect } from '@tnzi/core/services/chat'

// jsdom has no real AudioContext — stub one whose oscillators report start().
const startSpy = vi.fn()
beforeEach(() => {
  startSpy.mockClear()
  ;(globalThis as never as { AudioContext: unknown }).AudioContext = class {
    state = 'running'
    currentTime = 0
    createOscillator() {
      return {
        type: '',
        connect: vi.fn(),
        frequency: { setValueAtTime: vi.fn(), exponentialRampToValueAtTime: vi.fn() },
        start: startSpy,
        stop: vi.fn(),
      }
    }
    createGain() {
      return { connect: vi.fn(), gain: { setValueAtTime: vi.fn(), exponentialRampToValueAtTime: vi.fn() } }
    }
    get destination() {
      return {}
    }
    resume() {
      return Promise.resolve()
    }
  }
})

import { useChatSound } from '../../src/headless/useChatSound'

describe('useChatSound', () => {
  it('playNotification() triggers oscillators when enabled', () => {
    const s = useChatSound()
    s.configure({ enabled: true, notification: ChatSoundEffect.Chime, message: ChatSoundEffect.Pop })
    s.playNotification()
    expect(startSpy).toHaveBeenCalled()
  })

  it('playMessage() does nothing when the master toggle is off', () => {
    const s = useChatSound()
    s.configure({ enabled: false })
    s.playMessage()
    expect(startSpy).not.toHaveBeenCalled()
  })

  it('a None effect is silent even when enabled', () => {
    const s = useChatSound()
    s.configure({ enabled: true, notification: ChatSoundEffect.None })
    s.playNotification()
    expect(startSpy).not.toHaveBeenCalled()
  })

  it('preview() plays regardless of the enabled flag', () => {
    const s = useChatSound()
    s.configure({ enabled: false })
    s.preview(ChatSoundEffect.Bell)
    expect(startSpy).toHaveBeenCalled()
  })

  it('swallows errors (no throw) if AudioContext is missing', () => {
    ;(globalThis as never as { AudioContext: unknown }).AudioContext = undefined
    const s = useChatSound()
    s.configure({ enabled: true, notification: ChatSoundEffect.Chime })
    expect(() => s.playNotification()).not.toThrow()
  })
})
