/**
 * Shared account-field config for the code-login / register / password-recovery
 * modules. These three modules use a single account field that accepts an email
 * OR a phone number; this composable adapts the validation rule + label +
 * placeholder to which channels (SMS / email) the backend has enabled:
 *
 *   - both channels → accept either, label "Phone / Email"
 *   - SMS only      → phone rule, label "Phone"
 *   - email only    → email rule, label "Email"
 *
 * The actual `type` ('email' | 'phone') sent to the backend is detected per
 * submit via {@link detectAccountType}.
 */
import { computed, type ComputedRef } from 'vue'
import type { FormItemRule } from 'naive-ui'
import { useFormRules, type Translate } from './useFormRules'

export interface CodeChannels {
  sms: boolean
  email: boolean
}

export interface LoginAccountField {
  rule: ComputedRef<FormItemRule[]>
  label: ComputedRef<string>
  placeholder: ComputedRef<string>
}

export function useLoginAccountField(
  translate: Translate,
  channels: () => CodeChannels,
): LoginAccountField {
  const { rules: r } = useFormRules(translate)
  const onlySms = computed(() => channels().sms && !channels().email)
  const onlyEmail = computed(() => channels().email && !channels().sms)

  const rule = computed<FormItemRule[]>(() => {
    const base = onlySms.value
      ? r.phone
      : onlyEmail.value
        ? r.email
        : r.emailOrPhone(
            translate('admin.login.errorAccountFormat', 'Enter a valid email or phone number'),
          )
    // Tag each rule with key 'account' so the modules' per-field validation
    // before sending a code — `validate(_, r => r.key === 'account')` — actually
    // matches. naive-ui does NOT inject key/path onto merged rules, so without
    // this the predicate matches zero rules and the account is never validated.
    return base.map((ru) => ({ ...ru, key: 'account' }))
  })

  const label = computed(() => {
    if (onlySms.value) return translate('admin.login.identifier.phone', 'Phone')
    if (onlyEmail.value) return translate('admin.login.identifier.email', 'Email')
    return translate('admin.login.labels.phoneEmail', 'Phone / Email')
  })

  const placeholder = computed(() => {
    if (onlySms.value) return translate('admin.login.accountPlaceholderPhone', 'Enter phone number')
    if (onlyEmail.value) return translate('admin.login.accountPlaceholderEmail', 'Enter email')
    return translate('admin.login.phonePlaceholder', 'Enter phone or email')
  })

  return { rule, label, placeholder }
}
