'use client'

import { Suspense } from "react"
import Link from "next/link"
import { useRouter } from "next/navigation"
import { Card } from "@/components/ui/card"
import {
    Table,
    TableBody,
    TableCell,
    TableHead,
    TableHeader,
    TableRow,
} from "@/components/ui/table"
import type { Order, OrderPagedResult, OrderQueryFilters } from "@/models"
import { ReceiptText } from "lucide-react"
import { APP_ROUTES } from "@/lib/constants/routes"
import { OrdersFilters } from "./orders-filters"
import { OrdersPagination } from "./orders-pagination"
import { OrderStatusBadge } from "./order-status-badge"
import { format } from "date-fns"

interface Props {
    orders: OrderPagedResult
    currentFilters: OrderQueryFilters
}

export const OrdersPage = (props: Props) => {
    const router = useRouter()
    return (
        <div className="bg-background">
            <main className=" px-4 py-8 sm:px-6 lg:px-8 flex flex-col gap-4">
                <div className="flex justify-between items-start gap-4">
                    <div>
                        <h1 className="text-2xl font-bold tracking-tight text-foreground">Orders</h1>
                        <p className="mt-1 text-sm text-muted-foreground">
                            Demo Commerce orders, newest first. Keyset (cursor) paging .
                        </p>
                    </div>
                    <Suspense>
                        <OrdersFilters currentFilters={props.currentFilters} />
                    </Suspense>
                </div>

                {props.orders.totalCount > 0 && (
                    <p className="text-sm text-muted-foreground">
                        {props.orders.totalCount} orders
                    </p>
                )}

                {
                    props.orders.items.length > 0 ?
                        <Card className="py-0">
                            <Table>
                                <TableHeader>
                                    <TableRow>
                                        <TableHead>Order</TableHead>
                                        <TableHead>Customer</TableHead>
                                        <TableHead>Status</TableHead>
                                        <TableHead className="text-right">Items</TableHead>
                                        <TableHead className="text-right">Total</TableHead>
                                        <TableHead className="text-right">Created At</TableHead>
                                    </TableRow>
                                </TableHeader>
                                <TableBody>
                                    {props.orders.items.map((order: Order) => (
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
                                            <TableCell>#{order.customerId}</TableCell>
                                            <TableCell><OrderStatusBadge status={order.status} /></TableCell>
                                            <TableCell className="text-right">{order.lineCount}</TableCell>
                                            <TableCell className="text-right">
                                                {new Intl.NumberFormat("en-US", {
                                                    style: "currency",
                                                    currency: order.currency,
                                                }).format(Number(order.total))}
                                            </TableCell>
                                            <TableCell className="text-right text-muted-foreground">
                                                {format(order.createdAt, "MMM d, yyyy")}
                                            </TableCell>
                                        </TableRow>
                                    ))}
                                </TableBody>
                            </Table>
                        </Card>

                        :
                        <EmptyState />
                }

                {props.orders.items.length > 0 && (
                    <div className="mt-4 flex justify-center">
                        <Suspense>
                            <OrdersPagination
                                orders={props.orders}
                                currentFilters={props.currentFilters}
                            />
                        </Suspense>
                    </div>
                )}
            </main>
        </div>
    )
}

function EmptyState() {
    return (
        <Card className="flex flex-col items-center justify-center gap-3 p-12 text-center">
            <ReceiptText className="mb-1 size-8 text-tab" />
            <p className="text-sm font-medium text-foreground">No orders found</p>
        </Card>
    )
}