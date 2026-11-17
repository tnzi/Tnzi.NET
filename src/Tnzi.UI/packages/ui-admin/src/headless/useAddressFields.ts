import { computed, type ComputedRef } from 'vue'
import type { SelectOption } from 'naive-ui'

/**
 * 地址的逻辑字段名。宿主的模型可以叫别的（见 `keyMap` / `prefix`）。
 */
export type AddressFieldKey = 'street' | 'unit' | 'city' | 'region' | 'postalCode' | 'country'

/** 一个地址字段，已解好宿主模型上的真实键。 */
export interface AddressField {
  /** 逻辑键，稳定，可直接当 `v-for` 的 key。 */
  key: AddressFieldKey
  /** 宿主模型上实际读写的键（经 `keyMap` / `prefix` 解析后）。 */
  modelKey: string
  /** 标签文案，已应用 `labels` 覆盖。 */
  label: string
  /** 控件形态：给了 `regionOptions` / `countryOptions` 的那两个字段是 select。 */
  type: 'input' | 'select'
  /** `type === 'select'` 时的候选项。 */
  options?: SelectOption[]
  /** 当前值。 */
  value: string | null
  /** 写回宿主模型：默认就地写；给了 `onUpdate` 则改为交出一个新对象。 */
  set: (value: string | null) => void
}

export interface UseAddressFieldsOptions {
  /** 省/州候选项；给了就渲染成 select。 */
  regionOptions?: SelectOption[]
  /** 国家候选项；给了就渲染成 select。 */
  countryOptions?: SelectOption[]
  /** 是否包含 country 字段。默认 false。 */
  showCountry?: boolean
  /** 逐字段标签覆盖。 */
  labels?: Partial<Record<AddressFieldKey, string>>
  /** region 的默认标签。 */
  regionLabel?: string
  /** postalCode 的默认标签。 */
  postalLabel?: string
  /**
   * 把逻辑键映射到宿主模型实际用的键。
   *
   * ★ 多数真实模型不长成 `AddressValue`：它们是 DTO 上的扁平列（叫 `province` 不叫 `region`），
   * 而且一条记录常常带两个地址。
   */
  keyMap?: Partial<Record<AddressFieldKey, string>>
  /**
   * 同一模型上第二个地址的简写：每个键驼峰拼到这个前缀后
   * （`mailing` → `mailingStreet` / `mailingCity` …）。`keyMap` 的条目优先。
   */
  prefix?: string
  /**
   * 给了它就改成「不可变写」：不动原模型，改为用 spread + 覆盖出一个新对象交给它。
   *
   * ★ 两种写法都对，取决于宿主拿的是什么：`reactive` 表单要就地写（替换整个对象会打断绑定），
   * 而 `v-model` 要新对象。`TAddressFields` 走的是后者。
   */
  onUpdate?: (next: Record<string, unknown>) => void
}

/**
 * 逻辑键 → 宿主模型上实际的键。
 *
 * ★ 单独导出，是因为 `TAddressFields` 与本 composable 的**写语义不同**（组件是 v-model，
 * 必须 emit 新对象；宿主表单是 reactive，必须就地写），所以它们共享不了 `set` ——
 * 但键映射这段必须只有一份，两份的那天不会有任何东西报错，只是有一个地址字段读不到值。
 */
export function resolveAddressKey(
  key: AddressFieldKey,
  options: Pick<UseAddressFieldsOptions, 'keyMap' | 'prefix'>
): string {
  const mapped = options.keyMap?.[key]
  if (mapped) return mapped
  if (options.prefix) return options.prefix + key.charAt(0).toUpperCase() + key.slice(1)
  return key
}

const ORDER: AddressFieldKey[] = ['street', 'unit', 'city', 'region', 'postalCode', 'country']

const DEFAULT_LABELS: Record<AddressFieldKey, string> = {
  street: 'Street address',
  unit: 'Unit / Suite',
  city: 'City',
  region: 'Province / State',
  postalCode: 'Postal / ZIP',
  country: 'Country',
}

/**
 * 地址字段的无渲染层：解好键映射、标签与控件形态，把「画成什么样」留给调用方。
 *
 * @remarks
 * ★★ **存在的理由是 `TAddressFields` 自带 `<label>` 网格。** 那套标签与间距在它自己的设计里
 * 是对的，但宿主应用的表单通常有自己的一套（`NFormItem` 栅格、统一的标签位置、统一的必填星号），
 * 把一个自带标签的块塞进去会明显地「像是贴上去的」。此前消费方为此**整份重写**了地址块 ——
 * 连带把键映射、前缀、省份候选这些本已解决的问题又解了一遍，而重写出来的那份又缺了别的。
 *
 * 现在两条路共享同一份逻辑：`TAddressFields` 消费它，需要自己画的宿主也消费它。
 *
 * ```ts
 * const fields = useAddressFields(() => form, {
 *   keyMap: { region: 'province' },
 *   regionOptions: provinces,
 *   labels: { unit: '单元 / 房号' },
 * })
 * ```
 * ```vue
 * <NFormItem v-for="f in fields" :key="f.key" :label="f.label">
 *   <NSelect v-if="f.type === 'select'" :value="f.value" :options="f.options"
 *            @update:value="f.set" />
 *   <NInput v-else :value="f.value" @update:value="f.set" />
 * </NFormItem>
 * ```
 *
 * ★ 字段顺序是 street / unit / city / region / postalCode /（country），与组件一致 ——
 * 两处顺序不同会让同一个应用里的两个地址块读起来像两件东西。
 *
 * ★ `set` 默认**就地写**宿主模型的属性：宿主拿到的多半是一个 `reactive` 表单，
 * 在它上面替换整个地址对象会打断已经建立的绑定。要不可变写法（`v-model` 那种）传 `onUpdate`。
 */
export function useAddressFields(
  model: () => Record<string, unknown> | null | undefined,
  options: UseAddressFieldsOptions = {}
): ComputedRef<AddressField[]> {
  const actualKey = (key: AddressFieldKey): string => resolveAddressKey(key, options)

  const labelOf = (key: AddressFieldKey): string => {
    const override = options.labels?.[key]
    if (override) return override
    if (key === 'region' && options.regionLabel) return options.regionLabel
    if (key === 'postalCode' && options.postalLabel) return options.postalLabel
    return DEFAULT_LABELS[key]
  }

  const optionsFor = (key: AddressFieldKey): SelectOption[] | undefined => {
    if (key === 'region') return options.regionOptions?.length ? options.regionOptions : undefined
    if (key === 'country') return options.countryOptions?.length ? options.countryOptions : undefined
    return undefined
  }

  return computed<AddressField[]>(() => {
    const target = (model() ?? {}) as Record<string, unknown>

    return ORDER.filter((key) => key !== 'country' || options.showCountry).map((key) => {
      const modelKey = actualKey(key)
      const choices = optionsFor(key)
      const raw = target[modelKey]

      return {
        key,
        modelKey,
        label: labelOf(key),
        type: choices ? 'select' : 'input',
        options: choices,
        value: raw == null ? null : String(raw),
        set: (value: string | null) => {
          if (options.onUpdate) options.onUpdate({ ...target, [modelKey]: value })
          else target[modelKey] = value
        },
      }
    })
  })
}
