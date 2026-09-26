import { fetcher } from "@/lib/fetcher"
import {
  PRODUCT_DEFAULT_LIMIT,
  PRODUCT_MAX_LIMIT,
  PRODUCT_PAGE_DIRECTIONS,
  PRODUCT_SORT_DIRECTIONS,
  PRODUCT_SORT_FIELDS,
  type ProductPagedResult,
} from "@/models"
import { ProductsPage } from "./_components/products"
import { API_ROUTES } from "@/lib/constants/routes"

interface Props {
  searchParams: Promise<{
    limit?: string
    sortBy?: string
    sortDirection?: string
    cursor?: string
    pageDirection?: string
  }>
}

const oneOf = (value: string | undefined, allowed: readonly string[], fallback: string) =>
  value && allowed.includes(value) ? value : fallback

export default async function Products({ searchParams }: Props) {
  const params = await searchParams

  const rawLimit = Number(params.limit)
  const limit =
    params.limit && Number.isInteger(rawLimit) && rawLimit >= 1 && rawLimit <= PRODUCT_MAX_LIMIT
      ? params.limit
      : PRODUCT_DEFAULT_LIMIT
  const sortBy = oneOf(params.sortBy, PRODUCT_SORT_FIELDS, "Id")
  const sortDirection = oneOf(params.sortDirection, PRODUCT_SORT_DIRECTIONS, "Ascending")
  const pageDirection = oneOf(params.pageDirection, PRODUCT_PAGE_DIRECTIONS, "Forward")
  const cursor = params.cursor ?? ""

  // Keyset query: limit / sortBy / sortDirection / cursor / pageDirection (see ProductPaginationEndpoints).
  const query = new URLSearchParams({ limit, sortBy, sortDirection, pageDirection })
  if (cursor) query.set("cursor", cursor)

  const getProduct = await fetcher<ProductPagedResult>(`${API_ROUTES.PRODUCTS}?${query}`, {
    cache: 'no-store'
  })

  if (!getProduct.success || getProduct.data === undefined) {
    return <div>Error: {getProduct.message}</div>
  }

  return (
    <ProductsPage
      products={getProduct.data}
      currentFilters={{ limit, sortBy, sortDirection, cursor, pageDirection }}
    />
  )
}
