/**
 * `@tnzi/ui-ai/i18n` - the translation engine.
 *
 * Separate from `locales/` (the dictionaries) so a component can reach the
 * translator without pulling a language pack in with it - the same split
 * `@tnzi/ui-admin` makes between `i18n/` and `locales/`.
 */
import { computed, inject, provide, ref, toValue, type MaybeRefOrGetter, type Ref } from 'vue';
import { en, type AiLocaleMessages } from '../locales/en';
import { AI_I18N_KEY, AI_LOCALE_KEY } from './key';

export type { AiLocaleMessages } from '../locales/en';
export { AI_I18N_KEY, AI_LOCALE_KEY } from './key';
export { formatAiMessage } from './format';
export { createAiI18nPlugin, matchAiLocale } from './plugin';
export type { AiI18nPlugin, AiI18nPluginOptions } from './plugin';

/**
 * Provide AI i18n messages to descendant components.
 * Call in a root component's setup(). An app with no root component of its own to call it in
 * (custom elements) installs `createAiI18nPlugin` instead.
 *
 * `locale`, the language tag the messages are in, is provided beside them for what hands the
 * language on (a widget's host context); a ref or getter keeps it current.
 */
export function createAiI18n(
  messages: AiLocaleMessages = en,
  options: { readonly locale?: MaybeRefOrGetter<string | undefined> } = {},
): Ref<AiLocaleMessages> {
  const messagesRef = ref(messages) as Ref<AiLocaleMessages>;
  provide(AI_I18N_KEY, messagesRef);
  if (options.locale !== undefined) provide(AI_LOCALE_KEY, computed(() => toValue(options.locale)));
  return messagesRef;
}

/** The app's language tag as provided (`createAiI18nPlugin`, `createAiI18n`); undefined when none was. */
export function useAiLocale(): Readonly<Ref<string | undefined>> {
  return inject(AI_LOCALE_KEY, ref(undefined));
}

/**
 * Inject AI i18n messages. Falls back to English if not provided.
 */
export function useAiI18n(): Ref<AiLocaleMessages> {
  return inject(AI_I18N_KEY, ref(en) as Ref<AiLocaleMessages>);
}
