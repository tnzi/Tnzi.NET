// useDataTable / useDataList / useDataQuery were removed in the 2026-05-29
// audit cleanup: they re-implemented @tnzi/core's PaginationController /
// SortController / SelectionController / DataQueryController / FormController
// (zero consumers). Headless data/form state lives in @tnzi/core; bind its
// controllers (they are reactive via `vue`) instead of re-implementing here.
export * from './useEcharts'
