import { notFound } from "next/navigation"
import { fetcher } from "@/lib/fetcher"
import type { ProductDetail } from "@/models"
import { ProductPage } from "./_components/product"

interface Props {
  params: Promise<{ slug: string }>
}

export default async function Product({ params }: Props) {
  const { slug } = await params

  const getProduct = await fetcher<ProductDetail>(`/catalog/products/${encodeURIComponent(slug)}`)

  if (!getProduct.success || getProduct.data === undefined) {
    notFound()
  }

  return <ProductPage product={getProduct.data} />
}
