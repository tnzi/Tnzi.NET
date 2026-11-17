/**
 * The catalogue as an app-level install: `app.use(createAiI18nPlugin(...))`.
 *
 * `createAiI18n` provides the catalogue from a component's setup, which needs a root component of
 * the app's own. An app that has none to spare (each custom element of `defineCustomElement` is an
 * app of its own) installs this instead, in `configureApp`. One plugin installed in many apps
 * holds one language: `setLocale` switches every app it is installed in at once.
 *
 * It provides what `createAiI18n` provides (`AI_I18N_KEY`, read by `useAiI18n` and by the
 * controls' own words) plus the language tag (`AI_LOCALE_KEY`, read by `TWidgetFrame` for the
 * widget's host context).
 *
 * @example
 * ```ts
 * import { en, zhCn } from '@tnzi/ui-ai/locales'
 * import { createAiI18nPlugin } from '@tnzi/ui-ai/i18n'
 *
 * const aiI18n = createAiI18nPlugin({ messages: { en, 'zh-CN': zhCn }, locale: navigator.language })
 * customElements.define('tz-table', defineCustomElement(Table, { configureApp: (app) => app.use(aiI18n) }))
 * // later, when the user switches language:
 * aiI18n.setLocale('zh-CN')
 * ```
 */
import { computed, ref, type App, type ComputedRef, type Ref } from 'vue';
import { en, type AiLocaleMessages } from '../locales/en';
import { AI_I18N_KEY, AI_LOCALE_KEY } from './key';

export interface AiI18nPluginOptions {
  /**
   * The catalogues, by language tag (`{ en, 'zh-CN': zhCn }` from `@tnzi/ui-ai/locales`).
   * English alone when left out.
   */
  readonly messages?: Readonly<Record<string, AiLocaleMessages>>;
  /** The language to start in, a BCP 47 tag. `fallbackLocale` when left out. */
  readonly locale?: string;
  /** The catalogue used when no catalogue matches the language. `en` when left out. */
  readonly fallbackLocale?: string;
}

export interface AiI18nPlugin {
  /** The language in use, as it was asked for (`zh-Hans-CN` stays that, whichever catalogue serves it). */
  readonly locale: Readonly<Ref<string>>;
  /** The catalogue that serves the language. */
  readonly messages: ComputedRef<AiLocaleMessages>;
  /** Switches the language of every app the plugin is installed in. */
  setLocale(locale: string): void;
  /** Adds or replaces the catalogue of a language (a language pack loaded later). */
  setMessages(locale: string, messages: AiLocaleMessages): void;
  install(app: App): void;
}

/**
 * The catalogue key that serves `locale`: the tag itself, then the tag with its last subtags
 * dropped (`zh-Hans-CN` → `zh-Hans` → `zh`), then any catalogue of the same language (`zh-TW` is
 * served by `zh-CN` rather than by English). Case-insensitive, `_` read as `-`. Null when no
 * catalogue is of that language.
 */
export function matchAiLocale(locale: string, available: readonly string[]): string | null {
  const normalize = (tag: string) => tag.trim().replace(/_/g, '-').toLowerCase();
  const byNormal = new Map(available.map((key) => [normalize(key), key]));
  const parts = normalize(locale).split('-').filter(Boolean);
  for (let length = parts.length; length > 0; length--) {
    const found = byNormal.get(parts.slice(0, length).join('-'));
    if (found) return found;
  }
  const language = parts[0];
  if (!language) return null;
  return available.find((key) => normalize(key).split('-')[0] === language) ?? null;
}

export function createAiI18nPlugin(options: AiI18nPluginOptions = {}): AiI18nPlugin {
  const catalogues = ref<Record<string, AiLocaleMessages>>({ ...(options.messages ?? { en }) });
  const fallbackLocale = options.fallbackLocale ?? 'en';
  const locale = ref(options.locale?.trim() || fallbackLocale);

  const messages = computed<AiLocaleMessages>(() => {
    const keys = Object.keys(catalogues.value);
    const key = matchAiLocale(locale.value, keys) ?? matchAiLocale(fallbackLocale, keys);
    return (key ? catalogues.value[key] : undefined) ?? en;
  });

  return {
    locale,
    messages,
    setLocale(next) {
      const tag = next.trim();
      if (tag) locale.value = tag;
    },
    setMessages(tag, catalogue) {
      catalogues.value = { ...catalogues.value, [tag]: catalogue };
    },
    install(app) {
      app.provide(AI_I18N_KEY, messages as unknown as Ref<AiLocaleMessages>);
      app.provide(AI_LOCALE_KEY, locale);
    },
  };
}
