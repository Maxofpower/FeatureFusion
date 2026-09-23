export const APP_ROUTES = {
  HOME: "/",
  CATALOG: "/catalog",
  BRANDS: "/brands",
  catalogProduct: (slug: string) => `/catalog/products/${slug}`
} as const;