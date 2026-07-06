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
}

/**
 * Cash flow statement activity classification
 */
export enum CashFlowActivity {
  Operating = 'Operating',
  Investing = 'Investing',
  Financing = 'Financing',
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
