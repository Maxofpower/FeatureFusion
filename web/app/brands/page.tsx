import { fetcher } from "@/lib/fetcher"
import type { Brand } from "@/models"
import { BrandsPage } from "./_components/brands"
import { API_ROUTES } from "@/lib/constants/routes"

export default async function Brands() {
  const getBrands = await fetcher<Brand[]>(API_ROUTES.BRANDS, {
    cache: 'no-store'
  })

  if (!getBrands.success || getBrands.data === undefined) {
    return <div>Error: {getBrands.message}</div>
  }

  return <BrandsPage brands={getBrands.data} />
}
