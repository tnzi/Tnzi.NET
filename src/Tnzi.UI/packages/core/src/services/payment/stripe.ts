/**
 * Stripe.js, loaded on demand.
 *
 * `@stripe/stripe-js` is an OPTIONAL peer of this package: only an application
 * that collects cards in the browser needs it, and nothing imports it until a
 * card form actually mounts. Loading it here (not in a UI package) keeps one
 * loader for every UI package and one cache across tsup's duplicated entries.
 *
 * The types below are the slice of Stripe.js the framework uses, declared
 * structurally so this package's published types do not depend on a package
 * the consumer may not have installed. The real `Stripe` object satisfies them.
 */

import { createAdapterSingleton } from '../../adapters/singleton';

/** A Stripe API error as Stripe.js reports it. */
export interface StripeErrorLike {
  type?: string;
  code?: string;
  decline_code?: string;
  message?: string;
}

/** A mounted Stripe Element (the Payment Element). */
export interface StripeElementLike {
  mount(target: HTMLElement | string): void;
  destroy(): void;
  on(event: 'ready' | 'change' | 'loaderror', handler: (event: unknown) => void): unknown;
}

/** The Elements group created for one client secret. */
export interface StripeElementsLike {
  create(type: 'payment', options?: Record<string, unknown>): StripeElementLike;
  submit(): Promise<{ error?: StripeErrorLike }>;
}

/** Options shared by `confirmSetup` / `confirmPayment`. */
export interface StripeConfirmOptions {
  elements: StripeElementsLike;
  redirect: 'if_required';
  confirmParams?: { return_url?: string };
}

/** The part of the Stripe.js client the framework calls. */
export interface StripeLike {
  elements(options: { clientSecret: string; appearance?: Record<string, unknown> }): StripeElementsLike;
  confirmSetup(options: StripeConfirmOptions): Promise<{
    error?: StripeErrorLike;
    setupIntent?: { id?: string; status?: string; payment_method?: string | { id: string } | null };
  }>;
  confirmPayment(options: StripeConfirmOptions): Promise<{
    error?: StripeErrorLike;
    paymentIntent?: { id?: string; status?: string };
  }>;
}

/** `@stripe/stripe-js` is not installed in the application. */
export class StripeJsMissingError extends Error {
  constructor(cause?: unknown) {
    super('Card collection needs the @stripe/stripe-js package');
    this.name = 'StripeJsMissingError';
    if (cause !== undefined) {
      (this as { cause?: unknown }).cause = cause;
    }
  }
}

/** Cross-entry check (each tsup entry bundles its own copy of the class). */
export function isStripeJsMissingError(value: unknown): value is StripeJsMissingError {
  return value instanceof Error && value.name === 'StripeJsMissingError';
}

type StripeModule = { loadStripe: (publishableKey: string) => Promise<StripeLike | null> };

/** One load per publishable key, shared by every entry bundle. */
const loads = createAdapterSingleton<Map<string, Promise<StripeLike>>>('stripe-js-loads', () => new Map());

/**
 * Load Stripe.js and create the client for `publishableKey`.
 *
 * Rejects with {@link StripeJsMissingError} when the optional peer is not
 * installed, and with a plain `Error` when Stripe.js itself could not load
 * (blocked by the network or a content blocker). A failed load is not cached,
 * so mounting the form again retries.
 */
export function loadStripeClient(publishableKey: string): Promise<StripeLike> {
  if (!publishableKey) {
    return Promise.reject(new Error('Stripe publishable key is not configured'));
  }
  const cache = loads.use();
  const cached = cache.get(publishableKey);
  if (cached) {
    return cached;
  }
  const pending = importStripe()
    .then((mod) => mod.loadStripe(publishableKey))
    .then((stripe) => {
      if (!stripe) {
        throw new Error('Stripe.js could not be loaded');
      }
      return stripe;
    });
  cache.set(publishableKey, pending);
  pending.catch(() => cache.delete(publishableKey));
  return pending;
}

async function importStripe(): Promise<StripeModule> {
  try {
    return (await import('@stripe/stripe-js')) as unknown as StripeModule;
  } catch (error) {
    throw new StripeJsMissingError(error);
  }
}

/** Test isolation: forget cached loads. */
export function resetStripeClientCache(): void {
  loads.reset();
}

/**
 * The Stripe PaymentMethod id (`pm_...`) on a confirmed SetupIntent, which is
 * the token `POST payment-methods` binds.
 */
export function setupIntentPaymentMethodId(setupIntent: { payment_method?: string | { id: string } | null } | undefined): string | undefined {
  const pm = setupIntent?.payment_method;
  if (!pm) return undefined;
  return typeof pm === 'string' ? pm : pm.id;
}
