/**
 * What the message renderers need from the product, handed down the component tree:
 *
 *   - **tool renderers by tool name**: a product shows its own tools its own way (a
 *     screenshot tool as a picture, a device tool as a device card), and every other tool
 *     falls back to the generic card (`TToolCallCard`). A registration may also say how
 *     the step reads in a run's one-line summary and keep it out of the fold
 *     (`ToolRendererEntry`);
 *   - **attachment URLs**: stored attachments are read with the signed-in client, which a
 *     plain `<img src>` cannot do, so the renderers ask for an object URL
 *     (`useChatThreads().resolveAttachmentUrl`);
 *   - **file links**: what a file card links to (a file the agent handed over carries the
 *     URI its tool gave; where that opens is the product's own storage scheme).
 *
 * `TChatApp` provides it from its props (`toolRenderers`, `resolveAttachmentUrl`,
 * `fileHref`); a custom shell calls `provideChatRenderContext` itself. Renderers registered
 * for the whole app (`defineChatApp({ toolRenderers })`, or `provideToolRenderers`) apply
 * under every shell, and a shell's own map wins on the same name.
 */
import { computed, inject, provide, unref, type Component, type ComputedRef, type InjectionKey, type Ref } from 'vue';
import type { ToolCallInfo } from '../../headless/useChat';
import type { ToolStepDescribe } from '../reasoning/tool-steps';

/** A file shown as a card: an attachment, or a file a tool handed over. */
export interface ChatFileRef {
  name?: string | null;
  mediaType?: string | null;
  size?: number | null;
  /** A stored chat attachment (an upload, or an image a tool returned). */
  attachmentId?: string | null;
  /** A file a tool handed over: the URI the tool gave. */
  uri?: string | null;
}

/** Props every tool renderer receives. */
export interface ToolRendererProps {
  toolCall: ToolCallInfo;
  /** The message is still streaming (a call without a result is running, not waiting). */
  streaming: boolean;
}

/**
 * A tool's registration when there is more to say than its component: how its step reads
 * in a run's summary, and whether it stays in view instead of folding into the run.
 * Every field is optional; what is left out comes from the view this package ships for
 * the tool (the CLI's `WebSearch` / `WebFetch`) or from the defaults.
 */
export interface ToolRendererEntry {
  /** Draws the step. Without it the tool keeps the package's view (built-in or generic). */
  readonly component?: Component;
  /**
   * The step's words: the phrase while it runs, once done and when it failed, the kind it
   * is counted under in a run's summary ("Read 5 pages") and its subject. A function is
   * called per call with the package's dictionary. Without it the step reads as its
   * tool's name ("Used Read File").
   */
  readonly describe?: ToolStepDescribe;
  /**
   * Keep the step in view: shown under its run's line, whole, instead of folded into the
   * run's details (a file handed to the user, with Open and Download). A function decides
   * per call (say, only a call that succeeded). The step still counts in the summary.
   */
  readonly alwaysVisible?: boolean | ((toolCall: ToolCallInfo) => boolean);
}

/** A tool's renderer: its component, or an entry with its words and placement. */
export type ToolRenderer = Component | ToolRendererEntry;

/** Tool renderers by tool name. See {@link resolveToolRenderer} for how names match. */
export type ToolRendererMap = Readonly<Record<string, ToolRenderer>>;

export interface ChatRenderContext {
  toolRenderers: ToolRendererMap;
  /** An object URL for a stored attachment of the open thread; null when it cannot be read. */
  resolveAttachmentUrl?: (attachmentId: string) => Promise<string | null>;
  /**
   * Where a file card links to. Return a URL, or null/undefined for the default: the
   * attachment's own content for an attachment, the tool's URI when it is an `http(s)` or
   * site-relative address, otherwise no link.
   */
  fileHref?: (file: ChatFileRef) => string | null | undefined;
}

const RENDER_CONTEXT_KEY: InjectionKey<ComputedRef<ChatRenderContext>> = Symbol('tnzi-ui-ai:chat-render-context');
const APP_TOOL_RENDERERS_KEY: InjectionKey<ToolRendererMap> = Symbol('tnzi-ui-ai:tool-renderers');

const EMPTY: ChatRenderContext = { toolRenderers: {} };

/**
 * Register tool renderers for everything below (or, with `app.provide`, for the whole app):
 * `defineChatApp({ toolRenderers })` does this.
 */
export function provideToolRenderers(renderers: ToolRendererMap): void {
  provide(APP_TOOL_RENDERERS_KEY, renderers);
}

/** The key `provideToolRenderers` uses, for `app.provide` outside a component. */
export const TOOL_RENDERERS_KEY = APP_TOOL_RENDERERS_KEY;

/** Provide the render context to the message renderers below. */
export function provideChatRenderContext(context: ChatRenderContext | Ref<ChatRenderContext> | ComputedRef<ChatRenderContext>): void {
  const appRenderers = inject(APP_TOOL_RENDERERS_KEY, {});
  provide(
    RENDER_CONTEXT_KEY,
    computed(() => {
      const value = unref(context);
      return { ...value, toolRenderers: { ...appRenderers, ...value.toolRenderers } };
    }),
  );
}

/** The render context, or an empty one (app-wide tool renderers only) outside a provider. */
export function useChatRenderContext(): ComputedRef<ChatRenderContext> {
  const provided = inject(RENDER_CONTEXT_KEY, null);
  if (provided) return provided;
  const appRenderers = inject(APP_TOOL_RENDERERS_KEY, {});
  return computed(() => ({ ...EMPTY, toolRenderers: appRenderers }));
}

/**
 * The renderer for a tool: by its exact name, else by the part after the last `__`.
 *
 * Tools a CLI agent reaches over MCP carry their server as a prefix (claude names them
 * `mcp__<server>__<tool>`), so a renderer registered as `take_screenshot` also matches
 * `mcp__acme__take_screenshot`. Register the full name to tell two servers' tools apart.
 */
export function resolveToolRenderer(name: string | null | undefined, renderers: ToolRendererMap): Component | undefined {
  return resolveToolRendererEntry(name, renderers)?.component;
}

/**
 * The registration for a tool, as an entry (a bare component becomes `{ component }`),
 * matched the same way as {@link resolveToolRenderer}; undefined when there is none.
 */
export function resolveToolRendererEntry(name: string | null | undefined, renderers: ToolRendererMap): ToolRendererEntry | undefined {
  if (!name) return undefined;
  const exact = registeredRenderer(renderers, name);
  if (exact) return exact;
  const separator = name.lastIndexOf('__');
  return separator >= 0 ? registeredRenderer(renderers, name.slice(separator + 2)) : undefined;
}

/**
 * The registration under exactly `name`, as an entry. Own entries only: a map is a plain
 * object, and a tool called `constructor` or `toString` must not resolve to what every
 * object inherits.
 */
export function registeredRenderer(renderers: ToolRendererMap, name: string): ToolRendererEntry | undefined {
  if (!Object.prototype.hasOwnProperty.call(renderers, name)) return undefined;
  const value = renderers[name];
  if (!value) return undefined;
  return isToolRendererEntry(value) ? value : { component: value as Component };
}

/**
 * Whether a registration is an entry rather than a component. A component is a function
 * or an object with `setup`, `render` or `template`; an entry has none of those and
 * carries at least one entry field.
 */
export function isToolRendererEntry(value: ToolRenderer): value is ToolRendererEntry {
  if (typeof value !== 'object' || value === null) return false;
  if ('setup' in value || 'render' in value || 'template' in value) return false;
  return 'component' in value || 'describe' in value || 'alwaysVisible' in value;
}

/**
 * Two registrations for one tool, field by field: the product's wins where it says
 * something, the package's fills the rest.
 */
export function mergeToolRendererEntries(
  product: ToolRendererEntry | undefined,
  builtIn: ToolRendererEntry | undefined,
): ToolRendererEntry {
  return {
    component: product?.component ?? builtIn?.component,
    describe: product?.describe ?? builtIn?.describe,
    alwaysVisible: product?.alwaysVisible ?? builtIn?.alwaysVisible,
  };
}

/** Whether a step stays in view rather than folding into its run. Never throws. */
export function isAlwaysVisible(toolCall: ToolCallInfo, entry: ToolRendererEntry | undefined): boolean {
  const rule = entry?.alwaysVisible;
  if (typeof rule !== 'function') return rule === true;
  try {
    return rule(toolCall) === true;
  } catch {
    return false;
  }
}

/**
 * The default link of a tool-handed file: its URI when a browser can open it as is (an
 * `http(s)` or site-relative address), otherwise none. A product whose tools hand over
 * files by its own scheme (`storage://…`, a bare id) supplies `fileHref`.
 */
export function defaultToolFileHref(file: ChatFileRef): string | null {
  const uri = file.uri?.trim();
  if (!uri) return null;
  return /^https?:\/\//i.test(uri) || (uri.startsWith('/') && !uri.startsWith('//')) ? uri : null;
}
