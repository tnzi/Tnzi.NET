/**
 * Account-identifier type detection for the login modules.
 *
 * The code-login / register / password-recovery modules use a single account
 * field that accepts either an email or a phone number, then send the backend
 * a `type` (`'email' | 'phone'`) telling it which channel to use. A value
 * containing `@` is treated as an email; anything else is treated as a phone
 * number (the backend re-validates the actual format).
 */
export function detectAccountType(value: string): 'email' | 'phone' {
  return value.includes('@') ? 'email' : 'phone'
}
