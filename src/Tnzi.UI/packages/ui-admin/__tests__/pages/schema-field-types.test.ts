import { describe, it, expect, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { ref } from 'vue'
import { readFileSync, readdirSync, statSync } from 'node:fs'
import { join, relative } from 'node:path'
import TCrudSearchAdvanced from '../../src/components/crud/TCrudSearchAdvanced.vue'
import TFormSchemaRenderer, { type FormSchemaItem } from '../../src/pages/_shared/form-schema'
import { recordAccessSearchFields } from '../../src/pages/audit/record-access-config'
import { destructionSearchFields } from '../../src/pages/audit/destruction-config'
import { formSchema as cliRuntimeFormSchema } from '../../src/pages/ai/cli/cli-runtime-config'
import { templateFormSchema } from '../../src/pages/signing/signing-config'

/**
 * Free-text fields are `type: 'text'`.
 *
 * `FormSchemaFieldType` takes any string (a non-builtin type is a hook for a
 * page's own `fieldRenderers`), so the compiler cannot tell a typo from a custom
 * type. `'input'` reads like a builtin and is not one: the advanced search grid
 * rendered nothing for it (the filter cell stayed empty, nothing could be typed)
 * and the schema form rendered the value as plain text (a create form whose
 * required name could not be entered).
 */

const searchStubs = {
  Form: { name: 'Form', template: '<form><slot /></form>' },
  FormItem: { name: 'FormItem', template: '<div class="search-item"><slot /></div>' },
  Grid: { name: 'Grid', template: '<div><slot /></div>' },
  GridItem: { name: 'GridItem', template: '<div><slot /></div>' },
  Space: { name: 'Space', template: '<div><slot /></div>' },
  Button: { name: 'Button', template: '<button><slot /></button>' },
  Input: { name: 'Input', props: ['value'], template: '<input class="search-text" />' },
  Select: { name: 'Select', props: ['options', 'value'], template: '<select class="search-select" />' },
}

const formStubs = {
  Form: { template: '<form><slot /></form>' },
  FormItem: { props: ['label', 'path'], template: '<div class="form-item" :data-path="path"><slot /></div>' },
  Input: { props: ['value'], template: '<input class="ipt" />' },
  InputNumber: { template: '<input type="number" />' },
  Switch: { template: '<button class="sw" />' },
  Select: { props: ['options', 'value'], template: '<select class="sel" />' },
  DatePicker: { template: '<input type="date" />' },
}

function searchState() {
  return { query: ref({ searchText: '' }), setSearch: vi.fn(), setFilters: vi.fn(), refresh: vi.fn(async () => undefined) }
}

function textInputsInSearch(fields: FormSchemaItem[]): number {
  const w = mount(TCrudSearchAdvanced, {
    props: { state: searchState() as never, searchFields: fields, hideSubmit: true },
    global: { stubs: searchStubs },
  })
  return w.findAll('.search-text').length
}

/** Keys of the schema fields that render an editable text input. */
function editableTextKeys(schema: FormSchemaItem[]): string[] {
  const w = mount(TFormSchemaRenderer, {
    props: { schema, model: {}, readonly: false, fieldRenderers: { custom: () => null } },
    global: { stubs: formStubs },
  })
  return w
    .findAll('.form-item')
    .filter((item) => item.find('.ipt').exists())
    .map((item) => item.attributes('data-path') ?? '')
}

describe('free-text schema fields render an input', () => {
  it('Record Access filters can be typed into (resource type, record id, purpose)', () => {
    expect(textInputsInSearch(recordAccessSearchFields)).toBe(3)
  })

  it('Destruction certificates can be filtered by policy name', () => {
    expect(textInputsInSearch(destructionSearchFields)).toBe(1)
  })

  it('a CLI runtime can be given its required name', () => {
    expect(editableTextKeys(cliRuntimeFormSchema)).toContain('name')
  })

  it('a signing template can be given its name, category and host types', () => {
    expect(editableTextKeys(templateFormSchema)).toEqual(
      expect.arrayContaining(['name', 'category', 'hostEntityTypes']),
    )
  })

  it("no built-in page declares a field as type 'input'", () => {
    const srcDir = join(__dirname, '..', '..', 'src')
    const files: string[] = []
    const walk = (dir: string) => {
      for (const name of readdirSync(dir)) {
        const p = join(dir, name)
        if (statSync(p).isDirectory()) walk(p)
        else if (/\.(ts|vue)$/.test(name)) files.push(p)
      }
    }
    walk(srcDir)

    const hits: string[] = []
    for (const file of files) {
      const lines = readFileSync(file, 'utf8').split('\n')
      lines.forEach((line, i) => {
        if (/\btype:\s*(['"])input\1/.test(line)) hits.push(`${relative(srcDir, file)}:${i + 1}  ${line.trim()}`)
      })
    }
    expect(hits, `use type: 'text' for a free-text field:\n${hits.join('\n')}`).toEqual([])
  })
})
