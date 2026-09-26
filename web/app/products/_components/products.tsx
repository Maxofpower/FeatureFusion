'use client'

import { Suspense } from "react"
import { Card } from "@/components/ui/card"
import type { ProductPagedResult, ProductQueryFilters } from "@/models"
import { PackageSearch } from "lucide-react"
import { ProductsFilters } from "./products-filters"
import { ProductsPagination } from "./products-pagination"

interface Props {
    products: ProductPagedResult
    currentFilters: ProductQueryFilters
}

export const ProductsPage = (props: Props) => {
    return (
        <div className="bg-background">
            <main className=" px-4 py-8 sm:px-6 lg:px-8 flex flex-col gap-4">
                <div className="flex justify-between items-start gap-4">
                    <div>
                        <h1 className="text-2xl font-bold tracking-tight text-foreground">Product</h1>
                        <p className="mt-1 text-sm text-muted-foreground">
                            Discover our curated collection of products. Use Query Settings to refine by limit and sorting.
                        </p>
                    </div>
                    <Suspense>
                        <ProductsFilters currentFilters={props.currentFilters} />
                    </Suspense>
                </div>

                {
                    props.products.items.length > 0 ?
                        <div className="grid gap-x-5 gap-y-12 sm:grid-cols-2 lg:grid-cols-4">
                            {
                                props.products.items.map((product) => (
                                    <div
                                        key={product.id}
                                        className="group"
                                    >
                                        <article>
                                            <div className={`relative aspect-square overflow-hidden rounded-[1.25rem]`}>
                                                <img src="/image/apple.jpeg" alt={product.name} className="h-full w-full object-cover transition-transform duration-500 ease-out group-hover:scale-[1.04]" />
                                            </div>
                                            <div className="mt-4 flex items-start justify-between gap-3">
                                                <div>
                                                    <p className="text-xs uppercase tracking-[0.16em] text-muted-foreground">{product.createdAt}</p>
                                                    <h2 className="mt-1 text-lg font-medium tracking-[-0.02em]">{product.name}</h2>
                                                </div>
                                                <p className="text-sm font-medium">${product.price}</p>
                                            </div>
                                            <p className="mt-2 max-w-xs text-sm leading-6 text-muted-foreground">{product.description}</p>

                                        </article>
                                    </div>
                                ))

                            }
                        </div>

                        :
                        <EmptyState />
                }

                {props.products.items.length > 0 && (
                    <div className="mt-4 flex justify-center">
                        <ProductsPagination
                            products={props.products}
                            currentFilters={props.currentFilters}
                        />
                    </div>
                )}
            </main>


        </div>
    )
}

function EmptyState() {
    return (
        <Card className="flex flex-col items-center justify-center gap-3 p-12 text-center">
            <PackageSearch className="mb-1 size-8 text-tab" />
            <p className="text-sm font-medium text-foreground">Product is empty</p>
        </Card>
    )
}