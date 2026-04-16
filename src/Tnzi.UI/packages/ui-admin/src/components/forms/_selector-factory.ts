import { defineComponent, h, ref, onMounted, watch, type PropType } from 'vue'
import { NSelect } from 'naive-ui'

export interface SelectorOption {
  label: string
  value: string
}

export type SelectorFetcher = (keyword: string) => Promise<SelectorOption[]>

export function createSelectorComponent(name: string) {
  return defineComponent({
    name,
    props: {
      value: { type: [String, Number, Array, null] as PropType<unknown>, default: null },
      fetcher: { type: Function as PropType<SelectorFetcher>, required: true },
      multiple: { type: Boolean, default: false },
      disabled: { type: Boolean, default: false },
      placeholder: { type: String, default: '' },
    },
    emits: ['update:value'],
    setup(props, { emit }) {
      const options = ref<SelectorOption[]>([])
      const loading = ref(false)
      let searchTimer: ReturnType<typeof setTimeout> | null = null

      async function load(keyword: string) {
        loading.value = true
        try {
          options.value = await props.fetcher(keyword)
        }
        finally {
          loading.value = false
        }
      }

      function onSearch(keyword: string) {
        if (searchTimer) clearTimeout(searchTimer)
        searchTimer = setTimeout(() => load(keyword), 300)
      }

      onMounted(() => { load('') })
      watch(() => props.fetcher, () => load(''))

      return () => h(NSelect, {
        value: props.value,
        options: options.value,
        multiple: props.multiple,
        disabled: props.disabled,
        placeholder: props.placeholder,
        filterable: true,
        remote: true,
        loading: loading.value,
        'onUpdate:value': (v: unknown) => emit('update:value', v),
        onSearch,
      } as Record<string, unknown>)
    },
  })
}
