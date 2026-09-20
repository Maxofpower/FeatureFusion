import { fetcher } from "@/lib/fetcher"
import type { Brand } from "@/models"
import { BrandsPage } from "./_components/brands"

export default async function Brands() {
  const getBrands = await fetcher<Brand[]>(`/catalog/brands`, {
    cache: 'no-store'
  })

  if (!getBrands.success || getBrands.data === undefined) {
    return <div>Error: {getBrands.message}</div>
  }

  return <BrandsPage brands={getBrands.data} />
}
