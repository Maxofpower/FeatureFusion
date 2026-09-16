import { fetcher } from "@/lib/fetcher"
import type { Catalog } from "@/models"
import { CatalogPage } from "./_components/catalog"

interface Props {
  searchParams: Promise<{
    page?: string
    pageSize?: string
    sortBy?: string
    sortDirection?: string
  }>
}

export default async function Catalog({ searchParams }: Props) {
  const params = await searchParams

  const page = params.page ?? "1"
  const pageSize = params.pageSize ?? "24"
  const sortBy = params.sortBy ?? "Id"
  const sortDirection = params.sortDirection ?? "Ascending"

  const query = new URLSearchParams({
    page,
    pageSize,
    sortBy,
    sortDirection,
  })

  const getCatalogs = await fetcher<Catalog>(`/catalog/products?${query}`)

  if (!getCatalogs.success || getCatalogs.data === undefined) {
    return <div>Error: {getCatalogs.message}</div>
  }

  return (
    <CatalogPage
      catalogs={getCatalogs.data}
      currentFilters={{ page, pageSize, sortBy, sortDirection }}
    />
  )
}