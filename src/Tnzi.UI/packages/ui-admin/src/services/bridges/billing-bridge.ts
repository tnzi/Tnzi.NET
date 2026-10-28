/**
 * Billing bridge - the customer side of subscription billing: the signed-in
 * user's billing standing, their billing account, subscription, saved cards,
 * the on-session "pay now" flow and their invoices.
 *
 * Every call goes through `unwrapOk` / `ensureOk`: a refused read must surface
 * as an error (a billing page that shows "no subscription" after a 403 states
 * something false), and a refused write must never be announced as done.
 */
import {
  useSubscriptionApi,
  useBillingAccountApi,
  usePaymentMethodApi,
  usePaymentApi,
  useInvoiceApi,
  type BillingStandingDto,
  type BillingAccountDto,
  type UpdateBillingAccountDto,
  type SubscriptionDto,
  type SubscriptionPayNowResultDto,
  type SubscriptionPlanDto,
  type SubscriptionCreateResultDto,
  type StorageUsageDto,
  type StoredPaymentMethodDto,
  type SetupSessionDto,
  type PaymentClientConfigDto,
  type InvoiceDto,
} from '@tnzi/core/services/payment'
import type { PagedList } from '@tnzi/core'

// The billing gate's refusal, recognised in every shape a caller meets it.
export { isSubscriptionSuspendedError } from '@tnzi/core/services/payment'
import { ensureOk, unwrapOk } from '../_mappers'

type HttpClient = Parameters<typeof useSubscriptionApi>[0]

export interface BillingBridgeDeps {
  client: HttpClient
}

export interface BillingBridge {
  /** `GET subscriptions/standing` - never blocked by the billing gate. */
  getStanding(productCode?: string): Promise<BillingStandingDto>
  /** `GET billing-account` - the account this user acts for and whether they may manage it. */
  getAccount(): Promise<BillingAccountDto>
  /**
   * `PUT billing-account` - the whole profile is overwritten: pass every field
   * back, not only the one that changed.
   */
  updateAccount(profile: UpdateBillingAccountDto): Promise<BillingAccountDto>
  /** The account's subscriptions, newest first. */
  listSubscriptions(): Promise<SubscriptionDto[]>
  /** Open (or reuse) the checkout payment for an overdue subscription, or a Pending one's first payment. */
  payNow(subscriptionId: string): Promise<SubscriptionPayNowResultDto>
  /** Active plans (all products when `productCode` is omitted), each with one period's tax. */
  listPlans(productCode?: string): Promise<SubscriptionPlanDto[]>
  /** `GET subscriptions/storage-usage` - storage against the plan's allowance and the overage accrued this period. */
  getStorageUsage(productCode?: string): Promise<StorageUsageDto>
  /**
   * Start a subscription on the server's default channel. Unless the plan
   * costs nothing, it stays Pending until the returned first payment is paid,
   * and that payment saves the card for renewals.
   */
  createSubscription(planId: string): Promise<SubscriptionCreateResultDto>
  /** End a subscription now (a Pending one: its first payment is closed with it). */
  cancelSubscription(subscriptionId: string, reason: string): Promise<void>
  /** Put a saved card on a subscription. */
  attachPaymentMethod(subscriptionId: string, paymentMethodId: string): Promise<SubscriptionDto>
  /** Ask the channel for a payment's latest result (so recovery does not wait for the webhook). */
  syncPayment(tradeNo: string): Promise<void>
  listPaymentMethods(): Promise<StoredPaymentMethodDto[]>
  createSetupSession(channelCode?: string): Promise<SetupSessionDto>
  /** Register the card collected by the setup session (`token` = Stripe `pm_...`). */
  bindPaymentMethod(token: string, channelCode: string | undefined, setAsDefault: boolean): Promise<StoredPaymentMethodDto>
  setDefaultPaymentMethod(id: string): Promise<void>
  removePaymentMethod(id: string): Promise<void>
  getClientConfig(channelCode?: string): Promise<PaymentClientConfigDto>
  listInvoices(pageIndex: number, pageSize: number): Promise<PagedList<InvoiceDto>>
  /** The invoice document as a file (authenticated download, never a link). */
  downloadInvoicePdf(id: string): Promise<Blob>
}

export function createBillingBridge(deps: BillingBridgeDeps): BillingBridge {
  const subscriptions = useSubscriptionApi(deps.client)
  const account = useBillingAccountApi(deps.client)
  const methods = usePaymentMethodApi(deps.client)
  const payments = usePaymentApi(deps.client)
  const invoices = useInvoiceApi(deps.client)

  return {
    getStanding: async (productCode) => unwrapOk<BillingStandingDto>(await subscriptions.getStanding(productCode)),
    getAccount: async () => unwrapOk<BillingAccountDto>(await account.get()),
    updateAccount: async (profile) => unwrapOk<BillingAccountDto>(await account.update(profile)),
    listSubscriptions: async () => {
      const page = unwrapOk<PagedList<SubscriptionDto>>(await subscriptions.getMy())
      return page?.items ?? []
    },
    payNow: async (subscriptionId) => unwrapOk<SubscriptionPayNowResultDto>(await subscriptions.payNow(subscriptionId)),
    listPlans: async (productCode) => unwrapOk<SubscriptionPlanDto[]>(await subscriptions.getPlans(true, productCode)) ?? [],
    getStorageUsage: async (productCode) => unwrapOk<StorageUsageDto>(await subscriptions.getStorageUsage(productCode)),
    createSubscription: async (planId) => unwrapOk<SubscriptionCreateResultDto>(await subscriptions.create({ planId })),
    cancelSubscription: async (subscriptionId, reason) => {
      ensureOk(await subscriptions.cancel(subscriptionId, { reason, immediate: true }))
    },
    attachPaymentMethod: async (subscriptionId, paymentMethodId) =>
      unwrapOk<SubscriptionDto>(await subscriptions.updatePaymentMethod(subscriptionId, { paymentMethodId })),
    syncPayment: async (tradeNo) => {
      ensureOk(await payments.sync(tradeNo))
    },
    listPaymentMethods: async () => unwrapOk<StoredPaymentMethodDto[]>(await methods.getList()) ?? [],
    createSetupSession: async (channelCode) =>
      unwrapOk<SetupSessionDto>(await methods.createSetupSession(channelCode ? { channelCode } : {})),
    bindPaymentMethod: async (token, channelCode, setAsDefault) =>
      unwrapOk<StoredPaymentMethodDto>(await methods.bind({ paymentMethodToken: token, channelCode, setAsDefault })),
    setDefaultPaymentMethod: async (id) => {
      ensureOk(await methods.setDefault(id))
    },
    removePaymentMethod: async (id) => {
      ensureOk(await methods.remove(id))
    },
    getClientConfig: async (channelCode) => unwrapOk<PaymentClientConfigDto>(await methods.getClientConfig(channelCode)),
    listInvoices: async (pageIndex, pageSize) =>
      unwrapOk<PagedList<InvoiceDto>>(await invoices.getMy({ pageIndex, pageSize })),
    downloadInvoicePdf: async (id) => unwrapOk<Blob>(await invoices.downloadPdf(id)),
  }
}
