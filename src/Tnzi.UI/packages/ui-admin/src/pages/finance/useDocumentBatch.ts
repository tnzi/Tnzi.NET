import { computed, ref } from 'vue'
import type { Ref } from 'vue'
import { FinanceDocumentStatus } from '../../services/bridges/finance-bridge'
import type { FinanceDocRow } from './document-config'

/**
 * 单据列表的批量过账（规范 §5 标准 1「有批量操作」）。
 *
 * 三条不可妥协的行为，都是从"月底一次性过账当月单据"这个真实场景反推的：
 *
 * 1. **只处理草稿**。选中的行里通常混着已过账的（用户是先筛后全选），
 *    对它们调 post 只会收获一串 409。按钮上直接写"过账 3 张（共选 5 张）"，
 *    让人在点之前就知道会发生什么。
 * 2. **串行，不并行**。过账要分配连续单据号——并行会在号段行锁上互相等待，
 *    更糟的是一条失败时其余的成败无从谈起。
 * 3. **部分失败必须报出来**。批量操作最糟的失败模式是"看起来成功了，其实少了两张"，
 *    而那两张要到对账时才被发现。所以结果里同时给出成功数与逐条失败原因。
 */
export interface DocumentBatchOptions {
  /** 当前列表项（用来把选中的 id 解析回行，判断哪些是草稿）。 */
  items: Ref<FinanceDocRow[]>
  /** 单条过账。 */
  post: (id: string) => Promise<unknown>
  /** 页面级翻译器。 */
  translate: (key: string, params?: Record<string, unknown>) => string
  message: { success: (text: string) => void; error: (text: string) => void; warning?: (text: string) => void }
  refresh: () => Promise<unknown> | unknown
  /** 过账完成后清空选择。 */
  clearSelection: () => void
}

export function useDocumentBatch(options: DocumentBatchOptions) {
  const running = ref(false)
  const selectedIds = ref<string[]>([])

  /** 选中的行里有几张真的能过账——按钮文案与禁用都依赖它。 */
  const postableCount = computed(() => draftsOf(selectedIds.value).length)

  function draftsOf(ids: readonly (string | number)[]): FinanceDocRow[] {
    const wanted = new Set(ids.map(String))
    return options.items.value.filter(
      (row) => wanted.has(String(row.id ?? '')) && row.status === FinanceDocumentStatus.Draft,
    )
  }

  /** 供模板按当前选择算文案（`selectedIds` 由插槽传入，不进 store）。 */
  function countFor(ids: readonly (string | number)[]): number {
    return draftsOf(ids).length
  }

  async function postMany(ids: readonly (string | number)[]) {
    const drafts = draftsOf(ids)
    if (drafts.length === 0) {
      options.message.error(options.translate('batch.noDrafts'))
      return
    }

    running.value = true
    let posted = 0
    const failures: string[] = []

    try {
      // 串行：连续编号是行锁串行化的，且一条失败不该淹没其它条的结果。
      for (const row of drafts) {
        try {
          await options.post(String(row.id ?? ''))
          posted++
        } catch (error) {
          const label = row.number || row.id?.slice(0, 8) || '?'
          failures.push(`${label}: ${error instanceof Error ? error.message : String(error)}`)
        }
      }
    } finally {
      running.value = false
      await options.refresh()
      options.clearSelection()
    }

    if (failures.length === 0) {
      options.message.success(options.translate('batch.posted', { n: posted }))
      return
    }

    // ★部分失败单独报，且带上是哪几张——"看起来成功了其实少两张"要到对账时才发现。
    const detail = failures.slice(0, 3).join('; ')
    const more = failures.length > 3 ? options.translate('batch.andMore', { n: failures.length - 3 }) : ''
    options.message.error(
      `${options.translate('batch.partial', { ok: posted, failed: failures.length })} ${detail}${more}`,
    )
  }

  return { running, selectedIds, postableCount, countFor, postMany }
}
