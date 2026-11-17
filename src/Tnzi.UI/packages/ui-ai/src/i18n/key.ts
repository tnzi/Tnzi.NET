/**
 * The injection key the catalogue is provided under, apart from the engine.
 *
 * `i18n/index.ts` imports the English catalogue as its fallback; the controls
 * (`@tnzi/ui-ai/controls`) must not carry a whole catalogue for the few words
 * they say, so they inject the provided one through this key and fall back to
 * their own English words (`components/controls/controls-text.ts`).
 */
import type { InjectionKey, Ref } from 'vue';
import type { AiLocaleMessages } from '../locales/en';

export const AI_I18N_KEY: InjectionKey<Ref<AiLocaleMessages>> = Symbol('ai-i18n');

/**
 * The app's language as a BCP 47 tag (`en`, `zh-CN`), provided beside the catalogue by
 * `createAiI18nPlugin` (and by `createAiI18n` when given one). What a component hands on to
 * something that has no catalogue of its own: a widget's host context (`TWidgetFrame`).
 * Undefined when the app named none.
 */
export const AI_LOCALE_KEY: InjectionKey<Readonly<Ref<string | undefined>>> = Symbol('ai-locale');
