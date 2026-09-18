import { describe, it, expect, afterEach } from 'vitest'
import { h, type Component, type VNodeChild } from 'vue'
import { mount } from '@vue/test-utils'
import {
  NButton,
  NConfigProvider,
  NDynamicInput,
  NForm,
  NFormItem,
  NInput,
  NModal,
  NSelect,
  NSwitch,
  NTag,
  lightTheme,
  type GlobalComponentConfig,
} from 'naive-ui'
import TModalShell from '../../../src/components/overlay/TModalShell.vue'
import TDrawerShell from '../../../src/components/overlay/TDrawerShell.vue'
import TOverlayTheme from '../../../src/components/overlay/TOverlayTheme.vue'
import { OVERLAY_CONTROL_SIZE } from '../../../src/headless/theme/useOverlayTheme'

/**
 * The control density inside the overlay provider - the two shells and the
 * `TOverlayTheme` wrapper a hand-rolled overlay wears - run against REAL naive
 * components: the thing being asserted is the size naive resolves for a
 * control the shell never sees (it lives in the caller's slot), so a stub
 * would only report what the test itself wrote. Overlays teleport, hence
 * `attachTo` + document queries.
 *
 * naive's own dialogs and popconfirms hard-code `small` on their buttons; a
 * control dropped into a shell used to fall back to the global `medium`, so a
 * dialog's density depended on whether its author remembered `size="small"`.
 */

const shells: Array<[name: string, shell: Component]> = [
  ['TModalShell', TModalShell],
  ['TDrawerShell', TDrawerShell],
]

type Slots = Record<string, () => VNodeChild>

let mounted: Array<ReturnType<typeof mount>> = []

afterEach(() => {
  for (const w of mounted) w.unmount()
  mounted = []
  document.body.innerHTML = ''
})

function open(shell: Component, slots: Slots, root?: GlobalComponentConfig): void {
  const overlay = () => h(shell, { show: true, title: 't' }, slots)
  // A root provider, as an app has one (`TAdminAppRoot`), so the shell's own
  // provider has something to inherit from. Without `root` it carries no
  // `componentOptions`, which is what every consumer has today.
  const w = mount(
    { render: () => h(NConfigProvider, root ? { componentOptions: root } : {}, overlay) },
    { attachTo: document.body },
  )
  mounted.push(w)
}

/** Mount the same controls on a plain page: the outside-a-shell control. */
function onPage(children: () => VNodeChild): void {
  const w = mount({ render: children }, { attachTo: document.body })
  mounted.push(w)
}

const button = (props: Record<string, unknown> = {}) =>
  h(NButton, { class: 'probe-button', ...props }, () => 'Confirm')
const input = (props: Record<string, unknown> = {}) =>
  h(NInput, { class: 'probe-input', value: '', ...props })
const select = (props: Record<string, unknown> = {}) =>
  h(NSelect, { class: 'probe-select', options: [], ...props })
const toggle = (props: Record<string, unknown> = {}) =>
  h(NSwitch, { class: 'probe-switch', ...props })
const form = (props: Record<string, unknown>, children: () => VNodeChild) =>
  h(NForm, { model: {}, ...props }, () => h(NFormItem, { label: 'Field' }, children))

type Size = 'small' | 'medium' | 'large'
const SIZES: readonly Size[] = ['small', 'medium', 'large']

function rendered(selector: string): HTMLElement {
  const el = document.querySelector<HTMLElement>(selector)
  expect(el, `${selector} was not rendered`).not.toBeNull()
  return el!
}

/**
 * The size naive painted, read off the class it hangs the size styles on
 * (`n-button--small-type`, `n-input--small-size`). Restricted to the size
 * words: a button also carries `n-button--default-type` for its TYPE.
 */
function sizeClass(selector: string, prefix: string, suffix: string): Size | undefined {
  const classes = rendered(selector).classList
  return SIZES.find((s) => classes.contains(`${prefix}--${s}${suffix}`))
}

/**
 * Switch, Tag and the Select's selection box carry no size class; naive
 * writes the size into an inline custom property instead. Map it back
 * through the light theme's own per-size value, so the assertion does not
 * hard-code a pixel that a naive upgrade could legitimately move.
 */
function sizeVar(
  selector: string,
  name: string,
  perSize: Record<Size, string>,
): Size | undefined {
  const value = rendered(selector).style.getPropertyValue(name).trim()
  return SIZES.find((s) => perSize[s] === value)
}

const common = lightTheme.common
const tagVars = lightTheme.Tag.self(common)
const switchVars = lightTheme.Switch.self(common)
const selectionVars = lightTheme.Select.peers.InternalSelection.self(common)

const buttonSize = () => sizeClass('.probe-button', 'n-button', '-type')
const inputSize = () => sizeClass('.probe-input', 'n-input', '-size')
const selectSize = () =>
  sizeVar('.probe-select .n-base-selection', '--n-height', {
    small: selectionVars.heightSmall,
    medium: selectionVars.heightMedium,
    large: selectionVars.heightLarge,
  })
const switchSize = () =>
  sizeVar('.probe-switch', '--n-rail-height', {
    small: switchVars.railHeightSmall,
    medium: switchVars.railHeightMedium,
    large: switchVars.railHeightLarge,
  })
const tagSize = () =>
  sizeVar('.probe-tag', '--n-height', {
    small: tagVars.heightSmall,
    medium: tagVars.heightMedium,
    large: tagVars.heightLarge,
  })

describe('overlay control size', () => {
  it('is small - the same size naive gives its own dialog buttons', () => {
    expect(OVERLAY_CONTROL_SIZE).toBe('small')
  })

  // (d) The control: the same controls on a page keep naive's global default.
  // The shells decide the density of overlays and of nothing else.
  it('leaves controls outside a shell at medium', () => {
    onPage(() => [button(), input(), select(), toggle()])
    expect(buttonSize()).toBe('medium')
    expect(inputSize()).toBe('medium')
    expect(selectSize()).toBe('medium')
    expect(switchSize()).toBe('medium')
  })

  it('leaves a form outside a shell at medium', () => {
    onPage(() => form({}, () => [input(), button()]))
    expect(inputSize()).toBe('medium')
    expect(buttonSize()).toBe('medium')
  })

  describe.each(shells)('%s', (_name, shell) => {
    // (a) Bare controls in the slots. The footer button is the case the whole
    // change is about; the body input is the other half of the same ask.
    it('renders a bare button and input in its slots small', () => {
      open(shell, { default: () => input(), footer: () => button() })
      expect(buttonSize()).toBe('small')
      expect(inputSize()).toBe('small')
    })

    it('renders the other bare control families small too', () => {
      open(shell, { default: () => [select(), toggle()] })
      expect(selectSize()).toBe('small')
      expect(switchSize()).toBe('small')
    })

    // NDynamicInput has no `size`; its rows are ordinary NInputs and its add /
    // remove buttons ordinary NButtons, so both must land on the same default
    // without an entry of their own.
    it('renders a bare NDynamicInput small, rows and buttons alike', () => {
      open(shell, { default: () => h(NDynamicInput, { class: 'probe-dynamic', value: ['a'] }) })
      expect(sizeClass('.probe-dynamic .n-input', 'n-input', '-size')).toBe('small')
      expect(sizeClass('.probe-dynamic .n-button', 'n-button', '-type')).toBe('small')
    })

    it('reaches a control in the #header slot', () => {
      open(shell, { header: () => button() })
      expect(buttonSize()).toBe('small')
    })

    // (b) Inside an NForm with no size. This is the path a control inside an
    // NFormItem takes: `NFormItem.mergedSize` never returns undefined, so the
    // control never consults its own entry - only the Form entry can reach it.
    // A `TSchemaForm` builds exactly this (an NForm with no size).
    it('renders controls inside a size-less NForm small', () => {
      open(shell, { default: () => form({}, () => [input(), button()]) })
      expect(inputSize()).toBe('small')
      expect(buttonSize()).toBe('small')
    })

    // (c) Explicit sizes still win, at every level naive offers.
    it('lets an explicit size on the control win', () => {
      open(shell, { default: () => input({ size: 'large' }), footer: () => button({ size: 'large' }) })
      expect(inputSize()).toBe('large')
      expect(buttonSize()).toBe('large')
    })

    it('lets an explicit size on the enclosing NForm win', () => {
      open(shell, { default: () => form({ size: 'large' }, () => [input(), button()]) })
      expect(inputSize()).toBe('large')
      expect(buttonSize()).toBe('large')
    })

    it('lets an explicit size on the enclosing NFormItem win', () => {
      open(shell, {
        default: () =>
          h(NForm, { model: {} }, () => h(NFormItem, { label: 'F', size: 'large' }, () => input())),
      })
      expect(inputSize()).toBe('large')
    })

    // The shell's provider must EXTEND the app's root componentOptions, not
    // replace them - naive's nested provider replaces. Two things are pinned:
    // an entry the shell does not touch survives (a root `Tag.size` still
    // reaches a tag in the overlay), and an entry the shell would otherwise
    // fill yields to the root's explicit value (an app sized `large` on
    // purpose stays large in its overlays). Both read the parent back through
    // naive's own injection; if a naive upgrade renames that key, this is what
    // turns red instead of every overlay silently dropping the root's options.
    it('extends the root componentOptions instead of replacing them', () => {
      open(
        shell,
        {
          default: () => [h(NTag, { class: 'probe-tag' }, () => 'Posted'), input()],
          footer: () => button(),
        },
        { Tag: { size: 'large' }, Button: { size: 'large' } },
      )
      expect(tagSize()).toBe('large')
      expect(buttonSize()).toBe('large')
      // An entry the root left unset is still filled.
      expect(inputSize()).toBe('small')
    })
  })
  // The same provider, worn by a hand-rolled overlay. The shells go through
  // this component, so this is also the one place the density is decided.
  describe('TOverlayTheme', () => {
    // A preset-less NModal takes exactly ONE root child (it merges its own
    // props onto it), hence the wrapping div.
    function openRaw(props: Record<string, unknown>, children: () => VNodeChild): void {
      const w = mount(
        {
          render: () =>
            h(TOverlayTheme, props, () =>
              h(NModal, { show: true }, () => h('div', { class: 'raw-body' }, [children()])),
            ),
        },
        { attachTo: document.body },
      )
      mounted.push(w)
    }

    it('gives a hand-rolled NModal the same small default', () => {
      openRaw({}, () => [input(), button()])
      expect(inputSize()).toBe('small')
      expect(buttonSize()).toBe('small')
    })

    it('still lets an explicit size win', () => {
      openRaw({}, () => input({ size: 'large' }))
      expect(inputSize()).toBe('large')
    })

    // The opt-out is for a host that mounts a PAGE on a floating surface (the
    // desktop window host): it wants the theme reset and nothing else, and a
    // page keeps the page-level control sizes.
    it('keeps page-level sizes when dense is off', () => {
      openRaw({ dense: false }, () => [input(), button()])
      expect(inputSize()).toBe('medium')
      expect(buttonSize()).toBe('medium')
    })

    // A dialog opened from a dense-off host is still a dialog.
    it('lets a shell inside a dense-off host be dense again', () => {
      const w = mount(
        {
          render: () =>
            h(TOverlayTheme, { dense: false }, () =>
              h(TModalShell, { show: true, title: 't' }, { footer: () => button() }),
            ),
        },
        { attachTo: document.body },
      )
      mounted.push(w)
      expect(buttonSize()).toBe('small')
    })
  })
})
