import { fetcher } from "@/lib/fetcher"
import {
  CUSTOMER_DEFAULT_LIMIT,
  CUSTOMER_MAX_LIMIT,
  type CustomerPagedResult,
} from "@/models"
import { API_ROUTES } from "@/lib/constants/routes"
import { CustomersPage } from "./_components/customers"

interface Props {
  searchParams: Promise<{
    limit?: string
    cursor?: string
  }>
}

export default async function Customers({ searchParams }: Props) {
  const params = await searchParams

  const rawLimit = Number(params.limit)
  const limit =
    params.limit && Number.isInteger(rawLimit) && rawLimit >= 1 && rawLimit <= CUSTOMER_MAX_LIMIT
      ? params.limit
      : CUSTOMER_DEFAULT_LIMIT
  const cursor = params.cursor ?? ""

  const query = new URLSearchParams({ limit })
  if (cursor) query.set("cursor", cursor)

  const getCustomers = await fetcher<CustomerPagedResult>(`${API_ROUTES.CUSTOMERS}?${query}`, {
    cache: 'no-store'
  })

  if (!getCustomers.success || getCustomers.data === undefined) {
    return <div>Error: {getCustomers.message}</div>
  }

  return (
    <CustomersPage
      customers={getCustomers.data}
      currentFilters={{ limit, cursor }}
    />
  )
}
