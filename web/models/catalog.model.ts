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

export interface ProductImage {
    url: string;
    altText: string;
    isPrimary: boolean;
    displayOrder: number;
}

export interface ProductSpecification {
    name: string;
    value: string;
    displayOrder: number;
}

export interface RelatedProduct {
    id: number;
    name: string;
    slug: string;
    price: number;
    primaryImageUrl: string | null;
}

export interface ProductDetail {
    id: number;
    name: string;
    slug: string;
    sku: string;
    price: number;
    stockQuantity: number;
    inStock: boolean;
    shortDescription: string | null;
    fullDescription: string | null;
    brandName: string;
    brandSlug: string;
    categoryName: string;
    categorySlug: string;
    images: ProductImage[];
    specifications: ProductSpecification[];
    related: RelatedProduct[];
}