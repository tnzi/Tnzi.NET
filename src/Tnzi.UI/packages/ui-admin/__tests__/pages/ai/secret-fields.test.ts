import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import TFormSchemaRenderer, { type FormSchemaItem } from '../../../src/pages/_shared/form-schema'
import { providerFormSchema } from '../../../src/pages/ai/providers/provider-config'
import {
  mcpServerFormSchema,
  toCreateMcpServerDto,
  toUpdateMcpServerDto,
} from '../../../src/pages/ai/mcp/mcp-server-config'

/**
 * Credentials typed into the admin forms (a provider API key, an MCP server
 * token) are masked, keep their "leave blank to keep" hint, and are not
 * offered the browser's saved sign-in password. And an MCP server switched to
 * auth type "none" stops carrying its stored token: the server attaches any
 * stored token as a Bearer header, so "none" with a token left behind still
 * authenticates.
 */

const stubs = {
  Form: { template: '<form><slot /></form>' },
  FormItem: { props: ['label', 'path'], template: '<div class="form-item"><slot /></div>' },
  Input: {
    name: 'Input',
    props: ['value', 'type', 'placeholder', 'inputProps', 'readonly'],
    template: '<input class="ipt" :type="type || \'text\'" :placeholder="placeholder" :autocomplete="inputProps && inputProps.autocomplete" />',
  },
  InputNumber: { template: '<input type="number" />' },
  Switch: { template: '<button class="sw" />' },
  Select: { props: ['options', 'value'], template: '<select class="sel" />' },
}

function secretInput(schema: FormSchemaItem[], key: string) {
  const field = schema.find((f) => f.key === key)!
  const wrapper = mount(TFormSchemaRenderer, {
    props: { schema: [field], model: {}, readonly: false },
    global: { stubs },
  })
  return wrapper.find('.ipt')
}

describe('secret fields in the AI admin forms', () => {
  it.each([
    ['provider API key', providerFormSchema, 'apiKey'],
    ['MCP server auth token', mcpServerFormSchema, 'authToken'],
  ])('the %s is a masked input that keeps its hint and is not autofilled', (_name, schema, key) => {
    const input = secretInput(schema, key)
    expect(input.attributes('type')).toBe('password')
    expect(input.attributes('placeholder')).toMatch(/blank/i)
    expect(input.attributes('autocomplete')).toBe('new-password')
  })

  it('a read-only password field never shows a value, stored or not', () => {
    const field: FormSchemaItem = { key: 'apiKey', label: 'API Key', type: 'password' }
    const wrapper = mount(TFormSchemaRenderer, {
      props: { schema: [field], model: {}, readonly: true, readonlyLayout: 'inputs' },
      global: { stubs },
    })
    expect(wrapper.text()).not.toContain('•')
  })
})

describe('MCP server auth type "none"', () => {
  it('an update to "none" clears the stored token', () => {
    expect(toUpdateMcpServerDto({ name: 'Docs', authType: 'none' }).authToken).toBe('')
  })

  it('an update with a real auth type and a blank token keeps the stored one', () => {
    expect('authToken' in toUpdateMcpServerDto({ name: 'Docs', authType: 'bearer', authToken: '' })).toBe(false)
  })

  it('a create with "none" sends no token even when one was typed before the switch', () => {
    expect(toCreateMcpServerDto({ name: 'Docs', authType: 'none', authToken: 'leftover' }).authToken).toBeNull()
  })
})
