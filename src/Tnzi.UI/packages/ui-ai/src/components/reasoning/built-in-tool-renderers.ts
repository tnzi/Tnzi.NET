/**
 * The tool views this package ships for tools it knows: the Claude Code CLI's
 * `WebSearch` and `WebFetch`, each with its view and its words for a run's summary, the
 * words (no view of their own) for its file and shell tools (`Read`, `Edit`, `MultiEdit`,
 * `Write`, `Bash`, `Grep`, `Glob`: "Read 2 files, ran a command"), and for the steps where
 * the agent turns to the person: `AskUserQuestion`, `EnterPlanMode` and `ExitPlanMode`. A plan sent back or a question
 * left unanswered comes back from the CLI as a failed call; those two read as what
 * happened (`declined`), not as an error.
 * They apply to those names matched exactly (an MCP server's own
 * `mcp__<server>__WebSearch` is a different tool), after the product's registrations:
 * a product entry for the same name wins field by field (`mergeToolRendererEntries`),
 * so a product can replace the view and keep the words, or the reverse. Registering
 * `TToolCallCard` under the name brings the generic view back.
 */
import { formatAiMessage } from '../../i18n/index';
import {
  mergeToolRendererEntries,
  registeredRenderer,
  resolveToolRendererEntry,
  type ToolRendererEntry,
  type ToolRendererMap,
} from '../chat/chat-render-context';
import TWebSearchCall from './TWebSearchCall.vue';
import TWebFetchCall from './TWebFetchCall.vue';
import type { ToolStepDescriber } from './tool-steps';
import { toolResultText } from './tool-views';
import { inputString, parseWebSearchResult, webFetchTarget, WEB_FETCH_TOOL, WEB_SEARCH_TOOL } from './web-tools';

/** "Searched the web": searches are not counted, as nobody reads "searched the web 3 times". */
export const describeWebSearch: ToolStepDescriber = (toolCall, t) => ({
  kind: WEB_SEARCH_TOOL,
  running: t.tools.webSearchRunning,
  done: t.tools.webSearchDone,
  failed: t.tools.webSearchFailed,
  subject: inputString(toolCall.input, 'query') ?? parseWebSearchResult(toolResultText(toolCall)).query,
});

/** "Read a page", "Read 5 pages". */
export const describeWebFetch: ToolStepDescriber = (toolCall, t) => ({
  kind: WEB_FETCH_TOOL,
  running: t.tools.readingPage,
  done: t.tools.readPage,
  failed: t.tools.readPageFailed,
  many: (count) => formatAiMessage(t.tools.readPages, { count }),
  subject: webFetchTarget(toolCall.input)?.domain ?? inputString(toolCall.input, 'url'),
});

/** The CLI's tool for asking the person questions with options. */
export const ASK_USER_QUESTION_TOOL = 'AskUserQuestion';
/** The CLI's tool for starting to plan before changing anything. */
export const ENTER_PLAN_MODE_TOOL = 'EnterPlanMode';
/** The CLI's tool for handing the plan over and asking to start. */
export const EXIT_PLAN_MODE_TOOL = 'ExitPlanMode';

/** The first question asked (its short header when it has one). */
function firstQuestion(input: unknown): string | null {
  const questions = (input as { questions?: unknown } | null | undefined)?.questions;
  const first = Array.isArray(questions) ? (questions[0] as { header?: unknown; question?: unknown } | undefined) : undefined;
  const text = typeof first?.header === 'string' && first.header.trim() ? first.header : first?.question;
  return typeof text === 'string' && text.trim() ? text.trim() : null;
}

/** The plan's first line, without its heading marks ("Plan: create hello.py"). */
function planTitle(input: unknown): string | null {
  const plan = inputString(input, 'plan');
  const line = plan?.split('\n').map((l) => l.replace(/^#+\s*/, '').trim()).find((l) => l !== '');
  return line ?? null;
}

/** "Asking you a question" / "Got your answer" / "Question left unanswered". */
export const describeAskUserQuestion: ToolStepDescriber = (toolCall, t) => ({
  kind: ASK_USER_QUESTION_TOOL,
  running: t.tools.askQuestionRunning,
  done: t.tools.askQuestionDone,
  declined: t.tools.askQuestionDeclined,
  failed: t.tools.askQuestionFailed,
  subject: firstQuestion(toolCall.input),
});

/** "Started a plan". */
export const describeEnterPlanMode: ToolStepDescriber = (_toolCall, t) => ({
  kind: ENTER_PLAN_MODE_TOOL,
  running: t.tools.enterPlanModeRunning,
  done: t.tools.enterPlanModeDone,
  failed: t.tools.enterPlanModeFailed,
  subject: null,
});

/** "Waiting for the plan to be approved" / "Plan approved" / "Plan sent back". */
export const describeExitPlanMode: ToolStepDescriber = (toolCall, t) => ({
  kind: EXIT_PLAN_MODE_TOOL,
  running: t.tools.exitPlanModeRunning,
  done: t.tools.exitPlanModeDone,
  declined: t.tools.exitPlanModeDeclined,
  failed: t.tools.exitPlanModeFailed,
  subject: planTitle(toolCall.input),
});

/** The file a file tool acted on, by its name ("Login.vue"). */
function fileName(input: unknown): string | null {
  const path = inputString(input, 'file_path') ?? inputString(input, 'path')
  return path ? path.split(/[\\/]/).filter(Boolean).pop() ?? path : null
}

/** "Read a file", "Read 2 files". */
export const describeReadFile: ToolStepDescriber = (toolCall, t) => ({
  kind: 'read-file',
  running: t.tools.readingFile,
  done: t.tools.readFile,
  failed: t.tools.readFileFailed,
  many: (count) => formatAiMessage(t.tools.readFiles, { count }),
  subject: fileName(toolCall.input),
});

/** "Edited a file", "Edited 3 files" (`Edit` and `MultiEdit` alike). */
export const describeEditFile: ToolStepDescriber = (toolCall, t) => ({
  kind: 'edit-file',
  running: t.tools.editingFile,
  done: t.tools.editedFile,
  failed: t.tools.editFileFailed,
  many: (count) => formatAiMessage(t.tools.editedFiles, { count }),
  subject: fileName(toolCall.input),
});

/** "Wrote a file", "Wrote 2 files". */
export const describeWriteFile: ToolStepDescriber = (toolCall, t) => ({
  kind: 'write-file',
  running: t.tools.writingFile,
  done: t.tools.wroteFile,
  failed: t.tools.writeFileFailed,
  many: (count) => formatAiMessage(t.tools.wroteFiles, { count }),
  subject: fileName(toolCall.input),
});

/** "Ran a command", "Ran 3 commands" (a command started in the background reads as a task instead). */
export const describeCommand: ToolStepDescriber = (toolCall, t) => ({
  kind: 'command',
  running: t.tools.runningCommand,
  done: t.tools.ranCommand,
  failed: t.tools.commandFailed,
  many: (count) => formatAiMessage(t.tools.ranCommands, { count }),
  subject: inputString(toolCall.input, 'description') ?? inputString(toolCall.input, 'command'),
});

/** "Searched the code" (`Grep` and `Glob`; not counted, like web searches). */
export const describeCodeSearch: ToolStepDescriber = (toolCall, t) => ({
  kind: 'search-code',
  running: t.tools.searchingCode,
  done: t.tools.searchedCode,
  failed: t.tools.searchCodeFailed,
  subject: inputString(toolCall.input, 'pattern'),
});

export const BUILT_IN_TOOL_RENDERERS: ToolRendererMap = Object.freeze({
  [WEB_SEARCH_TOOL]: { component: TWebSearchCall, describe: describeWebSearch },
  [WEB_FETCH_TOOL]: { component: TWebFetchCall, describe: describeWebFetch },
  [ASK_USER_QUESTION_TOOL]: { describe: describeAskUserQuestion },
  [ENTER_PLAN_MODE_TOOL]: { describe: describeEnterPlanMode },
  [EXIT_PLAN_MODE_TOOL]: { describe: describeExitPlanMode },
  Read: { describe: describeReadFile },
  Edit: { describe: describeEditFile },
  MultiEdit: { describe: describeEditFile },
  Write: { describe: describeWriteFile },
  Bash: { describe: describeCommand },
  Grep: { describe: describeCodeSearch },
  Glob: { describe: describeCodeSearch },
});

/**
 * Everything known about how to show a tool: the product's registration (matched as
 * `resolveToolRenderer` does), filled in from the package's own for the same exact name.
 */
export function resolveToolEntry(name: string | null | undefined, renderers: ToolRendererMap): ToolRendererEntry {
  return mergeToolRendererEntries(
    resolveToolRendererEntry(name, renderers),
    name ? registeredRenderer(BUILT_IN_TOOL_RENDERERS, name) : undefined,
  );
}
