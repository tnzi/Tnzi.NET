import { defineComponent, h, type PropType } from 'vue'
import { NButton, NInput, NInputNumber, NSelect } from 'naive-ui'
import type { FieldRenderContext } from '../../_shared/form-schema'
import type { SelectOption } from '../options'
import { formatMoney } from '../../../utils/finance-format'

/**
 * Template lines for a recurring document.
 *
 * Deliberately holds **unit price, not amount**: a price rise is edited here
 * and the documents already generated stay exactly as they were - they are
 * facts, not projections.
 */
export const RecurringLinesEditor = defineComponent({
  name: 'RecurringLinesEditor',
  props: {
    ctx: { type: Object as PropType<FieldRenderContext>, required: true },
    /** 表单数据。FieldRenderContext 不带 model，故由页面传进来。 */
    model: { type: Object as PropType<Record<string, unknown>>, required: true },
    accountOptions: { type: Array as PropType<SelectOption[]>, default: () => [] },
    taxOptions: { type: Array as PropType<SelectOption[]>, default: () => [] },
    translate: { type: Function as PropType<(key: string) => string>, required: true },
  },
  setup(props) {
    const model = () => props.model
    const rows = (): Array<Record<string, unknown>> => {
      const m = model()
      if (!Array.isArray(m.lines)) m.lines = []
      return m.lines as Array<Record<string, unknown>>
    }

    const add = () => rows().push({ description: '', accountId: null, quantity: 1, unitPrice: 0, taxCodeId: null })
    const remove = (index: number) => rows().splice(index, 1)

    return () =>
      h('div', { class: 'fin-rec-lines flex flex-col gap-6px' }, [
        ...rows().map((line, index) =>
          h('div', { class: 'fin-rec-lines__row', key: index }, [
            h(NInput, {
              value: (line.description as string) ?? '',
              size: 'small',
              disabled: props.ctx.readonly,
              placeholder: props.translate('lines.description'),
              'onUpdate:value': (v: string) => (line.description = v),
              class: 'fin-rec-lines__cell fin-rec-lines__desc',
              'data-label': props.translate('lines.description'),
            }),
            h(NSelect, {
              value: (line.accountId as string) ?? null,
              size: 'small',
              filterable: true,
              clearable: true,
              disabled: props.ctx.readonly,
              placeholder: props.translate('lines.account'),
              options: props.accountOptions,
              'onUpdate:value': (v: string) => (line.accountId = v),
              class: 'fin-rec-lines__cell fin-rec-lines__acct',
              'data-label': props.translate('lines.account'),
            } as Record<string, unknown>),
            h(NInputNumber, {
              value: (line.quantity as number) ?? 1,
              size: 'small',
              min: 0,
              disabled: props.ctx.readonly,
              'onUpdate:value': (v: number | null) => (line.quantity = v ?? 1),
              class: 'fin-rec-lines__cell fin-rec-lines__qty',
              'data-label': props.translate('lines.qty'),
            }),
            h(NInputNumber, {
              value: (line.unitPrice as number) ?? 0,
              size: 'small',
              disabled: props.ctx.readonly,
              'onUpdate:value': (v: number | null) => (line.unitPrice = v ?? 0),
              class: 'fin-rec-lines__cell fin-rec-lines__price',
              'data-label': props.translate('lines.price'),
            }),
            h(NSelect, {
              value: (line.taxCodeId as string) ?? null,
              size: 'small',
              clearable: true,
              disabled: props.ctx.readonly,
              placeholder: props.translate('lines.tax'),
              options: props.taxOptions,
              'onUpdate:value': (v: string) => (line.taxCodeId = v),
              class: 'fin-rec-lines__cell fin-rec-lines__tax',
              'data-label': props.translate('lines.tax'),
            } as Record<string, unknown>),
            props.ctx.readonly
              ? null
              : h(NButton, { size: 'tiny', quaternary: true, onClick: () => remove(index) }, { default: () => '×' }),
          ]),
        ),
        h('div', { class: 'fin-rec-lines__foot flex items-center justify-between gap-12px mt-4px' }, [
          props.ctx.readonly
            ? null
            : h(NButton, { size: 'small', dashed: true, onClick: add }, { default: () => props.translate('lines.add') }),
          h(
            'span',
            { class: 'fin-rec-lines__total font-600' },
            `${props.translate('lines.total')} ${formatMoney(
              rows().reduce((sum, l) => sum + Number(l.quantity ?? 0) * Number(l.unitPrice ?? 0), 0),
              { currency: model().currency as string | undefined },
            )}`,
          ),
        ]),
      ])
  },
})

export default RecurringLinesEditor
