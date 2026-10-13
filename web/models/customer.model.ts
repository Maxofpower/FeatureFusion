export interface Customer {
    id: number;
    email: string;
    displayName: string;
    createdAt: string;
}

export interface CustomerPagedResult {
    items: Customer[];
    nextCursor: string;
    previousCursor: string;
    hasMore: boolean;
    hasPrevious: boolean;
    totalCount: number;
}

export const CUSTOMER_DEFAULT_LIMIT = "20";
export const CUSTOMER_MAX_LIMIT = 50;

export interface CustomerFilterValues {
    limit: string;
}

export interface CustomerQueryFilters extends CustomerFilterValues {
    cursor: string;
}
