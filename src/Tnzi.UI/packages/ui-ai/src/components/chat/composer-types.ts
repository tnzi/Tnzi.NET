/**
 * Shared composer types for TThreadComposer / TLandingPage.
 *
 * `ComposerAction` lets consumers declaratively add toolbar buttons to the
 * composer without forking the component - the primary answer to
 * "the input bar should allow placing more buttons".
 */

export interface ComposerAction {
  /** Stable id, emitted on click. */
  readonly id: string
  /** Iconify icon name, e.g. 'lucide:paperclip'. */
  readonly icon: string
  /** Tooltip / aria-label. */
  readonly tooltip?: string
  /** Which side of the toolbar to render on. Defaults to 'left'. */
  readonly side?: 'left' | 'right'
  /** Controlled active/highlight state (e.g. an open popover or toggled mode). */
  readonly active?: boolean
  /** Disable the button. */
  readonly disabled?: boolean
  /** Sort order within its side (ascending). */
  readonly order?: number
}

/** Default wide accept list for the built-in attachment button. */
export const DEFAULT_COMPOSER_ACCEPT =
  'image/*,.pdf,.txt,.md,.csv,.json,.doc,.docx,.xls,.xlsx,.ppt,.pptx,' +
  '.js,.ts,.py,.java,.go,.rs,.c,.cpp,.cs,.html,.css,.xml,.yaml,.yml,.sql,.sh,.log'
