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

export interface CustomerDetail {
    id: number;
    email: string;
    displayName: string;
    createdAt: string;
}

export interface CustomerOrderListItem {
    id: number;
    orderNumber: string;
    status: string;
    total: number;
    currency: string;
    createdAt: string;
    lineCount: number;
}

export interface CustomerOrderList {
    items: CustomerOrderListItem[];
    page: number;
    pageSize: number;
    totalCount: number;
}

export const CUSTOMER_ORDERS_DEFAULT_PAGE_SIZE = "10";

export const CUSTOMER_ORDERS_MAX_PAGE_SIZE = 50;

export const CUSTOMER_DEFAULT_LIMIT = "20";
export const CUSTOMER_MAX_LIMIT = 50;

export interface CustomerFilterValues {
    limit: string;
}

export interface CustomerQueryFilters extends CustomerFilterValues {
    cursor: string;
}
