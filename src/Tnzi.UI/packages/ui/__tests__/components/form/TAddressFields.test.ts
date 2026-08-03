import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { NInput } from 'naive-ui'
import TAddressFields, {
  type AddressValue,
} from '../../../src/components/form/fields/TAddressFields.vue'

/**
 * `keyMap` / `prefix` 存在的理由:真实模型几乎都不长 `AddressValue` 的样子。
 *
 * 缺陷由消费应用在拒绝采用本组件时指出:它的 DTO 是扁平列且把 region 拼作
 * `province`,还有记录在同一个对象上挂两个地址。没有这两个 prop 时,九个调用点
 * 每个都得写一层双向改名的适配器 —— 比组件本身还长。
 */
describe('TAddressFields key mapping', () => {
  /** 按顺序取出各字段的 NInput(street / unit / city / region / postal)。 */
  function inputs(wrapper: ReturnType<typeof mount>) {
    return wrapper.findAllComponents(NInput)
  }

  it('reads and writes the logical keys when no mapping is given', async () => {
    const wrapper = mount(TAddressFields, {
      props: { modelValue: { street: '1 King St', city: 'Toronto' } as AddressValue },
    })

    expect(inputs(wrapper)[0]!.props('value')).toBe('1 King St')

    inputs(wrapper)[2]!.vm.$emit('update:value', 'Ottawa')
    const emitted = wrapper.emitted('update:modelValue')!.at(-1)![0] as Record<string, unknown>
    expect(emitted.city).toBe('Ottawa')
  })

  it('reads through keyMap - the renamed key, not the logical one', () => {
    // 模型里叫 province;不映射读的话 region 框会是空的,而值就在旁边。
    const wrapper = mount(TAddressFields, {
      props: {
        modelValue: { province: 'ON' } as unknown as AddressValue,
        keyMap: { region: 'province' },
      },
    })

    // region 无 options 时渲染为 NInput,位置在 street/unit/city 之后。
    expect(inputs(wrapper)[3]!.props('value')).toBe('ON')
  })

  it('writes through keyMap and leaves every other key on the model alone', () => {
    const wrapper = mount(TAddressFields, {
      props: {
        modelValue: { province: 'ON', clientId: 42 } as unknown as AddressValue,
        keyMap: { region: 'province' },
      },
    })

    inputs(wrapper)[3]!.vm.$emit('update:value', 'QC')

    const emitted = wrapper.emitted('update:modelValue')!.at(-1)![0] as Record<string, unknown>
    expect(emitted.province).toBe('QC')
    // 扁平 DTO 上的其它列必须原样带回,否则直接绑 DTO 就是有损的。
    expect(emitted.clientId).toBe(42)
    expect(emitted).not.toHaveProperty('region')
  })

  it('camel-cases every key onto a prefix - the two-addresses-on-one-model case', () => {
    const wrapper = mount(TAddressFields, {
      props: {
        modelValue: {
          street: 'Home St',
          mailingStreet: 'PO Box 9',
          mailingCity: 'Laval',
        } as unknown as AddressValue,
        prefix: 'mailing',
      },
    })

    expect(inputs(wrapper)[0]!.props('value')).toBe('PO Box 9')
    expect(inputs(wrapper)[2]!.props('value')).toBe('Laval')

    inputs(wrapper)[2]!.vm.$emit('update:value', 'Montreal')
    const emitted = wrapper.emitted('update:modelValue')!.at(-1)![0] as Record<string, unknown>
    expect(emitted.mailingCity).toBe('Montreal')
    // 同一个模型上的另一个地址不能被碰。
    expect(emitted.street).toBe('Home St')
  })

  it('lets a keyMap entry win over the prefix', () => {
    // 第二个地址整体带前缀,唯独省份那一列沿用了别的拼法。
    const wrapper = mount(TAddressFields, {
      props: {
        modelValue: { mailingCity: 'Laval', mailingProvince: 'QC' } as unknown as AddressValue,
        prefix: 'mailing',
        keyMap: { region: 'mailingProvince' },
      },
    })

    expect(inputs(wrapper)[2]!.props('value')).toBe('Laval')
    expect(inputs(wrapper)[3]!.props('value')).toBe('QC')
  })
})
