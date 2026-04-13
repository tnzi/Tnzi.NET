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
