import { notFound } from "next/navigation"
import { fetcher } from "@/lib/fetcher"
import { API_ROUTES } from "@/lib/constants/routes"
import type { OrderDetail } from "@/models"
import { OrderPage } from "./_components/order"

interface Props {
  params: Promise<{ orderId: string }>
}

export default async function Order({ params }: Props) {
  const { orderId } = await params

  const getOrder = await fetcher<OrderDetail>(`${API_ROUTES.ORDERS}/${orderId}`)

  if (!getOrder.success || getOrder.data === undefined) {
    notFound()
  }

  return <OrderPage order={getOrder.data} />
}