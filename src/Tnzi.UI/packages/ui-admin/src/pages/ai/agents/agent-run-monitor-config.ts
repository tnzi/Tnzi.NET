/**
 * AgentRunMonitor pure-data config: the trace event shape its trace panel
 * renders. Kept loose so a trace record flows through unchanged.
 */
export interface RunTraceEvent {
  type?: string
  timestamp?: string
  content?: string
  raw?: unknown
}
