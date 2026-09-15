export interface Catalog {
    items: Array<{
        id: number;
        name: string;
        slug: string;
        sku: string;
        price: number;
        stockQuantity: number;
        inStock: boolean;
        brandName: string;
        brandSlug: string;
        categoryName: string;
        categorySlug: string;
        primaryImageUrl: string;
        shortDescription: string;
    }>;
    page: number;
    pageSize: number;
    totalCount: number;
}