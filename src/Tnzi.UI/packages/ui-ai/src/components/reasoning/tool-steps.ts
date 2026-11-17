/**
 * How tool steps read when several run back to back: the one line that stands for the
 * whole run.
 *
 * While the run is in progress the line says what the current step is doing ("Reading a
 * page"); once it is done it sums the run up by kind ("Read 5 pages, searched the web").
 * The words come from each step's description (`ToolStepDescription`): the built-in web
 * tools bring theirs, a product gives its own next to its renderer (`ToolRendererEntry`
 * `describe`), and any other tool reads as its name ("Used Read File").
 *
 * The phrases are written as a line would start ("Read a page"); the summary lowers the
 * first letter of every part after the first, so a product writes each phrase once.
 */
import { formatAiMessage } from '../../i18n/index';
import type { AiLocaleMessages } from '../../locales/en';
import type { ToolCallInfo } from '../../headless/useChat';
import { readableToolName, toolInputSummary, type ToolCallState } from './tool-call-status';

/**
 * What one tool step says, in the interface language. Every phrase is written as the
 * start of a line ("Read a page", "读取了网页").
 */
export interface ToolStepDescription {
  /** While the step runs: "Reading a page". */
  readonly running: string;
  /** Once it is done: "Read a page". */
  readonly done: string;
  /** When it failed: "Couldn't read a page". */
  readonly failed: string;
  /**
   * For a tool whose failing result is a person's answer rather than an error (a plan sent
   * back, a question left unanswered): the phrase for that. With it, such a step reads as
   * finished, in the normal tone, and never counts as a failure in a run's summary.
   */
  readonly declined?: string;
  /**
   * Several done steps of this kind in one run: `(5) => "Read 5 pages"`. Without it,
   * several read as `done` once ("Searched the web"), which suits steps nobody counts.
   */
  readonly many?: (count: number) => string;
  /**
   * Steps of one kind are counted together in a run's summary, across tools: give
   * `read_file` and `browser_read` the kind `read` and three of them read "Read 3 files".
   * Defaults to the tool's name (after any `mcp__<server>__` prefix).
   */
  readonly kind?: string;
  /** What the step acted on (a query, a path), shown after the phrase in the generic view. */
  readonly subject?: string | null;
  /**
   * For a step that goes on after its run (a sub-agent or a command in the background): how a finished run's line
   * says it is still going ("A background command running"). With it, such a step is counted under its own kind
   * instead of as "not finished".
   */
  readonly ongoing?: string;
  /** Several ongoing steps of this kind in one run: `(2) => "2 background commands running"`. */
  readonly manyOngoing?: (count: number) => string;
}

/**
 * A step's description from the call. Receives the package's dictionary (a product
 * usually reads its own instead); called again whenever the call or the language
 * changes, so it should only compute.
 */
export type ToolStepDescriber = (toolCall: ToolCallInfo, messages: AiLocaleMessages) => ToolStepDescription;

/** A description given as it is, or a function that writes one per call. */
export type ToolStepDescribe = ToolStepDescription | ToolStepDescriber;

/** One step of a run, as the summary needs it. */
export interface ToolRunStep {
  readonly description: ToolStepDescription;
  readonly state: ToolCallState;
}

/** One piece of a run's line, ready to show: joined to the piece before it, and in its tone. */
export interface ToolRunSummaryPart {
  /** The words, with the joiner in front of every part after the first. */
  readonly text: string;
  /**
   * `done`: what went right; `failed`: what failed and was never got past (shown as such);
   * `recovered`: steps that failed and were then done again (said, not shown as a failure);
   * `unfinished`: what the turn ended before.
   */
  readonly tone: 'done' | 'failed' | 'recovered' | 'unfinished';
}

/** A finished run's line. */
export interface ToolRunSummary {
  /** The whole line as text ("Read 5 pages, searched the web, couldn't read a page"). */
  readonly text: string;
  readonly parts: readonly ToolRunSummaryPart[];
  /** How many steps failed, recovered or not. */
  readonly failedCount: number;
  /**
   * How many of them the run never got past: no later step of the same kind completed.
   * The line reads as failed only when this is above zero.
   */
  readonly unrecoveredCount: number;
}

/** Kinds named in a summary before the rest collapse into "N more". */
const MAX_SUMMARY_KINDS = 3;

/** The tool's own name, without the `mcp__<server>__` prefix a CLI agent gives MCP tools. */
export function bareToolName(name: string | null | undefined): string {
  const raw = name ?? '';
  const separator = raw.lastIndexOf('__');
  return separator >= 0 ? raw.slice(separator + 2) : raw;
}

/** How a tool without a description reads: by its name ("Used Read File"). */
export function defaultToolStepDescription(toolCall: ToolCallInfo, messages: AiLocaleMessages): ToolStepDescription {
  const tool = readableToolName(toolCall.name);
  return {
    running: formatAiMessage(messages.tools.usingTool, { tool }),
    done: formatAiMessage(messages.tools.usedTool, { tool }),
    failed: formatAiMessage(messages.tools.toolFailed, { tool }),
    many: (count) => formatAiMessage(messages.tools.usedToolTimes, { tool, count }),
    kind: bareToolName(toolCall.name),
    subject: toolInputSummary(toolCall.input),
  };
}

/**
 * A step's description: the one given (a function is called; one that throws or answers
 * nothing usable falls back), filled in with the defaults for whatever it leaves out.
 */
export function describeToolStep(
  toolCall: ToolCallInfo,
  describe: ToolStepDescribe | null | undefined,
  messages: AiLocaleMessages,
): ToolStepDescription {
  const fallback = defaultToolStepDescription(toolCall, messages);
  let given: Partial<ToolStepDescription> | null = null;
  try {
    given = typeof describe === 'function' ? describe(toolCall, messages) : describe ?? null;
  } catch {
    given = null;
  }
  if (!given || typeof given !== 'object') return fallback;
  const text = (value: unknown, otherwise: string): string =>
    typeof value === 'string' && value.trim() ? value : otherwise;
  return {
    running: text(given.running, fallback.running),
    done: text(given.done, fallback.done),
    failed: text(given.failed, fallback.failed),
    declined: typeof given.declined === 'string' && given.declined.trim() ? given.declined : undefined,
    many: typeof given.many === 'function' ? given.many : undefined,
    kind: text(given.kind, fallback.kind ?? ''),
    subject: given.subject === undefined ? fallback.subject : given.subject,
    ongoing: typeof given.ongoing === 'string' && given.ongoing.trim() ? given.ongoing : undefined,
    manyOngoing: typeof given.manyOngoing === 'function' ? given.manyOngoing : undefined,
  };
}

/** Whether the step "failed" only because a person said no (see `ToolStepDescription.declined`). */
export function isDeclinedStep(step: ToolRunStep): boolean {
  return step.state === 'failed' && !!step.description.declined;
}

/** The state a step is drawn in: a declined step is a finished one, not a failed one. */
export function shownStepState(step: ToolRunStep): ToolCallState {
  return isDeclinedStep(step) ? 'completed' : step.state;
}

/** The phrase for a step in its state: a step that never answered still reads as under way. */
export function stepPhrase(step: ToolRunStep): string {
  if (isDeclinedStep(step)) return step.description.declined!;
  if (step.state === 'failed') return step.description.failed;
  if (step.state === 'completed') return step.description.done;
  return step.description.running;
}

/**
 * The line of a run still in progress: what the latest step is doing (the last one
 * running when several run at once), or what it just did.
 */
export function runningRunLabel(steps: readonly ToolRunStep[]): string {
  const current = [...steps].reverse().find((step) => step.state === 'running') ?? steps[steps.length - 1];
  return current ? stepPhrase(current) : '';
}

/**
 * Whether a failed step was got past: a later step of the same kind completed (the
 * model retried the tool, or did the same with a tool of the kind). A later success of
 * another kind does not count: it says the run went on, not that what failed was done.
 */
function isRecovered(steps: readonly ToolRunStep[], index: number): boolean {
  const kind = steps[index]!.description.kind ?? '';
  return steps.some((later, i) => i > index && later.state === 'completed' && (later.description.kind ?? '') === kind);
}

/**
 * The line of a finished run: its done steps by kind, the kinds with the most steps
 * first (ties in the order they first ran), at most three kinds and then "N more";
 * failures the run never got past, failures it retried past, and unfinished steps said
 * apart.
 */
export function summarizeToolRun(steps: readonly ToolRunStep[], messages: AiLocaleMessages): ToolRunSummary {
  const groups: Array<{ kind: string; first: ToolStepDescription; count: number; order: number }> = [];
  const failed: ToolRunStep[] = [];
  let recovered = 0;
  let unfinished = 0;

  steps.forEach((given, order) => {
    // A declined step is a finished outcome of its own ("Plan sent back"), counted apart from the approved ones
    const step: ToolRunStep = isDeclinedStep(given)
      ? {
          state: 'completed',
          description: {
            ...given.description,
            done: given.description.declined!,
            many: undefined,
            kind: `${given.description.kind ?? ''}:declined`,
          },
        }
      : given;
    if (step.state === 'failed') {
      if (isRecovered(steps, order)) recovered += 1;
      else failed.push(step);
      return;
    }
    const ongoing = step.state === 'running' && !!step.description.ongoing;
    if (step.state !== 'completed' && !ongoing) {
      unfinished += 1;
      return;
    }
    // A step still going on after the run (a background task) is counted apart, with its own words
    const kind = ongoing ? `${step.description.kind ?? ''}:ongoing` : step.description.kind ?? '';
    const description: ToolStepDescription = ongoing
      ? { ...step.description, done: step.description.ongoing!, many: step.description.manyOngoing }
      : step.description;
    const group = groups.find((g) => g.kind === kind);
    if (group) group.count += 1;
    else groups.push({ kind, first: description, count: 1, order });
  });

  const ranked = [...groups].sort((a, b) => b.count - a.count || a.order - b.order);
  const named = ranked.slice(0, MAX_SUMMARY_KINDS).map((g) =>
    g.count > 1 && g.first.many ? g.first.many(g.count) : g.first.done,
  );
  const rest = ranked.slice(MAX_SUMMARY_KINDS).reduce((sum, g) => sum + g.count, 0);
  if (rest > 0) named.push(formatAiMessage(messages.tools.moreSteps, { count: rest }));

  const pieces: Array<{ text: string; tone: ToolRunSummaryPart['tone'] }> = named.map((text) => ({ text, tone: 'done' }));
  if (failed.length === 1) pieces.push({ text: failed[0]!.description.failed, tone: 'failed' });
  else if (failed.length > 1) pieces.push({ text: formatAiMessage(messages.tools.stepsFailed, { count: failed.length }), tone: 'failed' });
  if (recovered === 1) pieces.push({ text: messages.tools.stepRecovered, tone: 'recovered' });
  else if (recovered > 1) pieces.push({ text: formatAiMessage(messages.tools.stepsRecovered, { count: recovered }), tone: 'recovered' });
  if (unfinished > 0) pieces.push({ text: formatAiMessage(messages.tools.stepsNotFinished, { count: unfinished }), tone: 'unfinished' });

  const parts = pieces
    .filter((piece) => piece.text.trim() !== '')
    .map((piece, i) => ({ text: i === 0 ? piece.text : messages.tools.summaryJoin + lowerFirst(piece.text), tone: piece.tone }));
  return {
    text: parts.map((part) => part.text).join(''),
    parts,
    failedCount: failed.length + recovered,
    unrecoveredCount: failed.length,
  };
}

/**
 * Every part after the first starts lower case ("Read 5 pages, searched the web"),
 * unless it opens with an abbreviation ("SQL", "PDF") that has to stay as it is.
 * Scripts without case (Chinese) are left as they are.
 */

function lowerFirst(text: string): string {
  const first = text.charAt(0);
  const second = text.charAt(1);
  if (first === first.toLocaleLowerCase() || (second && second === second.toLocaleUpperCase() && second !== second.toLocaleLowerCase())) {
    return text;
  }
  return first.toLocaleLowerCase() + text.slice(1);
}
