// TypeScript mirrors of the API's DTOs (src/ClaimFlow.Api/Contracts). Enums travel as strings.

export const CLAIM_STATUSES = ['Submitted', 'UnderReview', 'Approved', 'Rejected'] as const;
export type ClaimStatus = (typeof CLAIM_STATUSES)[number];

export const CLAIM_CATEGORIES = ['Motor', 'Home', 'Contents', 'Travel', 'Pet'] as const;
export type ClaimCategory = (typeof CLAIM_CATEGORIES)[number];

export const CLAIM_SORTS = ['Newest', 'Oldest', 'IncidentDate', 'AmountHighest'] as const;
export type ClaimSort = (typeof CLAIM_SORTS)[number];

export interface ClaimSummary {
  reference: string;
  policyNumber: string;
  category: ClaimCategory;
  incidentDate: string; // yyyy-MM-dd
  claimedAmount: number;
  status: ClaimStatus;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface StatusChange {
  fromStatus: ClaimStatus | null;
  toStatus: ClaimStatus;
  reason: string | null;
  changedAtUtc: string;
}

export interface ClaimDetail extends ClaimSummary {
  policyHolder: string;
  description: string;
  isDemoSeed: boolean;
  allowedNextStatuses: ClaimStatus[];
  history: StatusChange[];
}

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export interface Policy {
  policyNumber: string;
  holderName: string;
  coverageCategory: ClaimCategory;
  coverageLimit: number;
  validFrom: string;
  validTo: string;
  isActive: boolean;
}

export interface DashboardSummary {
  totalClaims: number;
  countByStatus: Record<ClaimStatus, number>;
  countByCategory: Record<ClaimCategory, number>;
  totalClaimedAmount: number;
  totalApprovedAmount: number;
  approvalRate: number | null;
  openClaims: number;
  recentClaims: ClaimSummary[];
  demo: {
    visitorClaims: number;
    maxVisitorClaims: number;
    lastResetAtUtc: string | null;
    nextResetAtUtc: string;
  };
}

export interface ClaimQuery {
  search?: string;
  status?: ClaimStatus;
  category?: ClaimCategory;
  policyNumber?: string;
  incidentFrom?: string;
  incidentTo?: string;
  sort?: ClaimSort;
  page?: number;
  pageSize?: number;
}

export interface CreateClaimRequest {
  policyNumber: string;
  category: ClaimCategory;
  incidentDate: string;
  claimedAmount: number;
  description: string;
}

export interface TransitionRequest {
  targetStatus: ClaimStatus;
  reason?: string;
}
