export const APP_ROUTES = {
  HOME: "/",
  CATALOG: "/catalog",
  BRANDS: "/brands",
  catalogProduct: (slug: string) => `/catalog/products/${slug}`,
  CATEGORIES: "/categories",
  PRODUCTS: "/products",
  ORDERS: "/orders",
  CUSTOMERS: "/customers",
  order: (id: number | string) => `/orders/${id}`,
} as const;

export const API_ROUTES = {
  CATALOG: "/catalog/products",
  BRANDS: "/catalog/brands",
  CATEGORIES: "/catalog/categories",
  PRODUCTS: "/products-page",
  ORDERS: "/orders",
  CUSTOMERS: "/customers",
} as const;