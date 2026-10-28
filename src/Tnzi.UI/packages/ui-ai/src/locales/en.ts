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
    regenerate: 'Regenerate',
    like: 'Like',
    dislike: 'Dislike',
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
    lastActivity: 'Last activity',
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
    sortActivity: 'Last activity',
    today: 'Today',
    yesterday: 'Yesterday',
    previous7: 'Previous 7 days',
    previous30: 'Previous 30 days',
    rename: 'Rename',
    archive: 'Archive',
    unarchive: 'Unarchive',
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
    readPageFailed: "Couldn't read a page",
    /** An inline widget (show_widget): its title follows. {count} = widgets made in one run. */
    widgetRunning: 'Creating a widget',
    widgetDone: 'Created a widget',
    widgetFailed: "Couldn't create a widget",
    widgetsDone: 'Created {count} widgets',
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
    attach: 'Attach files',
    removeAttachment: 'Remove attachment',
    voiceInput: 'Voice input',
    stopRecording: 'Stop recording',
    send: 'Send',
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
    close: 'Close settings',
    /** Accessible name of the settings dialog's section list. */
    sections: 'Settings sections',
    /** The back button of a settings section on a narrow screen. */
    backToSections: 'Back to all settings',
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
    passwordMismatch: 'The two new passwords do not match.',
    passwordUpdated: 'Password updated.',
    updatePassword: 'Update password',
  },
  /* TSecuritySettings - two-factor authentication and active sessions. */
  securitySettings: {
    twoFactorTitle: 'Two-factor authentication',
    authenticator: 'Authenticator app',
    authenticatorHint: 'A six-digit code from an app such as 1Password or Google Authenticator.',
    on: 'On',
    off: 'Off',
    scan: 'Scan this',
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
  feedback: {
    helpful: 'Helpful',
    notHelpful: 'Not helpful',
    report: 'Report',
    thankYou: 'Thanks for your feedback',
    reasonPlaceholder: 'What went wrong?',
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
    cached: 'Cached',
    cacheWrite: 'Cache write',
    total: 'Total tokens',
    cost: 'Cost',
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
  },
  knowledge: {
    panel: 'Knowledge Base',
    selectBase: 'Select knowledge base',
    noBase: 'No knowledge bases',
    citations: 'Sources',
    usedSources: 'Used {count} sources',
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
  common: {
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
  },
};

/** Recursive string record type derived from en structure */
export type AiLocaleMessages = {
  [K in keyof typeof en]: {
    [P in keyof (typeof en)[K]]: string;
  };
};
