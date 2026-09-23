import { fetcher } from "@/lib/fetcher"
import type { Brand } from "@/models"
import { CategoriesPage } from "./_components/categories"
import { API_ROUTES } from "@/lib/constants/routes"


export default async function Categories() {

  const getCategories = await fetcher<Brand[]>(API_ROUTES.CATEGORIES, {
    cache: 'no-store'
  })

  if (!getCategories.success || getCategories.data === undefined) {
    return <div>Error: {getCategories.message}</div>
  }

  return <CategoriesPage categories={getCategories.data} />
}
