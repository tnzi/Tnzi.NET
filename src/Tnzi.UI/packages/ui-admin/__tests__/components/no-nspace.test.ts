import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { defineComponent, h, nextTick, ref } from 'vue'
import { NSpace } from 'naive-ui'
import { readFileSync, readdirSync, statSync } from 'fs'
import { join, relative, sep } from 'path'

/**
 * Static gate: `@tnzi/ui-admin` lays out rows and stacks of controls with a
 * flex container and `gap` (UnoCSS `flex flex-col gap-24px`, `flex flex-wrap
 * items-center gap-x-8px gap-y-4px`), never with naive's `NSpace`.
 *
 * Why: the naive-ui 2.45.x build gives every item wrapper `NSpace` renders the
 * same key. When an item goes from rendered to not rendered (`v-if`), or a
 * `v-for` inside changes length, Vue matches the wrappers by that shared key
 * and patches one item's content into several slots. The sign-in page showed
 * three identical "Sign in" buttons that way once `/auth/config` hid the
 * entries around it. Children that never change render fine, which is what
 * makes the defect easy to reintroduce: the next conditional child added to an
 * innocent-looking `NSpace` breaks it, with no error. A flex container with
 * `gap` gives the same spacing without wrappers, so there is nothing to key.
 */

const SRC = join(__dirname, '..', '..', 'src')

function walk(dir: string, out: string[] = []): string[] {
  for (const name of readdirSync(dir)) {
    const full = join(dir, name)
    if (statSync(full).isDirectory()) walk(full, out)
    else if (/\.(vue|ts|tsx)$/.test(name)) out.push(full)
  }
  return out
}

/** Source with comments removed, so prose that names `NSpace` does not count. */
function code(file: string): string {
  return readFileSync(file, 'utf8')
    .replace(/<!--[\s\S]*?-->/g, ' ')
    .replace(/\/\*[\s\S]*?\*\//g, ' ')
    .replace(/(^|[^:])\/\/.*$/gm, '$1')
}

const USES = [
  /<NSpace\b/,
  /<n-space\b/,
  /\bh\(\s*NSpace\b/,
  /import\s*\{[^}]*\bNSpace\b[^}]*\}\s*from\s*['"]naive-ui['"]/,
]

describe('ui-admin lays out with flex gap, not NSpace', () => {
  it('no source file renders or imports NSpace', () => {
    const offenders = walk(SRC)
      .filter((file) => USES.some((re) => re.test(code(file))))
      .map((file) => relative(SRC, file).split(sep).join('/'))
    expect(offenders).toEqual([])
  })

  /**
   * Canary for the upstream defect the gate exists for. It asserts the defect
   * is still there; when a naive-ui upgrade fixes it this test fails, and the
   * gate above can be reconsidered (and this canary deleted) rather than kept
   * on faith.
   */
  it('naive-ui NSpace still repeats an item when a sibling before it stops rendering', async () => {
    const shown = ref(true)
    const Host = defineComponent({
      setup: () => () =>
        h(NSpace, { vertical: true }, {
          default: () => [
            shown.value ? h('button', 'first') : null,
            h('button', 'middle'),
            shown.value ? h('button', 'last') : null,
          ],
        }),
    })
    const wrapper = mount(Host)
    expect(wrapper.findAll('button').map((b) => b.text())).toEqual(['first', 'middle', 'last'])

    shown.value = false
    await nextTick()
    // Correct output would be ['middle'].
    expect(wrapper.findAll('button').map((b) => b.text())).not.toEqual(['middle'])
    wrapper.unmount()
  })
})
