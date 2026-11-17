/** One choice of a `TSelect`. */
export interface TSelectOption<V extends string | number | null = string> {
  readonly value: V
  readonly label: string
  readonly disabled?: boolean
}

/** One choice of a `TMultiSelect`. */
export interface TMultiSelectOption<V extends string = string> {
  readonly value: V
  readonly label: string
  /** Attributes for the option's checkbox (a `data-*` to find it by in tests). */
  readonly attrs?: Readonly<Record<string, string>>
}
