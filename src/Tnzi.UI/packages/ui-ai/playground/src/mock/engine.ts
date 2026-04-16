import { ref, type Ref } from 'vue'
import type { MockScenario, ScenarioEvent, PlaybackState, ArtifactMock, CitationMock } from './types'

export interface MockChatMessage {
  id: string
  role: 'user' | 'assistant'
  content: string
  reasoning?: string
  agentName?: string
  model?: string
  attachments?: unknown
  citations?: CitationMock[]
  isStreaming?: boolean
}

export interface WorkflowNodeRuntime {
  id: string
  status: 'idle' | 'running' | 'done' | 'error'
}

export interface MockChatEngineState {
  messages: Ref<MockChatMessage[]>
  playbackState: Ref<PlaybackState>
  currentEventIndex: Ref<number>
  speed: Ref<number>
  artifact: Ref<ArtifactMock | null>
  workflowNodeStatus: Ref<Record<string, WorkflowNodeRuntime['status']>>
}

export interface MockChatEngineControls {
  play(): void
  pause(): void
  skipToEnd(): void
  reset(): void
  setSpeed(speed: number): void
}

export interface MockChatEngine {
  state: MockChatEngineState
  controls: MockChatEngineControls
  dispose(): void
}

function newId(): string {
  return `msg-${Math.random().toString(36).slice(2, 10)}`
}

export function createMockChatEngine(scenario: MockScenario): MockChatEngine {
  const messages = ref<MockChatMessage[]>([])
  const playbackState = ref<PlaybackState>('idle')
  const currentEventIndex = ref(0)
  const speed = ref(1)
  const artifact = ref<ArtifactMock | null>(scenario.initial?.artifact ?? null)
  const workflowNodeStatus = ref<Record<string, WorkflowNodeRuntime['status']>>({})

  let timer: ReturnType<typeof setTimeout> | null = null

  function applyEvent(event: ScenarioEvent): void {
    switch (event.type) {
      case 'user-message': {
        messages.value.push({
          id: newId(),
          role: 'user',
          content: event.content,
          attachments: event.attachments,
        })
        break
      }
      case 'assistant-start': {
        messages.value.push({
          id: newId(),
          role: 'assistant',
          content: '',
          agentName: event.agentName,
          model: event.model,
          isStreaming: true,
        })
        break
      }
      case 'assistant-delta': {
        const last = messages.value[messages.value.length - 1]
        if (last && last.role === 'assistant') {
          last.content = last.content + event.text
        }
        break
      }
      case 'assistant-reasoning-delta': {
        const last = messages.value[messages.value.length - 1]
        if (last && last.role === 'assistant') {
          last.reasoning = (last.reasoning ?? '') + event.text
        }
        break
      }
      case 'citation': {
        const last = messages.value[messages.value.length - 1]
        if (last && last.role === 'assistant') {
          last.citations = [...(last.citations ?? []), event.source]
        }
        break
      }
      case 'artifact': {
        artifact.value = event.artifact
        break
      }
      case 'workflow-node-status': {
        workflowNodeStatus.value = { ...workflowNodeStatus.value, [event.nodeId]: event.status }
        break
      }
      case 'assistant-end': {
        const last = messages.value[messages.value.length - 1]
        if (last && last.role === 'assistant') {
          last.isStreaming = false
        }
        break
      }
      case 'tool-call':
      case 'tool-result': {
        const last = messages.value[messages.value.length - 1]
        if (last && last.role === 'assistant') {
          const marker = event.type === 'tool-call' ? `\n\n▶ ${event.name}` : `\n✓ ${event.name}`
          last.content = last.content + marker
        }
        break
      }
    }
  }

  function scheduleNext(): void {
    if (playbackState.value !== 'playing') return
    if (currentEventIndex.value >= scenario.events.length) {
      playbackState.value = 'done'
      return
    }
    const event = scenario.events[currentEventIndex.value]!
    const prevAt = currentEventIndex.value > 0 ? scenario.events[currentEventIndex.value - 1]!.at : 0
    const delay = Math.max(0, (event.at - prevAt) / speed.value)

    timer = setTimeout(() => {
      applyEvent(event)
      currentEventIndex.value = currentEventIndex.value + 1
      scheduleNext()
    }, delay)
  }

  function play(): void {
    if (playbackState.value === 'done') return
    playbackState.value = 'playing'
    scheduleNext()
  }

  function pause(): void {
    playbackState.value = 'paused'
    if (timer) {
      clearTimeout(timer)
      timer = null
    }
  }

  function skipToEnd(): void {
    if (timer) {
      clearTimeout(timer)
      timer = null
    }
    while (currentEventIndex.value < scenario.events.length) {
      applyEvent(scenario.events[currentEventIndex.value]!)
      currentEventIndex.value = currentEventIndex.value + 1
    }
    playbackState.value = 'done'
  }

  function reset(): void {
    if (timer) {
      clearTimeout(timer)
      timer = null
    }
    messages.value = []
    currentEventIndex.value = 0
    playbackState.value = 'idle'
    artifact.value = scenario.initial?.artifact ?? null
    workflowNodeStatus.value = {}
  }

  function setSpeed(next: number): void {
    speed.value = next
  }

  function dispose(): void {
    if (timer) {
      clearTimeout(timer)
      timer = null
    }
  }

  return {
    state: {
      messages,
      playbackState,
      currentEventIndex,
      speed,
      artifact,
      workflowNodeStatus,
    },
    controls: { play, pause, skipToEnd, reset, setSpeed },
    dispose,
  }
}
