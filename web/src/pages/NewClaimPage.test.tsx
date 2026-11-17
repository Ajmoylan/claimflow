import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { Policy } from '../api/types';
import { mockFetch, renderWithProviders } from '../test/utils';
import { validateClaimForm } from './claimFormValidation';
import { NewClaimForm } from './NewClaimPage';

const motorPolicy: Policy = {
  policyNumber: 'DEMO-POL-1001',
  holderName: 'Fictional Customer A',
  coverageCategory: 'Motor',
  coverageLimit: 500_000,
  validFrom: '2025-01-01',
  validTo: '2027-12-31',
  isActive: true,
};

const valid = {
  policyNumber: 'DEMO-POL-1001',
  category: 'Motor' as const,
  incidentDate: '2026-11-01',
  claimedAmount: '12500.50',
  description: 'Bumper damaged in a car park',
};

afterEach(() => vi.unstubAllGlobals());

describe('validateClaimForm', () => {
  it('accepts a valid claim', () => {
    expect(validateClaimForm(valid, motorPolicy, '2026-11-15')).toEqual({});
  });

  it('mirrors the backend business rules', () => {
    const errors = validateClaimForm(
      {
        ...valid,
        category: 'Pet',
        incidentDate: '2026-11-20',
        claimedAmount: '600000',
        description: 'short',
      },
      motorPolicy,
      '2026-11-15',
    );
    expect(Object.keys(errors).sort()).toEqual(['category', 'claimedAmount', 'description', 'incidentDate']);
    expect(errors.incidentDate).toMatch(/future/);
  });

  it('rejects amounts with more than two decimals', () => {
    expect(
      validateClaimForm({ ...valid, claimedAmount: '10.005' }, motorPolicy, '2026-11-15').claimedAmount,
    ).toMatch(/two decimal/);
  });
});

describe('NewClaimForm', () => {
  it('pre-selects the category covered by the chosen policy', async () => {
    renderWithProviders(<NewClaimForm policies={[motorPolicy]} />);

    await userEvent.selectOptions(screen.getByLabelText('Policy'), 'DEMO-POL-1001');

    expect(screen.getByLabelText('Category')).toHaveValue('Motor');
  });

  it('shows client-side errors without calling the API', async () => {
    const fetchMock = mockFetch(201, {});
    renderWithProviders(<NewClaimForm policies={[motorPolicy]} />);

    await userEvent.click(screen.getByRole('button', { name: 'Submit claim' }));

    expect(screen.getByText('Choose a policy.')).toBeInTheDocument();
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('shows the API validation errors next to the matching fields', async () => {
    mockFetch(400, {
      title: 'One or more validation errors occurred.',
      status: 400,
      errors: { IncidentDate: ['Incident date must be within the policy period.'] },
    });
    renderWithProviders(<NewClaimForm policies={[motorPolicy]} />);

    await userEvent.selectOptions(screen.getByLabelText('Policy'), 'DEMO-POL-1001');
    await userEvent.type(screen.getByLabelText('Incident date'), '2026-01-10');
    await userEvent.type(screen.getByLabelText('Amount claimed (NOK)'), '1000');
    await userEvent.type(screen.getByLabelText('What happened?'), 'Something happened to the car');
    await userEvent.click(screen.getByRole('button', { name: 'Submit claim' }));

    expect(await screen.findByText('Incident date must be within the policy period.')).toBeInTheDocument();
    expect(screen.getByLabelText('Incident date')).toHaveAttribute('aria-invalid', 'true');
  });
});
