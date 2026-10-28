/**
 * The tool views this package ships for tools it knows: the Claude Code CLI's
 * `WebSearch` and `WebFetch`, each with its view and its words for a run's summary.
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

export const BUILT_IN_TOOL_RENDERERS: ToolRendererMap = Object.freeze({
  [WEB_SEARCH_TOOL]: { component: TWebSearchCall, describe: describeWebSearch },
  [WEB_FETCH_TOOL]: { component: TWebFetchCall, describe: describeWebFetch },
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
