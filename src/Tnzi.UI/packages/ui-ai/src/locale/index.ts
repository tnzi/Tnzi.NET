import { inject, provide, ref, type InjectionKey, type Ref } from 'vue';
import { en, type AiLocaleMessages } from './en';

export type { AiLocaleMessages } from './en';
export { en } from './en';
export { zhCn } from './zh-cn';

const AI_I18N_KEY: InjectionKey<Ref<AiLocaleMessages>> = Symbol('ai-i18n');

/**
 * Provide AI i18n messages to descendant components.
 * Call in a root component's setup().
 */
export function createAiI18n(messages: AiLocaleMessages = en): Ref<AiLocaleMessages> {
  const messagesRef = ref(messages) as Ref<AiLocaleMessages>;
  provide(AI_I18N_KEY, messagesRef);
  return messagesRef;
}

/**
 * Inject AI i18n messages. Falls back to English if not provided.
 */
export function useAiI18n(): Ref<AiLocaleMessages> {
  return inject(AI_I18N_KEY, ref(en) as Ref<AiLocaleMessages>);
}

/**
 * Substitute `{name}` placeholders in a message.
 *
 * Several catalogue entries carry placeholders (`{count}`, `{seconds}`,
 * `{size}`); this is the one helper that fills them, so components do not each
 * hand-roll a `.replace()`. Unknown placeholders are left untouched, which
 * makes a typo visible instead of silently blanking the string.
 *
 * @example formatAiMessage(t.mcp.toolCount, { count: 12 }) // '12 tools'
 */
export function formatAiMessage(
  template: string,
  values: Readonly<Record<string, string | number>>,
): string {
  return template.replace(/\{(\w+)\}/g, (match, key: string) =>
    key in values ? String(values[key]) : match,
  );
}
