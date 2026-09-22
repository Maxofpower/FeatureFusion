import { fetcher } from "@/lib/fetcher"
import type { Catalog } from "@/models"
import { CatalogPage } from "./_components/catalog"
import { API_ROUTES } from "@/lib/constants/routes"

interface Props {
  searchParams: Promise<{
    page?: string
    pageSize?: string
  }>
}

export default async function Catalog({ searchParams }: Props) {
  const params = await searchParams

  const page = params.page ?? "1"
  const pageSize = params.pageSize ?? "24"

  const query = new URLSearchParams({
    page,
    pageSize,
  })

  const getCatalogs = await fetcher<Catalog>(`${API_ROUTES.CATALOG}?${query}`)

  if (!getCatalogs.success || getCatalogs.data === undefined) {
    return <div>Error: {getCatalogs.message}</div>
  }

  return (
    <CatalogPage
      catalogs={getCatalogs.data}
      currentFilters={{ page, pageSize }}
    />
  )
}