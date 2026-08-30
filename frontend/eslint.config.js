import js from '@eslint/js'
import tseslint from 'typescript-eslint'
import vue from 'eslint-plugin-vue'

/**
 * What the compiler cannot tell us.
 *
 * Typecheck and tests already cover correctness, so this is deliberately narrow:
 * the unused import, the forgotten variable, the accidental `any`, the template
 * that says v-for without a key. Style is not policed here - the project has a
 * voice and a formatter would flatten it - so nothing about quotes, semicolons or
 * line length is switched on.
 */
export default tseslint.config(
  { ignores: ['dist/**', 'coverage/**', 'dev-dist/**', 'node_modules/**', 'android/**', 'ios/**'] },

  js.configs.recommended,
  ...tseslint.configs.recommended,
  ...vue.configs['flat/recommended'],

  {
    languageOptions: {
      parserOptions: {
        parser: tseslint.parser,
        ecmaVersion: 'latest',
        sourceType: 'module',
      },
      globals: {
        // The browser surface these files actually use. Listed rather than pulled
        // from a globals package, so adding one is a decision.
        window: 'readonly',
        document: 'readonly',
        navigator: 'readonly',
        localStorage: 'readonly',
        location: 'readonly',
        fetch: 'readonly',
        console: 'readonly',
        setTimeout: 'readonly',
        clearTimeout: 'readonly',
        setInterval: 'readonly',
        clearInterval: 'readonly',
        requestAnimationFrame: 'readonly',
        cancelAnimationFrame: 'readonly',
        queueMicrotask: 'readonly',
        structuredClone: 'readonly',
        crypto: 'readonly',
        indexedDB: 'readonly',
        performance: 'readonly',
        AbortController: 'readonly',
        Blob: 'readonly',
        File: 'readonly',
        FileReader: 'readonly',
        FormData: 'readonly',
        Headers: 'readonly',
        Request: 'readonly',
        Response: 'readonly',
        URL: 'readonly',
        URLSearchParams: 'readonly',
        WebSocket: 'readonly',
        Worker: 'readonly',
        Notification: 'readonly',
        Event: 'readonly',
        EventTarget: 'readonly',
        KeyboardEvent: 'readonly',
        CustomEvent: 'readonly',
        MouseEvent: 'readonly',
        TouchEvent: 'readonly',
        ErrorEvent: 'readonly',
        PromiseRejectionEvent: 'readonly',
        Element: 'readonly',
        HTMLElement: 'readonly',
        HTMLInputElement: 'readonly',
        HTMLSelectElement: 'readonly',
        HTMLTextAreaElement: 'readonly',
        HTMLCanvasElement: 'readonly',
        Image: 'readonly',
        IntersectionObserver: 'readonly',
        ResizeObserver: 'readonly',
        MutationObserver: 'readonly',
        matchMedia: 'readonly',
        atob: 'readonly',
        btoa: 'readonly',
        TextDecoder: 'readonly',
        TextEncoder: 'readonly',
        Intl: 'readonly',
        self: 'readonly',
        globalThis: 'readonly',
        process: 'readonly',
      },
    },
    rules: {
      // An unused import or variable is either a leftover or a mistake, and both
      // are worth a word. A leading underscore says "on purpose".
      '@typescript-eslint/no-unused-vars': [
        'error',
        { argsIgnorePattern: '^_', varsIgnorePattern: '^_', caughtErrors: 'none' },
      ],
      // Vue's recommended set is opinionated about ordering and naming; the parts
      // that matter for correctness stay on, the arrangement rules do not.
      'vue/attributes-order': 'off',
      'vue/max-attributes-per-line': 'off',
      'vue/singleline-html-element-content-newline': 'off',
      'vue/html-self-closing': 'off',
      'vue/multi-word-component-names': 'off',
      // Where a line breaks is the author's business, and this project writes
      // short content inline on purpose.
      'vue/multiline-html-element-content-newline': 'off',
    },
  },

  {
    // Tests reach into things on purpose: a stubbed store, a payload shaped like
    // the server's, a global the environment does not type.
    files: ['tests/**/*.ts'],
    rules: {
      '@typescript-eslint/no-explicit-any': 'off',
      '@typescript-eslint/no-non-null-assertion': 'off',
    },
  },
)
