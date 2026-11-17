/**
 * The composer's one primary button: what it does follows what is in the
 * composer, so microphone, send and stop share a single place at the end of
 * the toolbar and switching between them never moves another control.
 *
 * Kept as a pure function next to `TThreadComposer` so the decision has its own
 * tests; the component only renders what this returns.
 */

/**
 * - `stop`: a turn is running; the button ends it.
 * - `queue`: a turn is running, the composer queues while running
 *   (`queueWhileRunning`) and there is text or an attachment; the button hands
 *   the message over to go once the turn has ended. Stop is then a second
 *   button beside it, so the turn can still be ended.
 * - `end-dictation`: speech input is listening; the button ends the dictation
 *   (what was heard stays in the box).
 * - `send`: there is text or at least one attachment.
 * - `voice`: the composer is empty and speech input can work here.
 * - `send` with `enabled` false: empty, and no speech input.
 */
export type ComposerPrimaryActionKind = 'stop' | 'queue' | 'end-dictation' | 'send' | 'voice'

export interface ComposerPrimaryActionInput {
  /** A turn is running (the composer's `running`). */
  readonly running: boolean
  /** Speech input is listening. */
  readonly listening: boolean
  /** Text other than whitespace, or at least one attachment. */
  readonly hasContent: boolean
  /** Speech input is turned on and the page can use it. */
  readonly voiceAvailable: boolean
  /** Sending is refused (the composer's `disabled`). */
  readonly disabled: boolean
  /** A message written while a turn runs may be queued (the composer's `queueWhileRunning`). */
  readonly queueWhileRunning?: boolean
  /** Attachments are in the composer. */
  readonly hasFiles?: boolean
  /**
   * A queued message may carry attachments (the composer's `queueAttachments`, default true). When
   * false, a composer with attachments offers stop, and its content waits for the turn to end.
   */
  readonly queueAttachments?: boolean
}

export interface ComposerPrimaryAction {
  readonly kind: ComposerPrimaryActionKind
  /** Whether pressing the button does something. */
  readonly enabled: boolean
}

/**
 * Which action the primary button offers.
 *
 * A running turn wins over everything: while it runs the button is stop, even
 * with text (or a draft the product keeps) in the box. By default nothing is
 * queued: the text stays where it is, Enter does not send it, and it goes once
 * the turn has ended. With `queueWhileRunning` a box with content offers
 * `queue` instead (stop moves beside it); an empty box still offers stop.
 * Dictation comes next so it can always be ended from the same button that
 * started it, even after it has filled the box with text.
 */
export function composerPrimaryAction(input: ComposerPrimaryActionInput): ComposerPrimaryAction {
  if (input.running && canQueueContent(input)) return { kind: 'queue', enabled: !input.disabled }
  if (input.running) return { kind: 'stop', enabled: true }
  if (input.listening) return { kind: 'end-dictation', enabled: true }
  if (input.hasContent) return { kind: 'send', enabled: !input.disabled }
  if (input.voiceAvailable) return { kind: 'voice', enabled: true }
  return { kind: 'send', enabled: false }
}

/** While a turn runs: the composer queues and what is in it can be queued. */
export function canQueueContent(input: ComposerPrimaryActionInput): boolean {
  return !!input.queueWhileRunning && input.hasContent && (input.queueAttachments !== false || !input.hasFiles)
}
