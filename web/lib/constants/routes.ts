export const APP_ROUTES = {
  HOME: "/",
  CATALOG: "/catalog",
  BRANDS: "/brands",
  catalogProduct: (slug: string) => `/catalog/products/${slug}`,
  CATEGORIES: "/categories",
} as const;

export const API_ROUTES = {
  CATALOG: "/catalog/products",
  BRANDS: "/catalog/brands",
  CATEGORIES: "/catalog/categories",
} as const;