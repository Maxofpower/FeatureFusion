import { notFound } from "next/navigation"
import { fetcher } from "@/lib/fetcher"
import type { ProductDetail } from "@/models"
import { ProductPage } from "./_components/product"
import { API_ROUTES } from "@/lib/constants/routes"

interface Props {
  params: Promise<{ slug: string }>
}

export default async function Product({ params }: Props) {
  const { slug } = await params

  const getProduct = await fetcher<ProductDetail>(`${API_ROUTES.CATALOG}/${encodeURIComponent(slug)}`)

  if (!getProduct.success || getProduct.data === undefined) {
    notFound()
  }

  return <ProductPage product={getProduct.data} />
}
