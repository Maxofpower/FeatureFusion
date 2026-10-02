'use client'

import Link from "next/link"
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
import { useIsMobile } from "@/hooks/use-mobile"
import type { OrderDetail } from "@/models"
import { PackageX } from "lucide-react"
import { format } from "date-fns"
import { OrderStatusBadge } from "../../_components/order-status-badge"

interface Props {
    order: OrderDetail
}

export const OrderPage = (props: Props) => {
    const { order } = props
    const isMobile = useIsMobile()

    const money = (value: number) =>
        new Intl.NumberFormat("en-US", {
            style: "currency",
            currency: order.currency,
        }).format(Number(value))

    return (
        <div className="bg-background">
            <main className="px-4 py-8 sm:px-6 lg:px-8 flex flex-col gap-6">

                <div className="flex flex-wrap items-start justify-between gap-4">
                    <div className="flex flex-col gap-2">
                        <div className="flex flex-wrap items-center gap-3">
                            <h1 className="text-2xl font-bold tracking-tight text-foreground">
                                {order.orderNumber}
                            </h1>
                            <OrderStatusBadge status={order.status} />
                        </div>
                        <p className="text-sm text-muted-foreground">
                            Placed {format(order.createdAt, "MMM d, yyyy 'at' HH:mm")} · Order
                            #{order.id}
                        </p>
                    </div>

                </div>

                <div className="grid gap-6 lg:grid-cols-3">
                    <section className="flex flex-col gap-4 lg:col-span-2">
                        <div>
                            <h2 className="text-lg font-medium tracking-[-0.02em] text-foreground">
                                Items
                            </h2>
                            <p className="mt-1 text-sm text-muted-foreground">
                                {order.lines.length} line{order.lines.length === 1 ? "" : "s"} on
                                this order.
                            </p>
                        </div>

                        {order.lines.length === 0 ? (
                            <Card className="flex flex-col items-center justify-center gap-3 p-12 text-center">
                                <PackageX className="mb-1 size-8 text-muted-foreground/50" />
                                <p className="text-sm font-medium text-foreground">
                                    This order has no line items
                                </p>
                            </Card>
                        ) : isMobile ? (
                            <div className="flex flex-col gap-3">
                                {order.lines.map((line) => (
                                    <Card key={line.productId} className="gap-1.5 px-4">
                                        <div className="flex items-start justify-between gap-3">
                                            <div className="flex min-w-0 flex-col gap-0.5">
                                                <span className="truncate text-sm font-medium text-foreground">
                                                    {line.productSlug ? (
                                                        <Link
                                                            href={APP_ROUTES.catalogProduct(
                                                                line.productSlug
                                                            )}
                                                            className="underline-offset-4 hover:underline"
                                                        >
                                                            {line.productName ?? `Product #${line.productId}`}
                                                        </Link>
                                                    ) : (
                                                        line.productName ?? `Product #${line.productId}`
                                                    )}
                                                </span>
                                                {line.productSku && (
                                                    <span className="font-mono text-xs text-muted-foreground">
                                                        {line.productSku}
                                                    </span>
                                                )}
                                            </div>
                                            <span className="shrink-0 text-sm font-semibold text-foreground">
                                                {money(line.lineTotal)}
                                            </span>
                                        </div>
                                        <p className="text-xs text-muted-foreground">
                                            Qty {line.quantity} × {money(line.unitPrice)}
                                        </p>
                                    </Card>
                                ))}
                            </div>
                        ) : (
                            <Card className="py-0 overflow-x-auto">
                                <Table>
                                    <TableHeader>
                                        <TableRow>
                                            <TableHead>Product</TableHead>
                                            <TableHead>SKU</TableHead>
                                            <TableHead className="text-right">Qty</TableHead>
                                            <TableHead className="text-right">Unit price</TableHead>
                                            <TableHead className="text-right">Line total</TableHead>
                                        </TableRow>
                                    </TableHeader>
                                    <TableBody>
                                        {order.lines.map((line) => (
                                            <TableRow key={line.productId}>
                                                <TableCell className="font-medium">
                                                    {line.productSlug ? (
                                                        <Link
                                                            href={APP_ROUTES.catalogProduct(
                                                                line.productSlug
                                                            )}
                                                            className="underline-offset-4 hover:underline"
                                                        >
                                                            {line.productName ?? `Product #${line.productId}`}
                                                        </Link>
                                                    ) : (
                                                        line.productName ?? `Product #${line.productId}`
                                                    )}
                                                </TableCell>
                                                <TableCell className="text-muted-foreground">
                                                    {line.productSku ? (
                                                        <span className="font-mono">
                                                            {line.productSku}
                                                        </span>
                                                    ) : (
                                                        "—"
                                                    )}
                                                </TableCell>
                                                <TableCell className="text-right">
                                                    {line.quantity}
                                                </TableCell>
                                                <TableCell className="text-right">
                                                    {money(line.unitPrice)}
                                                </TableCell>
                                                <TableCell className="text-right font-medium">
                                                    {money(line.lineTotal)}
                                                </TableCell>
                                            </TableRow>
                                        ))}
                                    </TableBody>
                                </Table>
                            </Card>
                        )}
                    </section>

                    <div className="flex flex-col gap-6">
                        <Card className="px-2">
                            <h2 className="text-lg font-medium tracking-[-0.02em] text-foreground">
                                Customer
                            </h2>
                            <dl className="mt-3 flex flex-col gap-2 text-sm">
                                <div className="flex items-center justify-between gap-4">
                                    <dt className="text-muted-foreground">Name</dt>
                                    <dd className="text-right font-medium text-foreground">
                                        {order.customerDisplayName ?? "—"}
                                    </dd>
                                </div>
                                <div className="flex items-center justify-between gap-4">
                                    <dt className="text-muted-foreground">Email</dt>
                                    <dd className="text-right font-medium text-foreground">
                                        {order.customerEmail ?? "—"}
                                    </dd>
                                </div>
                                <div className="flex items-center justify-between gap-4">
                                    <dt className="text-muted-foreground">Customer ID</dt>
                                    <dd className="text-right font-medium text-foreground">
                                        #{order.customerId}
                                    </dd>
                                </div>
                            </dl>
                        </Card>

                        <Card className="px-2">
                            <h2 className="text-lg font-medium tracking-[-0.02em] text-foreground">
                                Summary
                            </h2>
                            <dl className="mt-3 flex flex-col gap-2 text-sm">
                                <div className="flex items-center justify-between gap-4">
                                    <dt className="text-muted-foreground">Subtotal</dt>
                                    <dd className="text-right font-medium text-foreground">
                                        {money(order.subtotal)}
                                    </dd>
                                </div>
                                <div className="flex items-center justify-between gap-4">
                                    <dt className="text-muted-foreground">Shipping</dt>
                                    <dd className="text-right font-medium text-foreground">
                                        {money(order.shippingAmount)}
                                    </dd>
                                </div>
                                <div className="flex items-center justify-between gap-4">
                                    <dt className="text-muted-foreground">Tax</dt>
                                    <dd className="text-right font-medium text-foreground">
                                        {money(order.taxAmount)}
                                    </dd>
                                </div>
                                <Separator className="mt-1" />
                                <div className="flex items-center justify-between gap-4">
                                    <dt className="font-medium text-foreground">Total</dt>
                                    <dd className="text-right text-lg font-semibold text-foreground">
                                        {money(order.total)}
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