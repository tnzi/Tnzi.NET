/**
 * A reply cut where its tool calls happened, so the cards sit inside the text
 * at the point the agent used them instead of all above it.
 *
 * External CLI agents report, for every tool call, how long the reply text was
 * when the call started (`ToolCallInfo.textOffset`). The server folds the same
 * events into the same text whether the reply is streaming or reloaded from the
 * thread, so an offset means the same thing live and after a refresh. The text
 * that follows a call starts with the blank line the server puts between text
 * blocks, which keeps every piece a complete markdown block.
 */
import type { ToolCallInfo } from '../../headless/useChat'

export interface ReplySegment {
  /** The text before the tool calls of this segment. */
  readonly text: string
  /** The calls made after that text; empty for the last segment. */
  readonly toolCalls: readonly ToolCallInfo[]
}

export interface SegmentedReply {
  /** Calls that report no position (built-in agents): shown ahead of the text, as before. */
  readonly lead: readonly ToolCallInfo[]
  /** The text and the calls in reading order. Always ends with a segment holding the rest of the text. */
  readonly segments: readonly ReplySegment[]
}

const hasOffset = (call: ToolCallInfo): call is ToolCallInfo & { textOffset: number } =>
  typeof call.textOffset === 'number' && Number.isFinite(call.textOffset) && call.textOffset >= 0

/** A piece of a reply in reading order: text, or tool calls made back to back. */
export type ReplyPart =
  | { readonly kind: 'text'; readonly text: string; readonly last: boolean }
  | { readonly kind: 'run'; readonly toolCalls: readonly ToolCallInfo[] }

/**
 * The reply as text and runs of tool calls. Calls with no reply text between them are
 * one run, wherever they came from: the calls that lead (no position) and those placed
 * in the text, and calls at different positions with only blank lines between them.
 * The text after the last call is always the last part, even blank (it is where the
 * caret of a streaming answer sits); other blank text is dropped.
 */
export function replyParts(reply: SegmentedReply): ReplyPart[] {
  const parts: ReplyPart[] = []
  const addRun = (calls: readonly ToolCallInfo[]): void => {
    if (calls.length === 0) return
    const previous = parts[parts.length - 1]
    if (previous?.kind === 'run') parts[parts.length - 1] = { kind: 'run', toolCalls: [...previous.toolCalls, ...calls] }
    else parts.push({ kind: 'run', toolCalls: [...calls] })
  }

  addRun(reply.lead)
  reply.segments.forEach((segment, i) => {
    const last = i === reply.segments.length - 1
    if (last || segment.text.trim()) parts.push({ kind: 'text', text: segment.text, last })
    addRun(segment.toolCalls)
  })
  return parts
}

/** Whether any call of the reply reports where in the text it happened. */
export function hasPlacedToolCalls(toolCalls: readonly ToolCallInfo[] | null | undefined): boolean {
  return (toolCalls ?? []).some(hasOffset)
}

/**
 * Split `content` at the offsets of its tool calls. Calls at the same offset
 * stay together, in the order they were made; an offset past the end of the
 * text (the text has not caught up yet) places the call at the end.
 */
export function segmentReply(
  content: string,
  toolCalls: readonly ToolCallInfo[] | null | undefined,
): SegmentedReply {
  const calls = toolCalls ?? []
  const lead = calls.filter((call) => !hasOffset(call))
  // A stable sort: calls at the same offset keep the order they were made in.
  const placed = calls
    .filter(hasOffset)
    .map((call, index) => ({ call, index, at: Math.min(call.textOffset, content.length) }))
    .sort((a, b) => a.at - b.at || a.index - b.index)

  const segments: ReplySegment[] = []
  let from = 0
  let i = 0
  while (i < placed.length) {
    const at = placed[i]!.at
    const group: ToolCallInfo[] = []
    while (i < placed.length && placed[i]!.at === at) {
      group.push(placed[i]!.call)
      i += 1
    }
    segments.push({ text: content.slice(from, at), toolCalls: group })
    from = at
  }
  segments.push({ text: content.slice(from), toolCalls: [] })
  return { lead, segments }
}
