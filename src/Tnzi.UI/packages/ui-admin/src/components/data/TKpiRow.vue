<script lang="ts">
import { Comment, defineComponent, Fragment, h, Text, type PropType, type VNode, type VNodeChild } from 'vue'
import { NGi, NGrid } from 'naive-ui'

/**
 * TKpiRow — uniform KPI strip container. Wraps an `NGrid`
 * (`responsive="screen"`, default `cols="1 s:2 m:4"`, 12px gap) and
 * auto-wraps every default-slot child in an `NGi`, so pages drop
 * `TStatCard`s in directly (including via `v-for`) without grid-item
 * boilerplate:
 *
 * ```vue
 * <TKpiRow cols="1 s:2 m:3">
 *   <TStatCard :label="t('kpi.total')" :value="total" />
 *   <TStatCard :label="t('kpi.active')" :value="active" tone="success" />
 * </TKpiRow>
 * ```
 *
 * Mobile (375px): the responsive cols string collapses the grid to 1–2
 * columns; pass a custom `cols` to override (Naive screen breakpoints:
 * s:640 / m:1024 / l:1280 / xl:1536).
 */
export default defineComponent({
  name: 'TKpiRow',
  props: {
    /** NGrid responsive cols string (or fixed number). */
    cols: { type: [String, Number] as PropType<string | number>, default: '1 s:2 m:4' },
    /** Uniform x/y gap in px. */
    gap: { type: Number, default: 12 },
  },
  setup(props, { slots }) {
    /**
     * Flatten the slot content into element/component vnodes: `v-for`
     * produces Fragment wrappers and `v-if="false"` produces comment nodes —
     * neither may be passed to NGrid directly (it only accepts NGi children).
     */
    function flatten(nodes: VNodeChild[]): VNode[] {
      const out: VNode[] = []
      for (const node of nodes) {
        if (node == null || typeof node === 'boolean') continue
        if (Array.isArray(node)) {
          out.push(...flatten(node))
          continue
        }
        if (typeof node === 'string' || typeof node === 'number') continue
        const vnode = node as VNode
        if (vnode.type === Comment || vnode.type === Text) continue
        if (vnode.type === Fragment) {
          out.push(...flatten((vnode.children as VNodeChild[]) ?? []))
          continue
        }
        out.push(vnode)
      }
      return out
    }

    return () => {
      const children = flatten(slots.default?.() ?? [])
      return h(
        NGrid,
        {
          class: 't-kpi-row',
          cols: props.cols,
          responsive: 'screen',
          xGap: props.gap,
          yGap: props.gap,
        },
        () => children.map((child, i) => h(NGi, { key: child.key ?? `kpi-${i}` }, () => child)),
      )
    }
  },
})
</script>
