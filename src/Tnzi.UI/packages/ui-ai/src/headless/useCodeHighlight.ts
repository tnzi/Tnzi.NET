/**
 * @experimental
 * useCodeHighlight - Reactive Shiki syntax highlighter.
 *
 * Wraps Shiki's `codeToHtml` in a Vue-friendly interface: pass reactive
 * refs for code/lang/theme and get back a `shallowRef<string>` holding
 * the latest HTML output. Shiki is dynamically imported so consumers who
 * never call this composable don't pay the bundle cost.
 *
 * The composable also exposes `isLoading` and `error` refs so callers can
 * render skeletons or fallback UI while highlighting is in flight.
 *
 * ## Why a composable rather than inline Shiki
 *
 * TArtifactPanel was the first consumer, but any component that wants to
 * render a code block with github-light / github-dark theming can reuse
 * this - e.g. ChatMessage's inline code blocks, RAG citation snippets,
 * skill parameter samples. Centralizing the Shiki pipeline here also
 * means a single place to swap themes, add language detection heuristics,
 * or upgrade to Shiki's async `createHighlighter` API later.
 *
 * ## Filename-based language detection
 *
 * `detectLangFromFilename` is exported separately so callers who have
 * a filename but not a manually-specified lang can derive one. It maps
 * the common extensions to Shiki grammar ids; unknown extensions fall
 * back to `'text'` which renders uncolored (Shiki doesn't throw).
 *
 * @example
 * ```ts
 * const code = ref('const x = 1')
 * const lang = ref<string>('typescript')
 * const { html, isLoading } = useCodeHighlight(code, lang)
 *
 * // then in template:
 * <div v-if="isLoading">loading…</div>
 * <div v-else v-html="html" />
 * ```
 */
import {
  ref,
  shallowRef,
  watch,
  onWatcherCleanup,
  type Ref,
  type MaybeRefOrGetter,
  toValue,
} from 'vue'

/** Shiki grammar ids covered by the built-in detector. */
export type CodeLang =
  | 'vue' | 'typescript' | 'javascript' | 'css' | 'json' | 'markdown'
  | 'python' | 'html' | 'yaml' | 'shell' | 'sql' | 'rust' | 'go'
  | 'java' | 'csharp' | 'cpp' | 'c' | 'text'

/** Themes are free-form strings (Shiki accepts any theme id it knows). */
export type CodeTheme = string

/**
 * Derive a Shiki language id from a filename. Falls back to `'text'` for
 * unknown extensions - `'text'` is a valid Shiki lang that just renders
 * plain monospace output.
 */
export function detectLangFromFilename(filename: string | null | undefined): CodeLang {
  if (!filename) return 'text'
  const f = filename.toLowerCase()
  if (f.endsWith('.vue')) return 'vue'
  if (f.endsWith('.ts') || f.endsWith('.tsx')) return 'typescript'
  if (f.endsWith('.js') || f.endsWith('.jsx') || f.endsWith('.mjs') || f.endsWith('.cjs')) return 'javascript'
  if (f.endsWith('.css') || f.endsWith('.scss') || f.endsWith('.sass') || f.endsWith('.less')) return 'css'
  if (f.endsWith('.json')) return 'json'
  if (f.endsWith('.md') || f.endsWith('.markdown')) return 'markdown'
  if (f.endsWith('.py')) return 'python'
  if (f.endsWith('.html') || f.endsWith('.htm')) return 'html'
  if (f.endsWith('.yml') || f.endsWith('.yaml')) return 'yaml'
  if (f.endsWith('.sh') || f.endsWith('.bash') || f.endsWith('.zsh')) return 'shell'
  if (f.endsWith('.sql')) return 'sql'
  if (f.endsWith('.rs')) return 'rust'
  if (f.endsWith('.go')) return 'go'
  if (f.endsWith('.java')) return 'java'
  if (f.endsWith('.cs')) return 'csharp'
  if (f.endsWith('.cpp') || f.endsWith('.cc') || f.endsWith('.cxx') || f.endsWith('.hpp')) return 'cpp'
  if (f.endsWith('.c') || f.endsWith('.h')) return 'c'
  return 'text'
}

/** The light/dark pair Shiki renders when no single `theme` is pinned. */
export const DEFAULT_CODE_THEMES = { light: 'github-light', dark: 'github-dark' } as const

export interface UseCodeHighlightOptions {
  /**
   * Pin a SINGLE Shiki theme. Leave unset to get the dual-theme output
   * (`themes` below), which is what follows light/dark. Setting this is an
   * opt-out: the rendered colours will then be the same in both modes.
   */
  theme?: MaybeRefOrGetter<CodeTheme | undefined>
  /**
   * Light/dark theme pair. Defaults to `github-light` / `github-dark`.
   *
   * Shiki renders the light colours as inline `color:` and the dark ones as a
   * `--shiki-dark` custom property on the same element. Nothing consumes that
   * property on its own - the swap lives in ONE place, `styles/index.css`
   * (`.dark .shiki span { color: var(--shiki-dark) !important }`). Do not add a
   * scoped copy per component: this was broken for months precisely because
   * only `TStreamMarkdown` carried its own copy of that rule.
   */
  themes?: MaybeRefOrGetter<{ light: CodeTheme; dark: CodeTheme }>
  /** Run the first highlight synchronously in the setup tick. Default `true`. */
  immediate?: boolean
}

export interface UseCodeHighlightReturn {
  /** Shiki-rendered HTML. Empty string while loading or on error. */
  html: Readonly<Ref<string>>
  /** Whether a highlight pass is currently in flight. */
  isLoading: Readonly<Ref<boolean>>
  /** The most recent error thrown by Shiki, if any. */
  error: Readonly<Ref<Error | null>>
}

/**
 * @param code   Code source. Can be a ref, getter, or plain value.
 * @param lang   Shiki language id. Can be a ref, getter, or plain value.
 * @param options Optional `theme` and `immediate` flag.
 */
export function useCodeHighlight(
  code: MaybeRefOrGetter<string>,
  lang: MaybeRefOrGetter<string>,
  options: UseCodeHighlightOptions = {},
): UseCodeHighlightReturn {
  const html = shallowRef<string>('')
  const isLoading = ref(false)
  const error = ref<Error | null>(null)

  /* Watch source returns a tuple instead of a fresh object so Vue's
     shallow comparison can detect "no change": an object literal
     would create a new reference every reactive tick and re-trigger
     the highlight pass even when none of the inputs changed. */
  watch(
    /* Resolved down to primitives on purpose: a `{ light, dark }` object in the
       tuple would be a new reference every reactive tick and re-trigger the
       highlight pass even when nothing changed. */
    () => {
      /* `||` rather than `??`: an empty string reaches here from components
         that declare `theme?: string` with a `''` prop default, and it means
         "not pinned", not "a theme named empty string". */
      const pinned = toValue(options.theme) || null
      const pair = toValue(options.themes) ?? DEFAULT_CODE_THEMES
      return [toValue(code), toValue(lang), pinned, pair.light, pair.dark] as const
    },
    async ([c, l, pinned, light, dark]) => {
      /* Two awaits follow, so a rapid input change (switching files in the
         artifact panel) can leave an older pass still in flight. Registering
         the cleanup synchronously - before the first await - lets a superseded
         pass detect that it lost the race and drop its result instead of
         overwriting the newer one. */
      let superseded = false
      onWatcherCleanup(() => {
        superseded = true
      })

      if (!c) {
        html.value = ''
        error.value = null
        return
      }
      isLoading.value = true
      error.value = null
      try {
        const shiki = await import('shiki')
        if (superseded) return
        const rendered = pinned
          ? await shiki.codeToHtml(c, { lang: l || 'text', theme: pinned })
          : await shiki.codeToHtml(c, { lang: l || 'text', themes: { light, dark } })
        if (superseded) return
        html.value = rendered
      } catch (err) {
        if (superseded) return
        error.value = err instanceof Error ? err : new Error(String(err))
        // Fallback: escape and wrap in <pre><code> so the view still
        // renders something reasonable when Shiki fails to load the
        // requested lang/theme.
        const esc = c.replace(/[&<>]/g, (ch) =>
          ch === '&' ? '&amp;' : ch === '<' ? '&lt;' : '&gt;',
        )
        html.value = `<pre><code>${esc}</code></pre>`
      } finally {
        if (!superseded) isLoading.value = false
      }
    },
    { immediate: options.immediate ?? true },
  )

  return {
    html,
    isLoading,
    error,
  }
}
