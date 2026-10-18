'use client'

import Link from "next/link"
import { useRouter } from "next/navigation"
import { Card } from "@/components/ui/card"
import { Separator } from "@/components/ui/separator"
import {
    Table,
    TableBody,
    TableCell,
    TableHead,
    TableHeader,
    TableRow,
} from "@/components/ui/table"
import { APP_ROUTES } from "@/lib/constants/routes"
import type { CustomerDetail, CustomerOrderList } from "@/models"
import {  ClipboardList, Mail, CalendarDays, Hash, UserRound } from "lucide-react"
import { OrderStatusBadge } from "@/app/orders/_components/order-status-badge"
import { CustomerOrdersPagination } from "./customer-orders-pagination"
import { format } from "date-fns"

interface Props {
    customer: CustomerDetail
    orders: CustomerOrderList
    ordersError: string | null
}

export const CustomerPage = (props: Props) => {
    const { customer, orders, ordersError } = props
    const router = useRouter()

    const page = orders.page
    const pageSize = orders.pageSize
    const from = orders.totalCount === 0 ? 0 : (page - 1) * pageSize + 1
    const to = Math.min(page * pageSize, orders.totalCount)

    const initials = customer.displayName
        .split(" ")
        .filter(Boolean)
        .slice(0, 2)
        .map((part) => part[0]?.toUpperCase())
        .join("") || "?"

    const money = (value: number, currency: string) =>
        new Intl.NumberFormat("en-US", {
            style: "currency",
            currency,
        }).format(Number(value))

    return (
        <div className="bg-background">
            <main className="px-4 py-8 sm:px-6 lg:px-8 flex flex-col gap-6">
                <div className="flex flex-wrap items-center gap-4">
                    <div className="flex size-14 shrink-0 items-center justify-center rounded-full bg-primary/10 text-lg font-semibold text-primary">
                        {initials}
                    </div>
                    <div className="flex min-w-0 flex-col gap-1">
                        <div className="flex flex-wrap items-center gap-3">
                            <h1 className="text-2xl font-bold tracking-tight text-foreground">
                                {customer.displayName}
                            </h1>
                            <span className="rounded-full bg-muted px-2 py-0.5 font-mono text-xs text-muted-foreground">
                                #{customer.id}
                            </span>
                        </div>
                        <p className="text-sm text-muted-foreground">
                            Joined {format(customer.createdAt, "MMM d, yyyy 'at' HH:mm")}
                        </p>
                    </div>
                </div>

                <div className="grid gap-6 lg:grid-cols-3">
                    <section className="flex flex-col gap-4 lg:col-span-2">
                        <div>
                            <h2 className="text-lg font-medium tracking-[-0.02em] text-foreground">
                                Orders
                            </h2>
                            <p className="mt-1 text-sm text-muted-foreground">
                                {orders.totalCount === 0
                                    ? "This customer has not placed any orders yet."
                                    : orders.totalCount === 1
                                      ? "1 order placed by this customer."
                                      : `${orders.totalCount} orders placed by this customer · showing ${from}–${to}.`}
                            </p>
                        </div>

                        {ordersError ? (
                            <Card className="flex flex-col items-center justify-center gap-3 p-12 text-center">
                                <ClipboardList className="mb-1 size-8 text-muted-foreground/50" />
                                <p className="text-sm font-medium text-foreground">
                                    Orders could not be loaded
                                </p>
                                <p className="max-w-sm text-xs text-muted-foreground">
                                    {ordersError}
                                </p>
                            </Card>
                        ) : orders.items.length === 0 ? (
                            <Card className="flex flex-col items-center justify-center gap-3 p-12 text-center">
                                <ClipboardList className="mb-1 size-8 text-muted-foreground/50" />
                                <p className="text-sm font-medium text-foreground">
                                    {orders.totalCount > 0
                                        ? `No orders on page ${page}`
                                        : "No orders found"}
                                </p>
                                {orders.totalCount > 0 && (
                                    <p className="max-w-sm text-xs text-muted-foreground">
                                        This page is outside the available range. Jump back to a page below.
                                    </p>
                                )}
                            </Card>
                        ) : (
                            <Card className="py-0 overflow-x-auto">
                                <Table>
                                    <TableHeader>
                                        <TableRow>
                                            <TableHead>Order</TableHead>
                                            <TableHead>Status</TableHead>
                                            <TableHead className="text-right">Items</TableHead>
                                            <TableHead className="text-right">Total</TableHead>
                                            <TableHead className="text-right">Created At</TableHead>
                                        </TableRow>
                                    </TableHeader>
                                    <TableBody>
                                        {orders.items.map((order) => (
                                            <TableRow
                                                key={order.id}
                                                className="cursor-pointer transition-colors hover:bg-muted/50"
                                                onClick={() => router.push(APP_ROUTES.order(order.id))}
                                            >
                                                <TableCell className="font-medium">
                                                    <Link
                                                        href={APP_ROUTES.order(order.id)}
                                                        aria-label={`View order ${order.orderNumber}`}
                                                        className="underline-offset-4 hover:underline"
                                                        onClick={(event) => event.stopPropagation()}
                                                    >
                                                        {order.orderNumber}
                                                    </Link>
                                                </TableCell>
                                                <TableCell>
                                                    <OrderStatusBadge status={order.status} />
                                                </TableCell>
                                                <TableCell className="text-right">
                                                    {order.lineCount}
                                                </TableCell>
                                                <TableCell className="text-right font-medium">
                                                    {money(order.total, order.currency)}
                                                </TableCell>
                                                <TableCell className="text-right text-muted-foreground">
                                                    {format(order.createdAt, "MMM d, yyyy")}
                                                </TableCell>
                                            </TableRow>
                                        ))}
                                    </TableBody>
                                </Table>
                            </Card>
                        )}

                        {orders.totalCount > pageSize && !ordersError && (
                            <div className="mt-2 flex justify-center">
                                <CustomerOrdersPagination
                                    customerId={customer.id}
                                    page={page}
                                    pageSize={pageSize}
                                    totalCount={orders.totalCount}
                                />
                            </div>
                        )}
                    </section>

                    <div className="flex flex-col gap-6">
                        <Card className="px-2">
                            <h2 className="text-lg font-medium tracking-[-0.02em] text-foreground">
                                Profile
                            </h2>
                            <dl className="mt-3 flex flex-col gap-2 text-sm">
                                <ProfileRow
                                    icon={<UserRound className="size-4" />}
                                    label="Name"
                                    value={customer.displayName}
                                />
                                <ProfileRow
                                    icon={<Hash className="size-4" />}
                                    label="Customer ID"
                                    value={`#${customer.id}`}
                                />
                                <ProfileRow
                                    icon={<Mail className="size-4" />}
                                    label="Email"
                                    value={customer.email}
                                />
                                <ProfileRow
                                    icon={<CalendarDays className="size-4" />}
                                    label="Created"
                                    value={format(customer.createdAt, "MMM d, yyyy 'at' HH:mm")}
                                />
                            </dl>
                        </Card>

                        <Card className="px-2">
                            <h2 className="text-lg font-medium tracking-[-0.02em] text-foreground">
                                Summary
                            </h2>
                            <dl className="mt-3 flex flex-col gap-2 text-sm">
                                <div className="flex items-center justify-between gap-4">
                                    <dt className="text-muted-foreground">Total Orders</dt>
                                    <dd className="text-right font-medium text-foreground">
                                        {orders.totalCount}
                                    </dd>
                                </div>
                                <Separator className="mt-1" />
                                <div className="flex items-center justify-between gap-4">
                                    <dt className="text-muted-foreground">Member since</dt>
                                    <dd className="text-right font-medium text-foreground">
                                        {format(customer.createdAt, "yyyy")}
                                    </dd>
                                </div>
                            </dl>
                        </Card>
                    </div>
                </div>
            </main>
        </div>
    )
}

const ProfileRow = ({
    icon,
    label,
    value,
}: {
    icon: React.ReactNode
    label: string
    value: string
}) => (
    <div className="flex items-start justify-between gap-4">
        <dt className="flex items-center gap-2 text-muted-foreground">
            {icon}
            {label}
        </dt>
        <dd className="break-all text-right font-medium text-foreground">{value}</dd>
    </div>
)
