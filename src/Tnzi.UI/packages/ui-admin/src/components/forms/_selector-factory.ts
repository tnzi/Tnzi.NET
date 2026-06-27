import { defineComponent, h, ref, computed, onMounted, watch, type PropType } from 'vue'
import { NSelect } from 'naive-ui'

export interface SelectorOption {
  label: string
  value: string
}

export type SelectorFetcher = (keyword: string) => Promise<SelectorOption[]>

export type SelectorSize = 'tiny' | 'small' | 'medium' | 'large'

/**
 * Factory for the business selector components (`TUserSelector`,
 * `TRoleSelector`, ...). Each is a remote-search `NSelect` driven by a
 * `fetcher(keyword)` callback.
 *
 * Key behaviour (fixed 2026-06-27): a `seen` map accumulates every option the
 * fetcher has ever returned, so an already-selected value keeps its label even
 * after the search list changes — without it, switching the search term in a
 * multi-select dropped the labels of previously-picked tags back to raw ids
 * (the bug that made callers hand-roll their own `NSelect` instead of reusing
 * these). Also ships `clearable` + a pass-through `size` so the selectors sit
 * cleanly inside `size="small"` chrome and forms.
 */
export function createSelectorComponent(name: string) {
  return defineComponent({
    name,
    props: {
      value: {
        type: [String, Number, Array] as PropType<string | number | Array<string | number> | null>,
        default: null,
      },
      fetcher: { type: Function as PropType<SelectorFetcher>, required: true },
      multiple: { type: Boolean, default: false },
      disabled: { type: Boolean, default: false },
      clearable: { type: Boolean, default: true },
      placeholder: { type: String, default: '' },
      size: { type: String as PropType<SelectorSize>, default: undefined },
    },
    emits: ['update:value'],
    setup(props, { emit }) {
      // Latest search results.
      const results = ref<SelectorOption[]>([])
      const loading = ref(false)
      // Every option ever seen, keyed by value — so selected-but-not-in-results
      // values can be re-hydrated with their label (see component doc).
      const seen = new Map<string, SelectorOption>()
      let searchTimer: ReturnType<typeof setTimeout> | null = null

      function remember(opts: SelectorOption[]): void {
        for (const o of opts) seen.set(String(o.value), o)
      }

      async function load(keyword: string): Promise<void> {
        loading.value = true
        try {
          const opts = await props.fetcher(keyword)
          remember(opts)
          results.value = opts
        }
        finally {
          loading.value = false
        }
      }

      function onSearch(keyword: string): void {
        if (searchTimer) clearTimeout(searchTimer)
        searchTimer = setTimeout(() => void load(keyword), 300)
      }

      // Current results + any selected values missing from them (re-added with
      // their remembered label so multi-select tags never collapse to ids).
      const options = computed<SelectorOption[]>(() => {
        const current = results.value
        const present = new Set(current.map((o) => String(o.value)))
        const selected = Array.isArray(props.value)
          ? (props.value as unknown[])
          : props.value != null
            ? [props.value]
            : []
        const missing: SelectorOption[] = []
        for (const v of selected) {
          const key = String(v)
          if (!present.has(key)) {
            const o = seen.get(key)
            if (o) missing.push(o)
          }
        }
        return missing.length ? [...current, ...missing] : current
      })

      onMounted(() => { void load('') })
      watch(() => props.fetcher, () => { void load('') })

      return () => h(NSelect, {
        value: props.value,
        options: options.value,
        multiple: props.multiple,
        disabled: props.disabled,
        clearable: props.clearable,
        placeholder: props.placeholder,
        size: props.size,
        filterable: true,
        remote: true,
        loading: loading.value,
        'onUpdate:value': (v: unknown) => emit('update:value', v),
        onSearch,
      } as Record<string, unknown>)
    },
  })
}
