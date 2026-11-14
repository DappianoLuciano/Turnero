import js from '@eslint/js';
import prettier from 'eslint-config-prettier';
import reactHooks from 'eslint-plugin-react-hooks';
import reactRefresh from 'eslint-plugin-react-refresh';
import globals from 'globals';
import tseslint from 'typescript-eslint';

export default tseslint.config(
  { ignores: ['dist', 'dev-dist', 'coverage', 'src/shared/api/schema.d.ts'] },
  {
    files: ['**/*.{ts,tsx}'],
    extends: [js.configs.recommended, ...tseslint.configs.recommended, prettier],
    languageOptions: {
      ecmaVersion: 2023,
      globals: globals.browser,
    },
    plugins: {
      'react-hooks': reactHooks,
      'react-refresh': reactRefresh,
    },
    rules: {
      ...reactHooks.configs.recommended.rules,
      'react-refresh/only-export-components': ['warn', { allowConstantExport: true }],
      '@typescript-eslint/consistent-type-imports': 'error',
      '@typescript-eslint/no-unused-vars': ['error', { argsIgnorePattern: '^_' }],
      'no-restricted-syntax': [
        'error',
        {
          // UI texts live in src/shared/i18n. JSX string literals with letters are not allowed.
          selector: 'JSXText[value=/[A-Za-zÁÉÍÓÚáéíóúñÑ]{2,}/]',
          message: 'Usá los textos de src/shared/i18n (t.*) en vez de texto literal en JSX.',
        },
      ],
    },
  },
  {
    files: ['src/shared/ui/**/*.{ts,tsx}', 'src/test/**/*.{ts,tsx}', '**/*.test.{ts,tsx}'],
    rules: {
      'react-refresh/only-export-components': 'off',
      'no-restricted-syntax': 'off',
    },
  },
);
