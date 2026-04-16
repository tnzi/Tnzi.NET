/**
 * MockChatEngine type definitions.
 *
 * Scenario events are a timed stream. Each event has an `at` field
 * (ms offset from scenario start). The engine schedules events with
 * setTimeout scaled by the current speed multiplier.
 */

export interface AttachmentMock {
  id: string
  name: string
  mime: string
  sizeBytes: number
  previewUrl?: string
}

export interface CitationMock {
  id: string
  title: string
  snippet: string
  url?: string
  pageNumber?: number
}

export interface ArtifactMock {
  id: string
  title: string
  kind: 'code' | 'html' | 'markdown'
  language?: string
  content: string
}

export interface UsageMock {
  promptTokens: number
  completionTokens: number
  totalTokens: number
}

export type ScenarioEvent =
  | { at: number; type: 'user-message'; content: string; attachments?: readonly AttachmentMock[] }
  | { at: number; type: 'assistant-start'; agentName?: string; model?: string }
  | { at: number; type: 'assistant-delta'; text: string }
  | { at: number; type: 'assistant-reasoning-delta'; text: string }
  | { at: number; type: 'tool-call'; name: string; input: unknown }
  | { at: number; type: 'tool-result'; name: string; output: unknown }
  | { at: number; type: 'citation'; source: CitationMock }
  | { at: number; type: 'artifact'; artifact: ArtifactMock }
  | { at: number; type: 'workflow-node-status'; nodeId: string; status: 'running' | 'done' | 'error' }
  | { at: number; type: 'assistant-end'; usage?: UsageMock }

export interface ScenarioMeta {
  id: string
  title: string
  description: string
  category: 'conversation' | 'reasoning' | 'knowledge' | 'artifact' | 'agent' | 'embed'
  icon: string
  componentsShowcased: readonly string[]
}

export interface ScenarioInitialState {
  artifact?: ArtifactMock
  workflowNodes?: ReadonlyArray<{ id: string; label: string; x: number; y: number }>
  workflowEdges?: ReadonlyArray<{ id: string; source: string; target: string }>
}

export interface MockScenario {
  meta: ScenarioMeta
  events: readonly ScenarioEvent[]
  initial?: ScenarioInitialState
}

export type PlaybackState = 'idle' | 'playing' | 'paused' | 'done'
