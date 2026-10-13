'use client'

import { Suspense } from "react"
import { Card } from "@/components/ui/card"
import {
    Table,
    TableBody,
    TableCell,
    TableHead,
    TableHeader,
    TableRow,
} from "@/components/ui/table"
import type { Customer, CustomerPagedResult, CustomerQueryFilters } from "@/models"
import { Users } from "lucide-react"
import { CustomersFilters } from "./customers-filters"
import { CustomersPagination } from "./customers-pagination"
import { format } from "date-fns"

interface Props {
    customers: CustomerPagedResult
    currentFilters: CustomerQueryFilters
}

export const CustomersPage = (props: Props) => {
    return (
        <div className="bg-background">
            <main className=" px-4 py-8 sm:px-6 lg:px-8 flex flex-col gap-4">
                <div className="flex justify-between items-start gap-4">
                    <div>
                        <h1 className="text-2xl font-bold tracking-tight text-foreground">Customers</h1>
                        <p className="mt-1 text-sm text-muted-foreground">
                            Browse Commerce customers with keyset (cursor) pagination.
                        </p>
                    </div>
                    <Suspense>
                        <CustomersFilters currentFilters={props.currentFilters} />
                    </Suspense>
                </div>

                {
                    props.customers.items.length > 0 ?
                        <Card className="py-0">
                            <Table>
                                <TableHeader>
                                    <TableRow>
                                        <TableHead >Id</TableHead>
                                        <TableHead>Name</TableHead>
                                        <TableHead>Email</TableHead>
                                        <TableHead className="text-right">Created At</TableHead>
                                    </TableRow>
                                </TableHeader>
                                <TableBody>
                                    {props.customers.items.map((customer: Customer) => (
                                        <TableRow key={customer.id}>
                                            <TableCell className="text-muted-foreground">#{customer.id}</TableCell>
                                            <TableCell className="font-medium">{customer.displayName}</TableCell>
                                            <TableCell>{customer.email}</TableCell>
                                            <TableCell className="text-right text-muted-foreground">
                                                {format(customer.createdAt, "MMM d, yyyy")}
                                            </TableCell>
                                        </TableRow>
                                    ))}
                                </TableBody>
                            </Table>
                        </Card>

                        :
                        <EmptyState />
                }

                {props.customers.items.length > 0 && (
                    <div className="mt-4 flex justify-center">
                        <Suspense>
                            <CustomersPagination
                                customers={props.customers}
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
            <Users className="mb-1 size-8 text-tab" />
            <p className="text-sm font-medium text-foreground">No customers found</p>
        </Card>
    )
}
