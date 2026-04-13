/**
 * @tnzi/ui English locale
 *
 * Keys organized by feature area under the `tnzi.*` namespace.
 */

export const en: Locale = {
  tnzi: {
    auth: {
      login: {
        title: 'Sign in',
        username: 'Username',
        password: 'Password',
        remember: 'Remember me',
        forgot: 'Forgot password?',
        submit: 'Sign in',
        noAccount: "Don't have an account?",
        signUp: 'Sign up',
      },
      register: {
        title: 'Create account',
        username: 'Username',
        email: 'Email',
        password: 'Password',
        confirmPassword: 'Confirm password',
        submit: 'Create account',
        hasAccount: 'Already have an account?',
        signIn: 'Sign in',
      },
      passwordReset: {
        title: 'Reset password',
        email: 'Email',
        newPassword: 'New password',
        confirmPassword: 'Confirm password',
        sendCode: 'Send code',
        submit: 'Reset password',
        backToLogin: 'Back to sign in',
      },
    },
    layout: {
      header: {
        search: 'Search',
        notifications: 'Notifications',
        profile: 'Profile',
        settings: 'Settings',
        logout: 'Sign out',
      },
      sidebar: {
        collapse: 'Collapse',
        expand: 'Expand',
      },
      breadcrumb: {
        home: 'Home',
      },
    },
    feedback: {
      confirm: {
        title: 'Confirm',
        ok: 'OK',
        cancel: 'Cancel',
      },
      loading: 'Loading...',
      empty: 'No data',
      error: 'Something went wrong',
      retry: 'Retry',
    },
    control: {
      button: {
        submit: 'Submit',
        cancel: 'Cancel',
        save: 'Save',
        delete: 'Delete',
        edit: 'Edit',
        add: 'Add',
        refresh: 'Refresh',
        reset: 'Reset',
        search: 'Search',
      },
      pagination: {
        prev: 'Previous',
        next: 'Next',
        page: 'Page',
        of: 'of',
        total: 'Total',
      },
    },
  },
}

export interface Locale {
  tnzi: {
    auth: {
      login: {
        title: string
        username: string
        password: string
        remember: string
        forgot: string
        submit: string
        noAccount: string
        signUp: string
      }
      register: {
        title: string
        username: string
        email: string
        password: string
        confirmPassword: string
        submit: string
        hasAccount: string
        signIn: string
      }
      passwordReset: {
        title: string
        email: string
        newPassword: string
        confirmPassword: string
        sendCode: string
        submit: string
        backToLogin: string
      }
    }
    layout: {
      header: {
        search: string
        notifications: string
        profile: string
        settings: string
        logout: string
      }
      sidebar: {
        collapse: string
        expand: string
      }
      breadcrumb: {
        home: string
      }
    }
    feedback: {
      confirm: {
        title: string
        ok: string
        cancel: string
      }
      loading: string
      empty: string
      error: string
      retry: string
    }
    control: {
      button: {
        submit: string
        cancel: string
        save: string
        delete: string
        edit: string
        add: string
        refresh: string
        reset: string
        search: string
      }
      pagination: {
        prev: string
        next: string
        page: string
        of: string
        total: string
      }
    }
  }
}
