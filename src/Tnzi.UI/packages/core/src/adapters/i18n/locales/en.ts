/**
 * @tnzi/core/adapters/i18n/locales/en
 *
 * English translations.
 */

export default {
  // Common
  common: {
    home: 'Home',
    loading: 'Loading...',
    success: 'Success',
    error: 'Error',
    warning: 'Warning',
    info: 'Info',
    confirm: 'Confirm',
    cancel: 'Cancel',
    delete: 'Delete',
    edit: 'Edit',
    view: 'View',
    save: 'Save',
    search: 'Search',
    reset: 'Reset',
    submit: 'Submit',
    close: 'Close',
    back: 'Back',
    next: 'Next',
    prev: 'Previous',
    select: 'Select',
    selectAll: 'Select All',
    deselectAll: 'Deselect All',
    more: 'More',
    noData: 'No Data',
    required: 'This field is required',
    minLength: 'Must be at least {min} characters',
    maxLength: 'Must be at most {max} characters',
    invalid: 'Invalid value',
    invalidFormat: 'Invalid format',
  },

  // Auth
  auth: {
    login: 'Login',
    logout: 'Logout',
    register: 'Register',
    username: 'Username',
    password: 'Password',
    confirmPassword: 'Confirm Password',
    email: 'Email',
    phone: 'Phone',
    rememberMe: 'Remember Me',
    forgotPassword: 'Forgot Password?',
    noAccount: 'No account?',
    hasAccount: 'Already have an account?',
    loginSuccess: 'Login successful',
    registerSuccess: 'Registration successful',
    logoutSuccess: 'Logout successful',
    loginNow: 'Login now',
    passwordMismatch: 'Passwords do not match',
    enterUsername: 'Enter username',
    enterPassword: 'Enter password',
    enterEmail: 'Enter email',
    enterPhone: 'Enter phone number',
    verificationCode: 'Verification Code',
    enterVerificationCode: 'Enter verification code',
    sendCode: 'Send Code',
    newPassword: 'New Password',
    enterNewPassword: 'Enter new password',
    reenterPassword: 'Re-enter password',
    resetPassword: 'Reset Password',
    enterRegisteredEmail: 'Enter your registered email',
    captcha: 'Captcha',
    enterCaptcha: 'Enter captcha',
    orLoginWith: 'Or login with',
    orRegisterWith: 'Or register with',
    agreeTerms: 'I agree to the Terms of Service and Privacy Policy',
    passwordMinLength: 'Password must be at least {min} characters',
    pleaseEnter: 'Please enter {field}',
    pleaseConfirm: 'Please confirm {field}',
    pleaseAgreeTerms: 'You must agree to the terms',
  },

  // CRUD
  crud: {
    create: 'Create',
    batchDelete: 'Batch Delete',
    confirmDelete: 'Are you sure you want to delete this item?',
    confirmBatchDelete: 'Are you sure you want to delete the selected items?',
  },

  // Table
  table: {
    total: 'Total {count}',
    totalItems: 'Total {count} items',
    actions: 'Actions',
    noMore: 'No more',
    page: 'Page',
    size: 'per page',
    jumpTo: 'Go to',
    refresh: 'Refresh',
    export: 'Export',
    print: 'Print',
    columns: 'Columns',
  },

  // Form
  form: {
    required: 'This field is required',
    invalidEmail: 'Invalid email address',
    invalidPhone: 'Invalid phone number',
    minLength: 'Must be at least {min} characters',
    maxLength: 'Must be at most {max} characters',
    invalidFormat: 'Invalid format',
  },

  // Upload
  upload: {
    selectFile: 'Select File',
    dragDrop: 'Drag and drop file here',
    uploading: 'Uploading...',
    uploadSuccess: 'Upload successful',
    uploadFailed: 'Upload failed',
    fileSizeLimit: 'File size exceeds limit',
    fileTypeLimit: 'File type not supported',
  },

  // Date
  date: {
    today: 'Today',
    yesterday: 'Yesterday',
    tomorrow: 'Tomorrow',
    weekAgo: 'A week ago',
    monthAgo: 'A month ago',
    yearAgo: 'A year ago',
  },
} as const;

