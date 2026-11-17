/**
 * What a run's one line shows at its right end besides its words: the images its steps returned (as small
 * thumbnails, so a run that took screenshots shows them without being opened) and how many lines its steps added
 * and removed in files ("+12 -3").
 *
 * Images are the image blocks of the steps' results that were stored as attachments (`ToolResultBlockDto` with an
 * `attachmentId`). Line counts come from the arguments of the file-editing tools of Claude Code and similar CLIs:
 * `Edit` (`old_string` / `new_string`), `MultiEdit` (`edits`), `Write` (`content`, all of it added). Only steps that
 * completed count: a failed edit changed nothing.
 */
import type { ToolCallInfo } from '../../headless/useChat'
import { readableToolName } from './tool-call-status'
import { bareToolName } from './tool-steps'

/** One image a run's step returned. */
export interface RunImage {
  readonly attachmentId: string
  readonly mediaType?: string | null
  /** The call that returned it. */
  readonly toolCallId?: string | null
  /**
   * Where the image came from: the step's words as its run lists it ("Read a file"), when the caller gives them;
   * else its tool's readable name ("Read", "Screenshot").
   */
  readonly source: string
}

/**
 * The images the run's steps returned, in order. `sourceOf` names the step each came from, by the call and its index in
 * `toolCalls` (the run passes the step's localized words); without it, or when it says nothing, the tool's readable name stands in.
 */
export function runImages(
  toolCalls: readonly ToolCallInfo[],
  sourceOf?: (call: ToolCallInfo, index: number) => string | null | undefined,
): RunImage[] {
  const images: RunImage[] = []
  for (const [index, call] of toolCalls.entries()) {
    for (const block of call.result ?? []) {
      if (block.type === 'image' && block.attachmentId) {
        images.push({
          attachmentId: block.attachmentId,
          mediaType: block.mediaType ?? null,
          toolCallId: call.id ?? null,
          source: sourceOf?.(call, index) || readableToolName(call.name),
        })
      }
    }
  }
  return images
}

/** Lines added and removed in files by a run's steps. */
export interface LineChanges {
  readonly added: number
  readonly removed: number
}

const lines = (text: unknown): string[] => (typeof text === 'string' && text.length > 0 ? text.replace(/\r\n/g, '\n').replace(/\n$/, '').split('\n') : [])

/**
 * Lines added and removed going from `before` to `after`: a line counts as removed when `before` has more copies of it
 * than `after`, as added the other way round. The order of lines does not matter here, which is what makes a moved
 * line count twice in a real diff and not here; for a count on a summary line that is close enough.
 */
export function lineDiff(before: string, after: string): LineChanges {
  const counts = new Map<string, number>()
  for (const line of lines(before)) counts.set(line, (counts.get(line) ?? 0) + 1)
  let added = 0
  for (const line of lines(after)) {
    const left = counts.get(line) ?? 0
    if (left > 0) counts.set(line, left - 1)
    else added += 1
  }
  let removed = 0
  for (const left of counts.values()) removed += left
  return { added, removed }
}

/** What one completed call changed in files; zero for a call that edits nothing. */
export function callLineChanges(call: ToolCallInfo): LineChanges {
  if (call.status && call.status !== 'completed') return { added: 0, removed: 0 }
  if (call.isSuccess === false) return { added: 0, removed: 0 }
  const input = call.input && typeof call.input === 'object' ? (call.input as Record<string, unknown>) : null
  if (!input) return { added: 0, removed: 0 }
  switch (bareToolName(call.name)) {
    case 'Edit':
      return lineDiff(String(input.old_string ?? ''), String(input.new_string ?? ''))
    case 'MultiEdit': {
      const edits = Array.isArray(input.edits) ? input.edits : []
      return edits.reduce<LineChanges>((sum, edit) => {
        const e = edit && typeof edit === 'object' ? (edit as Record<string, unknown>) : {}
        const change = lineDiff(String(e.old_string ?? ''), String(e.new_string ?? ''))
        return { added: sum.added + change.added, removed: sum.removed + change.removed }
      }, { added: 0, removed: 0 })
    }
    case 'Write':
      return { added: lines(input.content).length, removed: 0 }
    default:
      return { added: 0, removed: 0 }
  }
}

/** What a run's steps changed in files. */
export function runLineChanges(toolCalls: readonly ToolCallInfo[]): LineChanges {
  return toolCalls.reduce<LineChanges>((sum, call) => {
    const change = callLineChanges(call)
    return { added: sum.added + change.added, removed: sum.removed + change.removed }
  }, { added: 0, removed: 0 })
}
