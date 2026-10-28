/**
 * Recognising the billing access gate's refusal.
 *
 * When a billing account is suspended the server answers every gated request
 * with HTTP 402 and the error code below; SignalR hub invocations are refused
 * with a HubException whose message is the same code. Callers meet that refusal
 * in three shapes: the failed envelope a bridge receives, the `HttpError` that
 * `ensureOk` / `unwrapOk` throw from it, and the `Error` a hub invocation
 * rejects with. One predicate covers all three so no caller re-implements it.
 *
 * Matching is on the error code, not the status alone: 402 is also what a
 * payment channel can answer for an unrelated declined charge.
 */

/** The error code the billing access gate answers with. */
export const SUBSCRIPTION_SUSPENDED = 'SUBSCRIPTION_SUSPENDED';

/** What the gate says about the caller in `errorDetails`. */
export interface SubscriptionSuspendedDetails {
  standing?: string;
  /** Whether the caller may pay to restore the service. */
  canManage?: boolean;
}

interface ErrorShape {
  succeeded?: boolean;
  errorCode?: string;
  message?: string;
  errorDetails?: SubscriptionSuspendedDetails;
  details?: SubscriptionSuspendedDetails;
}

/**
 * Whether `value` is the access gate's refusal: a failed envelope or thrown
 * `HttpError` carrying `SUBSCRIPTION_SUSPENDED`, or a hub invocation error
 * whose message names it.
 */
export function isSubscriptionSuspendedError(value: unknown): boolean {
  if (!value || typeof value !== 'object') {
    return false;
  }
  const source = value as ErrorShape;
  if (source.succeeded === true) {
    return false;
  }
  if (source.errorCode === SUBSCRIPTION_SUSPENDED) {
    return true;
  }
  // HubException: the client surfaces the server's message inside a longer
  // sentence ("An unexpected error occurred invoking 'X' on the server.
  // HubException: SUBSCRIPTION_SUSPENDED"), and there is no error code field.
  return value instanceof Error && typeof source.message === 'string' && source.message.includes(SUBSCRIPTION_SUSPENDED);
}

/** The refusal's details (envelope `errorDetails` or `HttpError.details`), if any. */
export function subscriptionSuspendedDetailsOf(value: unknown): SubscriptionSuspendedDetails | undefined {
  if (!isSubscriptionSuspendedError(value)) {
    return undefined;
  }
  const source = value as ErrorShape;
  return source.errorDetails ?? source.details ?? undefined;
}
