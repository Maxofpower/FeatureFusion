/** Item of GET /api/v1/products-page (lab ProductDto). */
export interface Product {
    id: number;
    name: string;
    price: number;
    description: string | null;
    createdAt: string;
}

/** Response of GET /api/v1/products-page (lab PagedResult<ProductDto>). */
export interface ProductPagedResult {
    items: Product[];
    nextCursor: string;
    previousCursor: string;
    hasMore: boolean;
    hasPrevious: boolean;
    totalCount: number;
}

export const PRODUCT_SORT_FIELDS = ["Id", "Name", "Price", "CreatedAt", "NameThenPrice"] as const;
export const PRODUCT_SORT_DIRECTIONS = ["Ascending", "Descending"] as const;
export const PRODUCT_PAGE_DIRECTIONS = ["Forward", "Backward"] as const;

export const PRODUCT_DEFAULT_LIMIT = "20";
export const PRODUCT_MAX_LIMIT = 100;

/** Editable query settings (the drawer form state). */
export interface ProductFilterValues {
    limit: string;
    sortBy: string;
    sortDirection: string;
}

/** Full URL query state of /products, including cursor navigation. */
export interface ProductQueryFilters extends ProductFilterValues {
    cursor: string;
    pageDirection: string;
}