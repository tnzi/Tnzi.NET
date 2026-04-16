import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { createMockChatEngine } from '../../playground/src/mock/engine'
import type { MockScenario } from '../../playground/src/mock/types'

const simpleScenario: MockScenario = {
  meta: {
    id: 'test-simple',
    title: 'Test Simple',
    description: 'Test scenario',
    category: 'conversation',
    icon: 'lucide:test',
    componentsShowcased: [],
  },
  events: [
    { at: 0, type: 'user-message', content: 'Hello' },
    { at: 100, type: 'assistant-start' },
    { at: 200, type: 'assistant-delta', text: 'Hi ' },
    { at: 300, type: 'assistant-delta', text: 'there!' },
    { at: 400, type: 'assistant-end', usage: { promptTokens: 1, completionTokens: 2, totalTokens: 3 } },
  ],
}

describe('createMockChatEngine', () => {
  beforeEach(() => {
    vi.useFakeTimers()
  })
  afterEach(() => {
    vi.useRealTimers()
  })

  it('starts in idle state', () => {
    const engine = createMockChatEngine(simpleScenario)
    expect(engine.state.playbackState.value).toBe('idle')
    expect(engine.state.messages.value).toHaveLength(0)
  })

  it('play() begins event emission', () => {
    const engine = createMockChatEngine(simpleScenario)
    engine.controls.play()
    expect(engine.state.playbackState.value).toBe('playing')
  })

  it('advances through events on timer', () => {
    const engine = createMockChatEngine(simpleScenario)
    engine.controls.play()
    vi.advanceTimersByTime(0)
    expect(engine.state.messages.value).toHaveLength(1)
    expect(engine.state.messages.value[0]?.role).toBe('user')
    vi.advanceTimersByTime(100)
    expect(engine.state.messages.value).toHaveLength(2)
    expect(engine.state.messages.value[1]?.role).toBe('assistant')
    vi.advanceTimersByTime(100)
    expect(engine.state.messages.value[1]?.content).toBe('Hi ')
    vi.advanceTimersByTime(100)
    expect(engine.state.messages.value[1]?.content).toBe('Hi there!')
    vi.advanceTimersByTime(100)
    expect(engine.state.playbackState.value).toBe('done')
  })

  it('pause() halts progress', () => {
    const engine = createMockChatEngine(simpleScenario)
    engine.controls.play()
    vi.advanceTimersByTime(150)
    engine.controls.pause()
    expect(engine.state.playbackState.value).toBe('paused')
    const messageCount = engine.state.messages.value.length
    vi.advanceTimersByTime(500)
    expect(engine.state.messages.value.length).toBe(messageCount)
  })

  it('play() after pause() resumes from current event', () => {
    const engine = createMockChatEngine(simpleScenario)
    engine.controls.play()
    vi.advanceTimersByTime(150)
    engine.controls.pause()
    engine.controls.play()
    vi.advanceTimersByTime(500)
    expect(engine.state.messages.value[1]?.content).toBe('Hi there!')
  })

  it('skipToEnd() applies all remaining events immediately', () => {
    const engine = createMockChatEngine(simpleScenario)
    engine.controls.skipToEnd()
    expect(engine.state.playbackState.value).toBe('done')
    expect(engine.state.messages.value).toHaveLength(2)
    expect(engine.state.messages.value[1]?.content).toBe('Hi there!')
  })

  it('reset() clears state and returns to idle', () => {
    const engine = createMockChatEngine(simpleScenario)
    engine.controls.skipToEnd()
    engine.controls.reset()
    expect(engine.state.playbackState.value).toBe('idle')
    expect(engine.state.messages.value).toHaveLength(0)
    expect(engine.state.currentEventIndex.value).toBe(0)
  })

  it('setSpeed scales delay', () => {
    const engine = createMockChatEngine(simpleScenario)
    engine.controls.setSpeed(4)
    engine.controls.play()
    vi.advanceTimersByTime(100)
    expect(engine.state.playbackState.value).toBe('done')
  })

  it('dispose() clears pending timers', () => {
    const engine = createMockChatEngine(simpleScenario)
    engine.controls.play()
    vi.advanceTimersByTime(50)
    engine.dispose()
    vi.advanceTimersByTime(1000)
    expect(engine.state.playbackState.value).not.toBe('done')
  })
})
