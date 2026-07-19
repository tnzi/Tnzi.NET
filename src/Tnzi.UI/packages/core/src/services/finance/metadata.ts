/**
 * Finance Module Metadata - enums aligned with Tnzi.Finance backend.
 *
 * The framework serializes every enum wire value as its PascalCase member name
 * (global `JsonStringEnumConverter`, see Tnzi.AspNetCore), so these enums use
 * string values that match the C# member names. Responses carry the string
 * (e.g. `status: "Draft"`); requests still accept both the string and the
 * legacy integer (converter `allowIntegerValues` default), so sending a member
 * value on create/update is safe.
 */

/**
 * Account root type (the five accounting elements)
 */
export enum AccountRootType {
  Asset = 'Asset',
  Liability = 'Liability',
  Equity = 'Equity',
  Income = 'Income',
  Expense = 'Expense',
}

/**
 * System account role (resolved by role, never by hardcoded account code)
 */
export enum AccountSystemRole {
  AccountsReceivable = 'AccountsReceivable',
  AccountsPayable = 'AccountsPayable',
  TaxPayable = 'TaxPayable',
  TaxReceivable = 'TaxReceivable',
  RetainedEarnings = 'RetainedEarnings',
  ExchangeGainLoss = 'ExchangeGainLoss',
  RoundingDifference = 'RoundingDifference',
  UndepositedFunds = 'UndepositedFunds',
  OpeningBalance = 'OpeningBalance',
  /** Currency exchange clearing (cross-currency transfers net to zero through this account) */
  CurrencyExchangeClearing = 'CurrencyExchangeClearing',
}

/**
 * Cash flow statement activity classification
 */
export enum CashFlowActivity {
  Operating = 'Operating',
  Investing = 'Investing',
  Financing = 'Financing',
  /** Cash and cash equivalents: the subject the cash flow statement explains; excluded from activity buckets */
  CashEquivalent = 'CashEquivalent',
}

/**
 * Bank reconciliation status
 */
export enum ReconciliationStatus {
  Draft = 'Draft',
  Completed = 'Completed',
}

/**
 * Journal entry lifecycle status
 */
export enum JournalEntryStatus {
  Draft = 'Draft',
  Posted = 'Posted',
  Reversed = 'Reversed',
}

/** Finance document status (invoices, bills, expenses, credit memos, payments) */
export enum FinanceDocumentStatus {
  Draft = 'Draft',
  Posted = 'Posted',
  PartiallyPaid = 'PartiallyPaid',
  Paid = 'Paid',
  Voided = 'Voided',
}

/** Payment direction */
export enum PaymentDirection {
  Inbound = 'Inbound',
  Outbound = 'Outbound',
}

/** Finance party type */
export enum FinancePartyType {
  Customer = 'Customer',
  Vendor = 'Vendor',
}

/** Settlement document type (application source/target) */
export enum SettlementDocType {
  Invoice = 'Invoice',
  Bill = 'Bill',
  PaymentEntry = 'PaymentEntry',
  CreditMemo = 'CreditMemo',
}

/** Item type (catalog only, no inventory) */
export enum ItemType {
  Service = 'Service',
  Product = 'Product',
}

// ── P3: banking (bank accounts, remit-to, statement import) ─────

/** Bank number scheme (routing-number encoding; drives validation + MICR line) */
export enum BankNumberScheme {
  /** US ABA routing number (9 digits, mod-10 check digit) */
  UsAba = 'UsAba',
  /** Canada EFT (3-digit institution + 5-digit transit) */
  CaEft = 'CaEft',
}

/** Check stock type (pre-printed MICR vs blank stock) */
export enum CheckStockType {
  /** Pre-printed stock (MICR already printed; no MICR at print time) */
  PrePrinted = 'PrePrinted',
  /** Blank stock, fully printed (needs an E-13B MICR font path) */
  Blank = 'Blank',
}

/** Check layout */
export enum CheckLayout {
  /** Check + two stubs (voucher) */
  Voucher = 'Voucher',
  /** Three checks per page */
  ThreePerPage = 'ThreePerPage',
}

/** Party bank account type (drives the EFT transaction code) */
export enum BankAccountType {
  Checking = 'Checking',
  Savings = 'Savings',
}

/** Bank transaction import source */
export enum BankTransactionSource {
  /** OFX file import */
  Ofx = 'Ofx',
  /** CSV file import */
  Csv = 'Csv',
  /** Pulled from a bank feed provider */
  Provider = 'Provider',
}

/** Bank transaction match status */
export enum BankTransactionStatus {
  /** Unmatched (not linked to a ledger line, not excluded) */
  Pending = 'Pending',
  /** Matched (a reconciliation line was generated) */
  Matched = 'Matched',
  /** Excluded (manually judged noise, no ledger impact) */
  Excluded = 'Excluded',
}

/** Document type created from a bank transaction (delegates to existing draft workflows) */
export enum BankFeedDocType {
  /** Expense (outbound transaction, direct pay) */
  Expense = 'Expense',
  /** Payment entry */
  PaymentEntry = 'PaymentEntry',
  /** Funds transfer */
  Transfer = 'Transfer',
}

// ── P3: check printing / EFT output / receipt capture ───────────

/** Check status (all three states hold the check number; no physical delete) */
export enum CheckStatus {
  /** Issued (number allocated, printed or registered) */
  Issued = 'Issued',
  /** Voided (invalid, the number stays reserved for the audit trail) */
  Void = 'Void',
  /** Spoiled (damaged / misaligned blank stock, number reserved, no payment) */
  Spoiled = 'Spoiled',
}

/** EFT file format (drives the fixed-width layout and currency constraint) */
export enum EftFileFormat {
  /** US NACHA ACH (94-char fixed record, base currency USD) */
  Nacha = 'Nacha',
  /** Canada CPA-005 (1464-char logical record, currency CAD) */
  Cpa005 = 'Cpa005',
}

/** EFT batch status */
export enum EftBatchStatus {
  /** Draft (lines can be added/removed, editable, voidable) */
  Draft = 'Draft',
  /** Generated (file encrypted and frozen; download or void only) */
  Generated = 'Generated',
  /** Voided (lines released; the linked payments can re-enter another batch) */
  Voided = 'Voided',
}

/** Receipt capture status */
export enum ReceiptStatus {
  /** Uploaded (not yet extracted) */
  Uploaded = 'Uploaded',
  /** Extracted (fields can be corrected manually) */
  Extracted = 'Extracted',
  /** Converted (an expense / bill draft was created; cannot delete) */
  Converted = 'Converted',
  /** Failed (extraction failed; retry-able) */
  Failed = 'Failed',
}

/** Target document type a receipt converts into */
export enum ReceiptDocType {
  /** Expense (direct pay) */
  Expense = 'Expense',
  /** Bill (forms an AP balance) */
  Bill = 'Bill',
}

/**
 * Balance-summary verify difference kind (diagnostic, not self-healing).
 *
 * Batch F: the account period-balance summary buckets are cross-checked
 * against the ledger by `admin/finance/balance-summary/verify`; each row
 * carries one of these kinds.
 */
export enum BalanceSummaryDifferenceKind {
  /** Missing: the ledger expects a bucket but the summary has none. */
  Missing = 'Missing',
  /** Extra: the summary has a bucket the ledger has no lines for. */
  Extra = 'Extra',
  /** Mismatch: the bucket exists but its amounts / line count disagree. */
  Mismatch = 'Mismatch',
}

/**
 * Suggested settlement instrument values for PaymentEntry/Expense `paymentMethod`.
 * The field is free-form on the backend (jurisdiction-specific instruments vary);
 * these mirror the `Tnzi.Finance.Metadata.PaymentMethods` constants.
 */
export const PAYMENT_METHODS = [
  'Cash',
  'Check',
  'CreditCard',
  'DebitCard',
  'BankTransfer',
  'Wire',
  'Other',
] as const;

export type PaymentMethod = (typeof PAYMENT_METHODS)[number] | (string & {});

/**
 * Journal `sourceType` tokens the framework writes when a business document is
 * projected into the ledger (mirrors `Tnzi.Finance.Metadata.FinanceSourceTypes`).
 *
 * These are wire tokens, not entity names: the backend deliberately freezes them
 * as literals so renaming a framework entity cannot silently change what lands in
 * the database or what a resolver has to match on. Consuming apps that post
 * programmatically may write their own tokens, so treat this as the framework's
 * set rather than a closed enum — always fall back to the raw token when labelling.
 */
export const FINANCE_SOURCE_TYPES = [
  'Invoice',
  'Bill',
  'CreditMemo',
  'Expense',
  'PaymentEntry',
  'PaymentApplication',
  'Transfer',
  'Revaluation',
] as const;

export type FinanceSourceType = (typeof FINANCE_SOURCE_TYPES)[number] | (string & {});
