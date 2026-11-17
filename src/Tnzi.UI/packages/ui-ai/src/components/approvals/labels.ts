/**
 * The framework's card words, from the dictionary (`AiLocaleMessages.approvals`) to the shape the
 * card builders of `run-requests.ts` take.
 */
import type { AiLocaleMessages } from '../../i18n/index';
import type { RunRequestCardLabels } from '../../headless/run-requests';

/** The framework's card words from the dictionary. */
export function runRequestCardLabels(words: AiLocaleMessages['approvals']): RunRequestCardLabels {
  return {
    permission: {
      title: words.permissionTitle,
      allowOnce: words.allowOnce,
      allowAlways: words.allowAlways,
      reject: words.reject,
      reason: words.reason,
      path: words.path,
      file: words.file,
      url: words.url,
      actions: {
        Bash: words.actionBash,
        Write: words.actionWrite,
        Edit: words.actionEdit,
        MultiEdit: words.actionEdit,
        NotebookEdit: words.actionNotebookEdit,
        WebFetch: words.actionWebFetch,
        WebSearch: words.actionWebSearch,
        other: words.actionOther,
      },
    },
    question: { title: words.questionTitle, titleMany: words.questionTitleMany },
    plan: {
      title: words.planTitle,
      approve: words.approve,
      approveAskEdits: words.approveAskEdits,
      approveAcceptEdits: words.approveAcceptEdits,
      reject: words.revise,
      draft: words.planDraft,
    },
  }
}
