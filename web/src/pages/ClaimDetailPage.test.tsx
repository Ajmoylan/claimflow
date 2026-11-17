import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { ClaimDetail } from '../api/types';
import { mockFetch, renderWithProviders } from '../test/utils';
import { WorkflowActions } from './ClaimDetailPage';

const underReview: ClaimDetail = {
  reference: 'CLM-2026-D00007',
  policyNumber: 'DEMO-POL-1007',
  policyHolder: 'Fictional Customer G',
  category: 'Home',
  incidentDate: '2026-09-25',
  claimedAmount: 310_000,
  description: 'Roof damaged by a fallen tree.',
  status: 'UnderReview',
  isDemoSeed: true,
  createdAtUtc: '2026-09-26T08:15:00Z',
  updatedAtUtc: '2026-09-27T08:15:00Z',
  allowedNextStatuses: ['Approved', 'Rejected'],
  history: [],
};

afterEach(() => vi.unstubAllGlobals());

describe('WorkflowActions', () => {
  it('offers only the transitions the API allows', () => {
    renderWithProviders(<WorkflowActions claim={underReview} />);

    expect(screen.getByRole('button', { name: 'Approve' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Reject' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Start review' })).not.toBeInTheDocument();
  });

  it('requires a reason before rejecting, without calling the API', async () => {
    const fetchMock = mockFetch(200, underReview);
    renderWithProviders(<WorkflowActions claim={underReview} />);

    await userEvent.click(screen.getByRole('button', { name: 'Reject' }));

    expect(screen.getByText('Please give a reason for the rejection.')).toBeInTheDocument();
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('sends the transition with the reason', async () => {
    const fetchMock = mockFetch(200, { ...underReview, status: 'Rejected', allowedNextStatuses: [] });
    renderWithProviders(<WorkflowActions claim={underReview} />);

    await userEvent.type(screen.getByLabelText(/Reason/), 'Storm damage excluded');
    await userEvent.click(screen.getByRole('button', { name: 'Reject' }));

    expect(fetchMock).toHaveBeenCalledWith(
      '/api/claims/CLM-2026-D00007/transitions',
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({ targetStatus: 'Rejected', reason: 'Storm damage excluded' }),
      }),
    );
  });

  it('explains that final claims cannot change', () => {
    renderWithProviders(
      <WorkflowActions claim={{ ...underReview, status: 'Approved', allowedNextStatuses: [] }} />,
    );

    expect(screen.getByText(/No further changes are possible/)).toBeInTheDocument();
    expect(screen.queryByRole('button')).not.toBeInTheDocument();
  });
});
