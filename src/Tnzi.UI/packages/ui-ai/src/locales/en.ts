export const en = {
  chat: {
    placeholder: 'Type a message...',
    send: 'Send',
    stop: 'Stop generating',
    scrollToLatest: 'Scroll to the latest message',
    asideLabel: 'Side panel',
    asideShow: 'Show side panel',
    asideHide: 'Hide side panel',
    asideBack: 'Back to the conversation',
    asideResize: 'Resize side panel',
    retry: 'Retry',
    copy: 'Copy',
    copied: 'Copied',
    newChat: 'New chat',
    deleteChat: 'Delete chat',
    clearHistory: 'Clear history',
    emptyState: 'Start a conversation',
    errorGeneric: 'Something went wrong. Please try again.',
    errorNetwork: 'Network error. Please check your connection.',
    errorRateLimit: 'Rate limit exceeded. Please wait a moment.',
    generationStopped: 'Generation stopped',
    /** A turn that only compacted the conversation's context (`/compact`). */
    conversationCompacted: 'Conversation compacted',
    /** An answer is done and the agent waits for its background tasks before it goes on (the tasks follow). */
    waitingForBackgroundTasks: 'Still running in the background',
    /** Between two answers with no user message between them: the agent went on by itself once a background task finished. */
    continuedAfterBackgroundTask: 'Continued after a background task',
    regenerate: 'Regenerate',
    like: 'Like',
    dislike: 'Dislike',
    /** Arrows between the versions of a message (an edited question, a regenerated answer). */
    previousVersion: 'Previous version',
    nextVersion: 'Next version',
    deleteConversation: 'Delete conversation',
    streaming: 'Streaming',
    generating: 'Generating...',
    copyCode: 'Copy code',
    /** Default name on assistant turns that carry none of their own. */
    agentName: 'Assistant',
    /** Heading above the conversation history in the sidebar. */
    threadsLabel: 'All tasks',
    /** Inline confirmation on a thread row's delete action. */
    deleteConfirm: 'Delete?',
    /** A reply the agent could not give: the account it runs on reached its usage limit. */
    usageLimit: "This assistant's subscription has reached its usage limit.",
    /** {time} = when the limit resets, in the user's locale. */
    usageLimitResets: 'It should be available again at about {time}.',
    editMessage: 'Edit message',
    saveEdit: 'Send',
    cancelEdit: 'Cancel',
    searchThreads: 'Search conversations',
    noThreadsFound: 'No conversations found',
    loadMoreThreads: 'Show more',
    loadingOlder: 'Loading earlier messages...',
    toolInput: 'Input',
    toolResult: 'Result',
    showToolDetails: 'Show details',
    hideToolDetails: 'Hide details',
    /** {name} = file name. */
    openFile: 'Open {name}',
    attachmentUnavailable: 'Not available',
    /** An image attachment's name when it has none of its own. */
    image: 'Image',
    /** Read out while a conversation picked in the sidebar loads. */
    openingThread: 'Opening conversation...',
  },
  /* The sidebar's recent conversations and the full Recents page. */
  recents: {
    /** BCP-47 tag for dates in this dictionary's language. */
    dateLocale: 'en-US',
    viewAll: 'View all',
    search: 'Search',
    filterAndGroup: 'Filter and group',
    /** The "..." menu of a conversation. */
    moreActions: 'More actions',
    status: 'Status',
    statusActive: 'Active',
    statusArchived: 'Archived',
    statusAll: 'All',
    /** The time window filter: when the user last wrote in a conversation (or created it). */
    lastActivity: 'Last message',
    window1d: '1d',
    window3d: '3d',
    window7d: '7d',
    window30d: '30d',
    windowAll: 'All',
    groupBy: 'Group by',
    groupDate: 'Date',
    groupNone: 'None',
    sortBy: 'Sort by',
    sortName: 'Name',
    sortCreated: 'Date created',
    /** Sort by when the user last wrote in a conversation (the default). */
    sortLastMessage: 'Last message',
    /** Heading above the conversations waiting for the user, when the list is grouped by date. */
    groupAwaitingOwner: 'Needs you',
    /** Heading above the conversations with a turn going on, when the list is grouped by date. */
    groupRunning: 'Working',
    today: 'Today',
    yesterday: 'Yesterday',
    previous7: 'Previous 7 days',
    previous30: 'Previous 30 days',
    rename: 'Rename',
    archive: 'Archive',
    unarchive: 'Unarchive',
    /** Read after a conversation's title while a turn of it waits for the user (its row shows a raised hand). */
    threadAwaitingOwner: 'Needs you',
    /** Read after a conversation's title while a turn of it is going on (its row shows a running mark). */
    threadRunning: 'Working',
    /** Read after a conversation's title when it has a reply not seen yet (its row shows a dot). */
    threadUnread: 'Unread',
    delete: 'Delete',
    /** The Active / Archived tabs of the Recents page. */
    tabs: 'Conversation status',
    searchPlaceholder: 'Search recents...',
    closeSearch: 'Close search',
    /** The top bar's inline confirmation before deleting the open conversation. */
    deleteThreadConfirm: 'Delete this conversation?',
    emptyActive: 'No conversations yet.',
    emptyArchived: 'No archived conversations.',
  },
  /* Tool steps in an answer (TToolStep and the views built on it). */
  tools: {
    /** A step's state, read out before its words. */
    running: 'Running',
    completed: 'Done',
    failed: 'Failed',
    pending: 'Not finished',
    /** {count} = number of lines. */
    showAll: 'Show all {count} lines',
    showLess: 'Show less',
    /** Heading above the pages a search found. */
    results: 'Results',
    noResults: 'No results',
    /** The CLI's WebSearch: the query follows. */
    webSearchRunning: 'Searching the web',
    webSearchDone: 'Searched the web',
    webSearchFailed: "Couldn't search the web",
    /** The CLI's WebFetch: the site follows. */
    webFetchRunning: 'Reading',
    webFetchDone: 'Read',
    webFetchFailed: "Couldn't read",
    /** A run of steps summed up in one line: what joins its parts. */
    summaryJoin: ', ',
    /** {count} = steps the summary does not name. */
    moreSteps: '{count} more',
    /** {count} = failed steps (two or more). */
    stepsFailed: '{count} steps failed',
    /** One step failed and a later step of its kind then did it. */
    stepRecovered: 'A step retried after an error',
    /** {count} = steps that failed and were then done again (two or more). */
    stepsRecovered: '{count} steps retried after errors',
    /** {count} = steps the answer stopped before they finished. */
    stepsNotFinished: '{count} not finished',
    /** A tool that brings no words of its own. {tool} = its readable name. */
    usingTool: 'Using {tool}',
    usedTool: 'Used {tool}',
    /** {count} = how many times. */
    usedToolTimes: 'Used {tool} {count} times',
    toolFailed: '{tool} failed',
    /** The CLI's WebFetch in a run's summary. {count} = pages read. */
    readingPage: 'Reading a page',
    readPage: 'Read a page',
    readPages: 'Read {count} pages',
    /** The CLI's file and shell tools (Read, Edit, Write, Bash, Grep, Glob). {count} = how many. */
    readingFile: 'Reading a file',
    readFile: 'Read a file',
    readFiles: 'Read {count} files',
    readFileFailed: "Couldn't read a file",
    editingFile: 'Editing a file',
    editedFile: 'Edited a file',
    editedFiles: 'Edited {count} files',
    editFileFailed: "Couldn't edit a file",
    writingFile: 'Writing a file',
    wroteFile: 'Wrote a file',
    wroteFiles: 'Wrote {count} files',
    writeFileFailed: "Couldn't write a file",
    runningCommand: 'Running a command',
    ranCommand: 'Ran a command',
    ranCommands: 'Ran {count} commands',
    commandFailed: 'A command failed',
    searchingCode: 'Searching the code',
    searchedCode: 'Searched the code',
    searchCodeFailed: "Couldn't search the code",
    readPageFailed: "Couldn't read a page",
    /** An inline widget (show_widget): its title follows. {count} = widgets made in one run. */
    widgetRunning: 'Creating a widget',
    widgetDone: 'Created a widget',
    widgetFailed: "Couldn't create a widget",
    widgetsDone: 'Created {count} widgets',
    /**
     * The CLI's AskUserQuestion: the agent asks the person; the first question follows.
     * `...Declined`: the person chose not to answer, or no one answered in time (an outcome, not a failure).
     */
    askQuestionRunning: 'Asking you a question',
    askQuestionDone: 'Got your answer',
    askQuestionDeclined: 'Question left unanswered',
    askQuestionFailed: "Couldn't ask a question",
    /** The CLI's EnterPlanMode: the agent starts planning before it changes anything. */
    enterPlanModeRunning: 'Starting a plan',
    enterPlanModeDone: 'Started a plan',
    enterPlanModeFailed: "Couldn't start a plan",
    /**
     * The CLI's ExitPlanMode: the agent hands over its plan and asks to start.
     * `...Declined`: the plan went back to the agent (with feedback, or unanswered in time) and it keeps planning.
     */
    exitPlanModeRunning: 'Waiting for the plan to be approved',
    exitPlanModeDone: 'Plan approved',
    exitPlanModeDeclined: 'Plan sent back',
    exitPlanModeFailed: "Couldn't hand over the plan",
  },
  /* TWidgetCall / TWidgetFrame - an interactive widget an agent put into its reply. */
  widget: {
    /** While the agent is still writing it. */
    building: 'Building the widget…',
    /** Shown when the widget has no title. */
    untitled: 'Widget',
    expand: 'Open larger',
    close: 'Close',
    copySource: 'Copy HTML',
    copied: 'Copied',
    download: 'Download HTML',
    /** The widget navigated its frame away and was stopped. */
    navigated: 'This widget tried to leave the page and was stopped.',
    reload: 'Reload',
    /** The product registered the renderer without a usable runner URL. */
    noRunner: 'This widget cannot be shown here.',
    /** The widget asks to send a message as the user; the text follows. */
    messageRequest: 'The widget wants to send this message:',
    send: 'Send',
    cancel: 'Cancel',
  },
  /* TLandingPage - the empty state before the first message. */
  landing: {
    greeting: 'What can I do for you?',
    placeholder: 'Assign a task or ask anything',
  },
  /* The signed-out welcome screen (defineChatApp({ guestWelcome })). */
  welcome: {
    signIn: 'Sign in',
    /** Under the composer: what happens when the visitor sends. */
    hint: 'You will be asked to sign in first, and your message is sent right after.',
  },
  sidebar: {
    collapse: 'Collapse sidebar',
    /** The same button on a narrow screen, where the sidebar is a drawer. */
    close: 'Close sidebar',
    expand: 'Expand sidebar',
    show: 'Show sidebar',
    settings: 'Settings',
    commandPalette: 'Command palette',
    /** {group} = nav group label. */
    collapseGroup: 'Collapse {group}',
    expandGroup: 'Expand {group}',
  },
  composer: {
    /** Accessible name of the model picker. */
    model: 'Model',
    /** Title of the effort row in the model picker. */
    effort: 'Thinking effort',
    /** What a screen reader hears for the mark on the model's default effort. */
    effortDefault: 'Default',
    /**
     * Names of reasoning efforts, by the runtime's own value (Claude Code's and Codex's levels, Kimi Code's off / on
     * switch); a value not listed here is shown as it is.
     */
    effortNames: {
      none: 'None',
      minimal: 'Minimal',
      low: 'Low',
      medium: 'Medium',
      high: 'High',
      xhigh: 'Extra high',
      max: 'Max',
      off: 'Off',
      on: 'On',
    } as Record<string, string>,
    /** One line under the effort row, by the chosen value; none for a value not listed here. */
    effortHints: {
      none: 'Answers without thinking first',
      minimal: 'Barely thinks: the quickest replies',
      low: 'Quick answers for simple questions, using the least',
      medium: 'A balance of speed, depth and usage',
      high: 'Thinks longer before answering harder questions',
      xhigh: 'Thinks at length for complex, many-step work',
      max: 'The deepest thinking: slowest, and uses the most',
      off: 'Answers without thinking first',
      on: 'Thinks before answering',
    } as Record<string, string>,
    attach: 'Attach files',
    removeAttachment: 'Remove attachment',
    voiceInput: 'Voice input',
    /** The primary button while dictating: ends it, keeping what was heard. */
    stopRecording: 'Stop dictation',
    /** Placeholder and screen-reader status while dictating. */
    listening: 'Listening…',
    send: 'Send',
    /** The primary button while a turn runs and the box has a message: it goes once the turn has ended. */
    queue: 'Send when this reply ends',
    /** {size} = human-readable limit, {names} = comma-separated file names. */
    filesTooLarge: 'Not attached, larger than {size}: {names}',
    /** {names} = comma-separated file names. */
    filesNotAccepted: 'Not attached, this type of file is not accepted here: {names}',
    dismiss: 'Dismiss',
    /* Why voice input stopped: what happened and what the user can do. */
    voiceMicBlocked:
      'Microphone access is blocked for this site. Click the icon at the left of the address bar, allow the microphone, then try again.',
    voiceMicNotGranted:
      "Microphone access wasn't granted. If your browser asked, choose Allow and try again. If it didn't ask, this browser or app doesn't let this page use the microphone.",
    voiceServiceNotAllowed:
      "This browser doesn't provide speech recognition on this page. Type your message instead, or try another browser.",
    voiceNoMicrophone: 'No microphone could be used. Connect one, or close other apps that are using it, then try again.',
    voiceNetwork:
      "Couldn't reach the speech recognition service. Check your connection and try again. Some browsers and apps don't provide this service; type your message instead.",
    voiceNoSpeech: 'No speech was heard. Check that the right microphone is selected, then try again.',
    voiceLanguageNotSupported: "Voice input doesn't support this language in this browser. Type your message instead.",
    voiceAborted: 'Voice input was interrupted. Try again.',
    voiceUnavailable: "Voice input couldn't start. Try again, or type your message instead.",
  },
  account: {
    signOut: 'Sign out',
    email: 'Email',
    emailHint: 'Your login email address',
    role: 'Role',
    roleHint: 'Your system role and permissions',
    fallbackName: 'User',
    accountLabel: 'Account',
    userMenu: 'Account menu',
    switchAccount: 'Switch account',
    /** Back row of a submenu in the account menu. {label} = the item that opened it. */
    backTo: 'Back to {label}',
  },
  settings: {
    appearance: 'Appearance',
    /* Built-in section names in the settings dialog. */
    security: 'Security',
    personalization: 'Personalization',
    usage: 'Usage',
    about: 'About',
    themeLight: 'Light',
    themeDark: 'Dark',
    themeSystem: 'Follow System',
    /** {brand} = brand name passed to TChatApp. */
    poweredBy: 'Powered by Tnzi.NET, a modular AI framework.',
    search: 'Search',
    searchAria: 'Search settings',
    noResults: 'No matching settings.',
    /** Accessible name of the settings dialog's section list. */
    sections: 'Settings sections',
    language: 'Language',
    languageHint: 'Interface language.',
    theme: 'Theme',
    themeHint: 'Follow the system or pick one.',
    /* TStepUpPrompt - re-authentication for [RequireStepUp] account writes. */
    stepUpTitle: 'Confirm it is you',
    stepUpIntro: 'This action requires you to verify it is really you before it goes ahead.',
    stepUpMethodPasskey: 'Use a passkey',
    stepUpMethodTotp: 'Authenticator app code',
    stepUpMethodSms: 'Send a code by SMS',
    stepUpMethodEmail: 'Send a code by email',
    stepUpNoMethods:
      'No verification method is available for this account. Add a passkey, or verify your email address or phone number, then try again.',
    stepUpCodeHintTotp: 'Enter the code from your authenticator app.',
    /** {to} = masked address the code went to. */
    stepUpCodeSentTo: 'We sent a code to {to}.',
    stepUpCodeSent: 'We sent you a code.',
    stepUpCodePlaceholder: 'Verification code',
    stepUpBack: 'Use another method',
    stepUpResend: 'Resend code',
    stepUpCancel: 'Cancel',
    stepUpVerify: 'Verify',
  },
  /* TAccountSettings - the signed-in user's own profile and password. */
  accountSettings: {
    profileTitle: 'Profile',
    displayName: 'Display name',
    displayNameHint: 'How you appear in this product.',
    email: 'Email',
    emailHint: 'A code is sent to the new address to prove you own it.',
    phone: 'Phone',
    phoneHint: 'A code is sent to the new number to prove you own it.',
    notSet: 'Not set',
    change: 'Change',
    newEmail: 'New email',
    newPhone: 'New phone',
    codeSentHint: 'Enter the code we just sent there.',
    willSendHint: 'We will send a verification code to it.',
    emailPlaceholder: 'name@example.com',
    phonePlaceholder: 'Phone number',
    codePlaceholder: 'Verification code',
    cancel: 'Cancel',
    sendCode: 'Send code',
    confirm: 'Confirm',
    emailUpdated: 'Email updated.',
    phoneUpdated: 'Phone updated.',
    reset: 'Reset',
    save: 'Save',
    passwordTitle: 'Password',
    currentPassword: 'Current password',
    newPassword: 'New password',
    confirmPassword: 'Confirm new password',
    currentPasswordPlaceholder: 'Enter your current password',
    newPasswordPlaceholder: 'Enter a new password',
    confirmPasswordPlaceholder: 'Enter the new password again',
    passwordMismatch: 'The two new passwords do not match.',
    passwordUpdated: 'Password updated.',
    updatePassword: 'Update password',
    showPassword: 'Show password',
    hidePassword: 'Hide password',
    photo: 'Profile photo',
    photoHint: 'PNG, JPEG, or WebP, up to {size} MB.',
    photoChange: 'Change profile photo',
    photoRemove: 'Remove profile photo',
    photoWrongType: 'Choose a PNG, JPEG, or WebP image.',
    photoTooLarge: 'That image is larger than {size} MB.',
    photoFailed: 'The photo could not be saved.',
    deleteTitle: 'Delete account',
    deleteLabel: 'Delete your account',
    deleteHint: 'You will be signed out everywhere and will not be able to sign in again.',
    deleteButton: 'Delete account',
    deleteConfirmLabel: 'Confirm deletion',
    deleteTypeToConfirm: 'Type {target} to confirm.',
    deleteConfirmButton: 'Delete my account',
    deleteMismatch: 'That does not match your account.',
    deleteInUse: 'Your account is still in use, so it cannot be deleted yet.',
    deleteNeedsVerification: 'Verify it is you, then try again.',
    deleteFailed: 'The account could not be deleted.',
  },
  /* TSecuritySettings - two-factor authentication and active sessions. */
  securitySettings: {
    twoFactorTitle: 'Two-factor authentication',
    authenticator: 'Authenticator app',
    authenticatorHint: 'A six-digit code from an app such as 1Password or Google Authenticator.',
    on: 'On',
    off: 'Off',
    scan: 'Scan this',
    qrLabel: 'QR code for your authenticator app',
    cantScan: "Can't scan? Enter this key in your app:",
    codePlaceholder: '6-digit code',
    setUp: 'Set up',
    turnOn: 'Turn on',
    paused: 'Currently paused',
    pausedHint: 'Your authenticator is still enrolled. Resume to require codes again.',
    resume: 'Resume',
    pause: 'Pause',
    remove: 'Remove authenticator',
    sessionsTitle: 'Active sessions',
    unknownDevice: 'Unknown device',
    thisDevice: 'This device',
    revoke: 'Revoke',
    revokeConfirm: 'Sign out this device?',
    noSessions: 'No sessions recorded.',
    signOutOthers: 'Sign out other devices',
    signOutOthersConfirm: 'Sign out every other device? You stay signed in here.',
    turnOff: 'Turn off',
    emailCode: 'Email verification code',
    emailCodeHint: 'Get a code by email when you sign in.',
    emailNeedsAddress: 'Confirm your email address first.',
    passkey: 'Passkey or security key',
    passkeyHint: 'Verify with your device, a fingerprint or a hardware key.',
    passkeyAdd: 'Add a passkey',
    passkeyAddAnother: 'Add another',
    unnamedPasskey: 'Passkey',
    passkeyBackedUp: 'Synced',
    removeKey: 'Remove',
    removeKeyConfirm: 'Remove this key?',
  },
  /* TPersonalizationSettings - what the assistant should know about the user. */
  personalizationSettings: {
    name: 'What should the assistant call you?',
    nameHint: 'Used when it addresses you directly.',
    namePlaceholder: 'Your preferred name',
    role: 'What do you do?',
    roleHint: 'Helps it pitch answers at the right level.',
    rolePlaceholder: 'e.g. Backend engineer',
    language: 'Preferred language',
    languageHint: 'What it should answer in unless you ask otherwise.',
    languagePlaceholder: 'e.g. English',
    content: 'Anything else it should know',
    contentHint: 'Applied to every new conversation.',
    contentPlaceholder: 'Preferences, context about your work, how you like answers structured…',
    reset: 'Reset',
    save: 'Save',
  },
  /* TUsageSettings - the signed-in user's usage limits, as shares of each window. */
  usageSettings: {
    title: 'Usage limits',
    session: 'Current session',
    weekly: 'Weekly limit',
    /** {duration} = window length, e.g. "5 hours". */
    sessionHint: 'Starts with your first message and lasts {duration}.',
    /** {duration} = window length, e.g. "7 days". */
    weeklyHint: 'Renews every {duration}.',
    /** {percent} = whole number. */
    used: '{percent}% used',
    noLimit: 'No limit',
    notStarted: 'Starts with your next message',
    /** {time} = when the figures were read. */
    lastUpdated: 'Last updated: {time}',
    refresh: 'Refresh',
    loading: 'Loading…',
    loadFailed: 'Usage could not be loaded.',
    noLimits: 'No usage limits apply to your account.',
  },
  /* Usage-limit wording shared by the settings page, the composer notice and a refused reply. */
  usageLimits: {
    /** BCP-47 tag for dates and times in this dictionary's language. */
    dateLocale: 'en-US',
    /** {time} = countdown, e.g. "2 hr 13 min". */
    resetsIn: 'Resets in {time}',
    /** {time} = day and time, e.g. "Thu 10:59 PM". */
    resetsAt: 'Resets {time}',
    hoursMinutes: '{hours} hr {minutes} min',
    hours: '{hours} hr',
    minutes: '{minutes} min',
    lessThanMinute: 'less than a minute',
    /** Window lengths: {count} = number. */
    durationMinutes: '{count} minutes',
    durationHour: '1 hour',
    durationHours: '{count} hours',
    durationDay: '1 day',
    durationDays: '{count} days',
    reachedSession: "You've reached your session limit.",
    reachedWeekly: "You've reached your weekly limit.",
    reached: "You've reached your usage limit.",
    /** {time} = countdown. */
    resetSentenceIn: 'It resets in {time}.',
    /** {time} = day and time. */
    resetSentenceAt: 'It resets {time}.',
    /** A limit of zero does not reset on its own. */
    noReset: 'Ask an administrator to raise it.',
    draftKept: 'Your message was not sent and is still in the box.',
    /** {percent} = whole number. */
    warningSession: "You've used {percent}% of your session limit.",
    /** {percent} = whole number. */
    warningWeekly: "You've used {percent}% of your weekly limit.",
    dismiss: 'Dismiss',
  },
  views: {
    agents: 'Agents',
    scheduled: 'Scheduled',
    artifacts: 'Artifacts',
    skills: 'Skills',
    knowledge: 'Knowledge',
    chat: 'Chat',
  },
  palette: {
    placeholder: 'Type a command or search...',
    noResults: 'No matching actions.',
    escHint: 'Esc',
  },
  reasoning: {
    thinking: 'Thinking...',
    collapsed: 'Show reasoning',
    expanded: 'Hide reasoning',
    duration: '{seconds}s',
    thoughtFor: 'Thought for {seconds}s',
  },
  cli: {
    working: 'Working...',
    waitingTools: 'Running tools...',
    noActivity: 'No activity yet',
    showMore: 'Show full output',
    showLess: 'Show less',
  },
  tasks: {
    /** A sub-agent with neither a description nor a type. */
    agent: 'Sub-agent',
    /** A background command with neither a description nor a command line. */
    command: 'Background command',
    running: 'Running',
    completed: 'Done',
    failed: 'Failed',
    stopped: 'Stopped',
    /** Tag on a task that runs in the background. */
    background: 'Background',
    /** A run's line: the phrases are the start of a line, like the other steps' (see `tools`). */
    agentRunning: 'Running an agent',
    agentOngoing: 'An agent running',
    /** {count} = how many. */
    agentsOngoing: '{count} agents running',
    agentDone: 'Ran an agent',
    agentsDone: 'Ran {count} agents',
    agentFailed: 'An agent failed',
    agentStopped: 'Stopped an agent',
    agentsStopped: 'Stopped {count} agents',
    commandRunning: 'Running a background command',
    commandOngoing: 'A background command running',
    commandsOngoing: '{count} background commands running',
    commandDone: 'Finished a background command',
    commandsDone: 'Finished {count} background commands',
    commandFailed: 'A background command failed',
    commandStopped: 'Stopped a background command',
    commandsStopped: 'Stopped {count} background commands',
    /** {count} = tool calls a sub-agent made (one). */
    oneTool: '1 tool call',
    /** {count} = tool calls a sub-agent made. */
    tools: '{count} tool calls',
    /** What a running sub-agent does when the CLI has not said yet. */
    working: 'Working...',
    /** A sub-agent's own panel. */
    prompt: 'Prompt',
    showMore: 'Show more',
    showLess: 'Show less',
    model: 'Model',
    result: 'Result',
    noSteps: 'No steps yet',
    nestedSteps: 'The CLI does not stream the steps of a sub-agent that another sub-agent started; its state and report are here.',
    back: 'Back',
    close: 'Close',
    /** {title} = the task's description. Opens a sub-agent's panel. */
    open: 'Open {title}',
    stop: 'Stop',
    /** {title} = the task's description. Accessible name of a stop button. */
    stopNamed: 'Stop {title}',
    stopping: 'Stopping...',
    /** The side panel's list. */
    listTitle: 'Background tasks',
    listEmpty: 'No background tasks',
    /** {count} = how many have finished. A row that opens and closes the finished ones. */
    finished: '{count} finished',
    /** Images a run's steps returned, at the end of its line. */
    images: 'Images from these steps',
    /** {index} / {count}: which image of the run's images. */
    imageCount: '{index} of {count}',
    /** {count} = lines the run's steps added / removed in files. */
    linesAdded: '{count} lines added',
    linesRemoved: '{count} lines removed',
  },
  feedback: {
    helpful: 'Helpful',
    notHelpful: 'Not helpful',
    report: 'Report',
    thankYou: 'Thanks for your feedback',
    reasonPlaceholder: 'What went wrong?',
    sendReason: 'Send feedback',
  },
  agent: {
    handoff: 'Handing off to {agent}...',
    toolCall: 'Using {tool}...',
    toolResult: 'Result from {tool}',
    running: 'Agent running...',
    completed: 'Agent completed',
    failed: 'Agent failed',
    cancelled: 'Agent cancelled',
  },
  skill: {
    search: 'Search skills...',
    noResults: 'No skills found',
    activate: 'Activate',
    deactivate: 'Deactivate',
    category: 'Category',
    builtIn: 'Built-in',
    custom: 'Custom',
  },
  workflow: {
    run: 'Run workflow',
    stop: 'Stop workflow',
    addNode: 'Add node',
    deleteNode: 'Delete node',
    connect: 'Connect',
    status: 'Status',
    running: 'Running',
    completed: 'Completed',
    failed: 'Failed',
    paused: 'Paused',
    zoomIn: 'Zoom in',
    zoomOut: 'Zoom out',
    fitView: 'Fit to view',
  },
  quota: {
    usage: 'Usage',
    limit: 'Limit',
    remaining: 'Remaining',
    exceeded: 'Quota exceeded',
    resetDate: 'Resets on {date}',
  },
  token: {
    input: 'Input tokens',
    output: 'Output tokens',
  },
  plan: {
    title: 'Plan',
    collapse: 'Toggle plan',
  },
  task: {
    queue: 'Task Queue',
    pending: 'Pending',
    completed: 'Completed',
    running: 'Running',
    attachment: 'Attachment',
  },
  model: {
    select: 'Select model',
    search: 'Search models...',
    noResults: 'No models found',
    current: 'Current model',
  },
  checkpoint: {
    label: 'Checkpoint',
    save: 'Save checkpoint',
  },
  artifact: {
    title: 'Artifact',
    preview: 'Preview',
    code: 'Code',
    download: 'Download',
    close: 'Close',
    console: 'Console',
    openExternal: 'Open in new tab',
    noPreview: 'Preview not available',
    address: 'Preview address',
  },
  knowledge: {
    panel: 'Knowledge Base',
    selectBase: 'Select knowledge base',
    noBase: 'No knowledge bases',
    citations: 'Sources',
    usedSources: 'Used {count} sources',
    documentCount: '{count} docs',
  },
  mcp: {
    tools: 'MCP Tools',
    connected: 'Connected',
    disconnected: 'Disconnected',
    error: 'Error',
    toolCount: '{count} tools',
  },
  openIn: {
    label: 'Open in...',
    chatgpt: 'ChatGPT',
    claude: 'Claude',
    cursor: 'Cursor',
  },
  embed: {
    title: 'AI Chat',
    open: 'Open chat',
    close: 'Close chat',
    minimize: 'Minimize',
    expand: 'Expand',
  },
  toolbar: {
    search: 'Search',
    more: 'More actions',
    clearSearch: 'Clear search',
    /** Shown beside a folded action that has something applied (a filter). */
    active: 'On',
    /** {label} = the primary action, e.g. "New task". */
    primaryMenu: '{label}: more options',
  },
  approvals: {
    /** Accessible name of the cards above the composer. */
    region: 'Waiting for you',
    /** {time} = m:ss left. */
    expires: '{time} left; no answer by then counts as no',
    expired: 'Timed out, counted as no',
    /** {message} = why it failed. */
    failed: "Couldn't send your answer: {message}",
    closed: 'This card is no longer waiting for an answer',
    /** {action} = one of the action* entries. */
    permissionTitle: 'The agent wants to {action}',
    allowOnce: 'Allow once',
    allowAlways: 'Always allow',
    reject: 'Deny',
    reason: 'Why',
    path: 'Path',
    file: 'File',
    url: 'Address',
    actionBash: 'run a command',
    actionWrite: 'write a file',
    actionEdit: 'edit a file',
    actionNotebookEdit: 'edit a notebook',
    actionWebFetch: 'read a web page',
    actionWebSearch: 'search the web',
    /** {tool} = the tool's name. */
    actionOther: 'use {tool}',
    questionTitle: 'The agent has a question for you',
    /** {count} = how many questions. */
    questionTitleMany: 'The agent has {count} questions for you',
    submit: 'Send answers',
    decline: 'Skip',
    own: 'Other',
    ownPlaceholder: 'Write your answer',
    multiple: 'Pick any',
    planTitle: 'The agent has a plan for your approval',
    approve: 'Approve',
    approveAskEdits: 'Approve, ask before changes',
    approveAcceptEdits: 'Approve, accept file edits',
    revise: 'Revise',
    send: 'Send back',
    cancel: 'Cancel',
    feedback: 'What to change (optional)',
    feedbackPlaceholder: 'For example: leave the database alone, only change the page',
    planEmpty: 'The agent handed over no plan.',
    planDraft: 'This is the draft the agent wrote while thinking.',
  },
  queue: {
    /** Accessible name of the queued messages above the composer. */
    region: 'Queued messages',
    /** {count} = how many wait to go. */
    title: '{count} queued',
    /** Beside the title while the turn runs and cannot take messages: they go as the next turn. */
    hint: 'They go together when this turn ends',
    /** Beside the title while the turn runs and takes messages as it works. */
    hintToAgent: 'The agent reads them between its steps',
    sending: 'Sending…',
    next: 'Goes next',
    /** A queued message written to the running agent, not yet taken in. */
    handed: 'With the agent: it reads this after its current step',
    stopped: 'The turn was stopped, so this was not sent',
    /** {reason} = why its turn was refused. */
    failed: 'Not sent: {reason}',
    reasonModel: 'this model is not available to you',
    reasonAgent: 'this assistant is not available to you',
    reasonLimit: 'you have reached your usage limit',
    reasonInterrupted: 'sending was interrupted; it may not have arrived',
    reasonNotTaken: 'the agent did not take it in',
    edit: 'Edit',
    remove: 'Remove',
    sendNow: 'Send now',
    /** Tooltip of "Send now" while the turn runs and takes messages: nothing is stopped. */
    sendNowHint: 'Give this to the agent now; it reads it after its current step',
    /** The button while the turn runs and cannot take this message: sending it stops the turn. */
    stopAndSend: 'Stop and send',
    /** Tooltip of "Stop and send". */
    stopAndSendHint: 'This turn cannot take a message while it works: stop it and send this one right away',
    /** Accessible name of a queued message's attachments. */
    attachments: 'Attachments',
    send: 'Send',
    save: 'Save',
    cancel: 'Cancel',
    editLabel: 'Edit the queued message',
    /** The folded list on a phone while cards wait: shows the messages again. */
    show: 'Show',
    hide: 'Hide',
  },
  slash: {
    /** Heading and accessible name of the composer's `/` menu. */
    title: 'Skills and commands',
    loading: 'Reading…',
    loadFailed: "Couldn't read the commands here. Type / again in a moment.",
    none: 'No skill or command matches',
    keys: '↑↓ to choose · Enter to insert · Esc to close',
  },
  context: {
    /** The context meter's tooltip and accessible name: tokens used against the window. */
    used: 'Context {used} / {window}',
    /** The same when the window's size is not known. */
    usedOnly: 'Context {used}',
  },
  imageViewer: {
    /** Accessible name of the full-size image viewer when the picture has no name. */
    label: 'Image',
    previous: 'Previous image',
    next: 'Next image',
    /** {index} = the picture shown, {count} = how many there are. */
    counter: '{index} / {count}',
    failed: "This image couldn't be loaded",
    zoomIn: 'Zoom in',
    zoomOut: 'Zoom out',
    /** The button between the two zoom buttons: shows the zoom as {percent}%, a press fits the picture again. */
    fit: 'Fit to screen',
    /** {percent} = the zoom, 100 when the picture fits the screen. */
    zoomLevel: '{percent}%',
  },
  common: {
    /** Read for something going on that has no words of its own (a `TSegmented` option's `running`). */
    running: 'Working',
    loading: 'Loading...',
    error: 'Error',
    cancel: 'Cancel',
    confirm: 'Confirm',
    save: 'Save',
    delete: 'Delete',
    edit: 'Edit',
    close: 'Close',
    search: 'Search',
    noData: 'No data',
    retry: 'Retry',
    refresh: 'Refresh',
    all: 'All',
    user: 'User',
    actions: 'Actions',
    yes: 'Yes',
    no: 'No',
    minimize: 'Minimize',
    maximize: 'Maximize',
    restore: 'Restore',
    resizeColumn: 'Resize column {name}',
    moreInfo: 'More information',
    /** A searchable list (`TSearchSelect`) that found nothing for the words typed. */
    noMatches: 'No matches',
    /** Empties a field (`TSearchSelect`'s clear button). */
    clear: 'Clear',
    /** A list whose options could not be loaded. */
    loadFailed: 'Could not load',
  },
};

/**
 * String record type derived from en structure. An entry is a string, or a map of strings keyed by a value the
 * backend gives (`composer.effortNames`: a runtime's own effort values), looked up at run time.
 */
export type AiLocaleMessages = {
  [K in keyof typeof en]: {
    [P in keyof (typeof en)[K]]: (typeof en)[K][P] extends string ? string : Record<string, string>;
  };
};
