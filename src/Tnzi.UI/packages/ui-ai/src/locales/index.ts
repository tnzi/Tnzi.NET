/**
 * `@tnzi/ui-ai/locales` - the message catalogues.
 *
 * Dictionaries only. The i18n **engine** (`createAiI18n` / `useAiI18n` /
 * `formatAiMessage`) lives in `src/i18n/`, mirroring `@tnzi/ui-admin`: a
 * component that needs to translate should not have to import a language pack
 * to get at the translator.
 */
export type { AiLocaleMessages } from './en';
export { en } from './en';
export { zhCn } from './zh-cn';
