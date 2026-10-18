import { notFound } from "next/navigation"
import { fetcher } from "@/lib/fetcher"
import { API_ROUTES } from "@/lib/constants/routes"
import {
  CUSTOMER_ORDERS_DEFAULT_PAGE_SIZE,
  CUSTOMER_ORDERS_MAX_PAGE_SIZE,
  type CustomerDetail,
  type CustomerOrderList,
} from "@/models"
import { CustomerPage } from "./_components/customer"

interface Props {
  params: Promise<{ customerId: string }>
  searchParams: Promise<{
    page?: string
    pageSize?: string
  }>
}

export default async function Customer({ params, searchParams }: Props) {
  const [{ customerId }, query] = await Promise.all([params, searchParams])

  const rawPage = Number(query.page)
  const page =
    query.page && Number.isInteger(rawPage) && rawPage >= 1 ? String(rawPage) : "1"

  const rawPageSize = Number(query.pageSize)
  const pageSize =
    query.pageSize &&
    Number.isInteger(rawPageSize) &&
    rawPageSize >= 1 &&
    rawPageSize <= CUSTOMER_ORDERS_MAX_PAGE_SIZE
      ? query.pageSize
      : CUSTOMER_ORDERS_DEFAULT_PAGE_SIZE

  const [getCustomer, getCustomerOrders] = await Promise.all([
    fetcher<CustomerDetail>(`${API_ROUTES.CUSTOMERS}/${customerId}`, {
      cache: "no-store",
    }),
    fetcher<CustomerOrderList>(
      `${API_ROUTES.CUSTOMERS}/${customerId}/orders?page=${page}&pageSize=${pageSize}`,
      { cache: "no-store" }
    ),
  ])

  if (!getCustomer.success || getCustomer.data === undefined) {
    notFound()
  }

  const orders =
    getCustomerOrders.success && getCustomerOrders.data !== undefined
      ? getCustomerOrders.data
      : {
          items: [],
          page: Number(page),
          pageSize: Number(pageSize),
          totalCount: 0,
        }

  return (
    <CustomerPage
      customer={getCustomer.data}
      orders={orders}
      ordersError={getCustomerOrders.success ? null : getCustomerOrders.message}
    />
  )
}
