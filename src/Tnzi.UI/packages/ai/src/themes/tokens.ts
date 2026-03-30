/**
 * AI theme token definitions.
 * HSL values (without hsl() wrapper) for CSS variable injection.
 */

export interface AiThemeTokens {
  /** User message bubble background */
  readonly userBubble: string;
  /** Assistant message bubble background */
  readonly assistantBubble: string;
  /** Reasoning/thinking block background */
  readonly reasoningBg: string;
  /** Tool call block background */
  readonly toolCallBg: string;
  /** Streaming cursor color */
  readonly streamingCursor: string;
  /** Code block background */
  readonly codeBg: string;
  /** Active workflow node */
  readonly nodeActive: string;
  /** Completed workflow node */
  readonly nodeCompleted: string;
  /** Failed workflow node */
  readonly nodeFailed: string;
  /** Handoff accent color */
  readonly handoffAccent: string;
}

export const lightTokens: AiThemeTokens = {
  userBubble: '222.2 47.4% 95%',
  assistantBubble: '0 0% 97%',
  reasoningBg: '45 100% 97%',
  toolCallBg: '210 40% 96%',
  streamingCursor: '222.2 47.4% 11.2%',
  codeBg: '220 14% 96%',
  nodeActive: '217.2 91.2% 59.8%',
  nodeCompleted: '142.1 76.2% 36.3%',
  nodeFailed: '0 84.2% 60.2%',
  handoffAccent: '262.1 83.3% 57.8%',
};

export const darkTokens: AiThemeTokens = {
  userBubble: '217.2 32.6% 17.5%',
  assistantBubble: '222.2 84% 6%',
  reasoningBg: '40 30% 12%',
  toolCallBg: '217.2 32.6% 12%',
  streamingCursor: '210 40% 98%',
  codeBg: '220 14% 11%',
  nodeActive: '217.2 91.2% 59.8%',
  nodeCompleted: '142.1 70.6% 45.3%',
  nodeFailed: '0 72.2% 50.6%',
  handoffAccent: '262.1 83.3% 67.8%',
};

const TOKEN_TO_VAR: Record<keyof AiThemeTokens, string> = {
  userBubble: '--ai-user-bubble',
  assistantBubble: '--ai-assistant-bubble',
  reasoningBg: '--ai-reasoning-bg',
  toolCallBg: '--ai-tool-call-bg',
  streamingCursor: '--ai-streaming-cursor',
  codeBg: '--ai-code-bg',
  nodeActive: '--ai-node-active',
  nodeCompleted: '--ai-node-completed',
  nodeFailed: '--ai-node-failed',
  handoffAccent: '--ai-handoff-accent',
};

/**
 * Apply AI theme tokens to the document root as CSS variables.
 * Call with custom tokens to override defaults.
 */
export function applyAiTheme(tokens: AiThemeTokens, target: HTMLElement = document.documentElement): void {
  for (const [key, varName] of Object.entries(TOKEN_TO_VAR)) {
    target.style.setProperty(varName, tokens[key as keyof AiThemeTokens]);
  }
}
