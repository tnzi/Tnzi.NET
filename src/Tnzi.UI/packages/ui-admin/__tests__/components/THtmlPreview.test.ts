import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import fs from 'node:fs'
import path from 'node:path'
import THtmlPreview from '../../src/components/display/THtmlPreview.vue'

const SRC = path.resolve(__dirname, '../../src')

describe('THtmlPreview', () => {
  it('renders the HTML through srcdoc, never into the host DOM', () => {
    // The closing tag is split rather than escaped: `\/` is a useless escape in
    // a .ts module (this file is never inlined into an HTML <script> block),
    // but a literal `</script>` here would still be the kind of thing a future
    // bundler-inlined harness trips over.
    const w = mount(THtmlPreview, { props: { html: '<b>hi</b><script>alert(1)</' + 'script>' } })
    const frame = w.find('iframe')

    expect(frame.exists()).toBe(true)
    expect(frame.attributes('srcdoc')).toContain('<b>hi</b>')
    // The markup must exist ONLY as an attribute value. If it were parsed into
    // the host document the element would be reachable as a real child.
    expect(w.find('b').exists()).toBe(false)
  })

  it('sandboxes by default with no escape hatches granted', () => {
    const w = mount(THtmlPreview, { props: { html: '<p>x</p>' } })
    const sandbox = w.find('iframe').attributes('sandbox')

    // Present-and-empty is the strictest setting: every restriction applies and
    // the document gets a unique opaque origin. Absent would mean unsandboxed.
    expect(sandbox).toBe('')
  })

  it('never grants allow-scripts together with allow-same-origin', () => {
    // The pair voids the sandbox - the framed document can delete its own
    // sandbox attribute and reload with full host-origin access. A caller can
    // still opt in explicitly; what must not happen is the component doing it.
    const w = mount(THtmlPreview, { props: { html: '<p>x</p>' } })
    const sandbox = w.find('iframe').attributes('sandbox') ?? ''
    expect(sandbox.includes('allow-scripts') && sandbox.includes('allow-same-origin')).toBe(false)
  })

  it('drops the attribute only when sandbox is explicitly null', () => {
    const w = mount(THtmlPreview, { props: { html: '<p>x</p>', sandbox: null } })
    expect(w.find('iframe').attributes('sandbox')).toBeUndefined()
  })

  it('renders an empty srcdoc rather than the string "null" for empty content', () => {
    const w = mount(THtmlPreview, { props: { html: null } })
    expect(w.find('iframe').attributes('srcdoc')).toBe('')
  })
})

describe('no page renders untrusted HTML with v-html', () => {
  // A backend-rendered template is author-controlled markup. `v-html` puts it
  // in the admin's own DOM at the admin's own origin inside an authenticated
  // session, so template-edit rights become script execution in a super-admin's
  // browser. Both known call sites now go through THtmlPreview; this keeps a
  // third from appearing, because the next one will look just as harmless.
  function walk(dir: string, out: string[] = []): string[] {
    for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
      const full = path.join(dir, entry.name)
      if (entry.isDirectory()) walk(full, out)
      else if (entry.name.endsWith('.vue')) out.push(full)
    }
    return out
  }

  it('finds the sources (guards against a stale path)', () => {
    expect(walk(SRC).length).toBeGreaterThan(50)
  })

  it('has no v-html binding anywhere under src/', () => {
    const offenders = walk(SRC)
      .filter((f) => /\sv-html\s*=/.test(fs.readFileSync(f, 'utf8')))
      .map((f) => path.relative(SRC, f))

    expect(
      offenders,
      `${offenders.join(', ')} bind v-html. Render untrusted HTML through ` +
        `<THtmlPreview> (sandboxed iframe) instead.`,
    ).toEqual([])
  })
})
