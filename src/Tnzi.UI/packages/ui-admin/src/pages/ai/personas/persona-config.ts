import type { FormSchemaItem } from '../../_shared/form-schema'

/**
 * Personas page config (sibling of Personas.vue).
 *
 * Persona shape derives from @tnzi/core/services/ai AgentPersonaDto:
 *   { id, name, slug, content, description, scope (ResourceScope: System=0 shared / Tenant=1 private) }
 * `content` IS the soul/system-prompt body injected as a `<soul>` block by the
 * backend ContextInjectionMiddleware. Personas render as a TCardPage grid; the
 * card + detail drawer own presentation, so there are no table columns — only
 * the create/edit form schema + the keyword search field.
 */
export const personaFormSchema: FormSchemaItem[] = [
  { key: 'name', labelKey: 'form.name', label: 'Name', type: 'text', required: true },
  {
    key: 'slug',
    labelKey: 'form.slug',
    label: 'Slug',
    type: 'text',
    required: true,
    placeholder: 'lower-kebab-case, unique within tenant',
  },
  { key: 'description', labelKey: 'form.description', label: 'Description', type: 'textarea' },
  {
    key: 'content',
    labelKey: 'form.content',
    label: 'Persona Content',
    type: 'textarea',
    required: true,
    placeholder: 'The soul/system-prompt body injected as a <soul> block before every conversation.',
  },
  {
    key: 'scope',
    labelKey: 'form.scope',
    label: 'Scope',
    type: 'select',
    placeholder: 'Inferred from session when omitted (tenant -> Tenant, host -> System)',
    options: [
      { label: 'System (shared)', value: 0 },
      { label: 'Tenant (private)', value: 1 },
    ],
  },
]

/** Keyword search over name / slug / description. */
export const personaSearchFields: FormSchemaItem[] = [
  { key: 'keyword', labelKey: 'search.keyword', label: 'Keyword', type: 'text' },
]
