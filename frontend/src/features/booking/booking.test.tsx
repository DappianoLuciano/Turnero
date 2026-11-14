import { screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { t } from '@/shared/i18n';
import { renderApp } from '@/test/render';

describe('public booking zone', () => {
  it('renders the placeholder for the business slug without requiring login', async () => {
    renderApp('/r/barberia-demo');

    expect(await screen.findByRole('heading', { name: t.booking.title })).toBeInTheDocument();
    expect(screen.getByText(t.booking.placeholder('barberia-demo'))).toBeInTheDocument();
  });
});
