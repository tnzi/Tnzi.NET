import { TemplateType } from '@tnzi/core/services/template'

/**
 * Select options for a template's rendering surface, shared by the template
 * library page and the notification-template page (both edit the same
 * backend DTO).
 *
 * `Sms` is load-bearing: the server renders an Sms body as plain text and
 * every other type HTML-encoded. The form must be able to show and send it;
 * the server keeps the stored value when the field is omitted, but an editor
 * that cannot set it cannot author an SMS template at all. Label keys resolve
 * under each page's own `type.*` locale block.
 */
export const templateTypeOptions: Array<{ label: string; labelKey: string; value: string }> = [
  { value: TemplateType.Generic, labelKey: 'type.generic', label: 'Generic' },
  { value: TemplateType.Email, labelKey: 'type.email', label: 'Email' },
  { value: TemplateType.Sms, labelKey: 'type.sms', label: 'SMS (plain text)' },
  { value: TemplateType.Page, labelKey: 'type.page', label: 'Page' },
  { value: TemplateType.Print, labelKey: 'type.print', label: 'Print' },
  { value: TemplateType.Pdf, labelKey: 'type.pdf', label: 'PDF' },
]
