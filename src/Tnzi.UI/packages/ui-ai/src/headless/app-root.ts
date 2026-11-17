/**
 * The page-wide marker of an app built on ui-ai: `data-tnzi-ai-app` on `<html>`.
 *
 * A handful of rules belong to the whole page of such an app rather than to one
 * component (the body face, the thin scrollbar on every scroller, the keyboard
 * focus line, the colour scheme of native controls). They are written under
 * this marker in `styles/index.css`, so importing the package stylesheet alone
 * restyles nothing: a page that embeds a ui-ai widget, or an admin app that
 * lazy-loads one ui-ai component, keeps its own look.
 *
 * `defineChatApp` marks the page for the app's lifetime and `TChatApp` while it
 * is mounted. An app that builds its own shell from the components calls
 * `markAiApp()` once at start-up (or writes the attribute into its
 * `index.html`). The embeddable widgets never mark the host page.
 */

/** The attribute on `<html>` the page-wide rules are scoped to. */
export const AI_APP_ATTRIBUTE = 'data-tnzi-ai-app'

interface Holder {
  count: number
  /** The attribute was there before the first mark (the app's own `index.html`): never removed. */
  preset: boolean
}

const holders = new WeakMap<Document, Holder>()

/**
 * Marks the document as a ui-ai app until the returned function is called.
 * Marks are counted, so two shells mounted in turn do not unmark each other;
 * the attribute goes with the last release, unless the page had it already.
 * Without a document (server rendering, or `null`) it does nothing.
 */
export function markAiApp(target?: Document | null): () => void {
  const doc = target === undefined ? (typeof document === 'undefined' ? null : document) : target
  if (!doc) return () => {}
  const root = doc.documentElement
  const holder = holders.get(doc) ?? { count: 0, preset: root.hasAttribute(AI_APP_ATTRIBUTE) }
  holders.set(doc, { ...holder, count: holder.count + 1 })
  root.setAttribute(AI_APP_ATTRIBUTE, '')

  let released = false
  return () => {
    if (released) return
    released = true
    const current = holders.get(doc)
    if (!current) return
    const count = current.count - 1
    if (count > 0) {
      holders.set(doc, { ...current, count })
      return
    }
    holders.delete(doc)
    if (!current.preset) root.removeAttribute(AI_APP_ATTRIBUTE)
  }
}
