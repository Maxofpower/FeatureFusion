'use client'

import { Suspense } from "react"
import { Card } from "@/components/ui/card"
import { Catalog } from "@/models"
import { BookOpen } from "lucide-react"
import { CatalogFilters } from "./catalog-filters"

interface Filters {
    page: string
    pageSize: string
    sortBy: string
    sortDirection: string
}

interface Props {
    catalogs: Catalog
    currentFilters: Filters
}

export const CatalogPage = (props: Props) => {
    return (
        <div className="bg-background">
            <main className=" px-4 py-8 sm:px-6 lg:px-8 flex flex-col gap-4">
                <div className="flex justify-between items-start gap-4">
                    <div>
                        <h1 className="text-2xl font-bold tracking-tight text-foreground">Catalog</h1>
                        <p className="mt-1 text-sm text-muted-foreground">
                            Discover our curated collection of products. Use filters to refine by page, sort order, and direction.
                        </p>
                    </div>
                    <Suspense>
                        <CatalogFilters currentFilters={props.currentFilters} />
                    </Suspense>
                </div>
                <div className="grid gap-x-5 gap-y-12 sm:grid-cols-2 lg:grid-cols-4">
                    {props.catalogs.items.map((product) => (
                        <article key={product.id} className="group">
                            <div className={`relative aspect-square overflow-hidden rounded-[1.25rem]`}>
                                <img src={product.primaryImageUrl} alt={product.name} className="h-full w-full object-cover transition-transform duration-500 ease-out group-hover:scale-[1.04]" />
                                <button className="absolute bottom-4 right-4 flex h-11 w-11 items-center justify-center rounded-full bg-foreground text-xl text-background opacity-0 shadow-lg transition-all group-hover:opacity-100 hover:scale-105 focus:opacity-100 focus:outline-none focus:ring-2 focus:ring-primary" aria-label={`Add ${product.name} to bag`}>+</button>
                            </div>
                            <div className="mt-4 flex items-start justify-between gap-3"><div><p className="text-xs uppercase tracking-[0.16em] text-muted-foreground">{product.categoryName}</p>
                                <h2 className="mt-1 text-lg font-medium tracking-[-0.02em]">{product.name}</h2>
                            </div>
                                <p className="text-sm font-medium">{product.price}</p>
                            </div>
                            <p className="mt-2 max-w-xs text-sm leading-6 text-muted-foreground">{product.shortDescription}</p>

                        </article>
                    )
                    )}
                </div>
            </main>


        </div>
    )
}

function EmptyState() {
    return (
        <Card className="flex flex-col items-center justify-center gap-3 p-12 text-center">
            <BookOpen className="mb-1 size-8 text-tab" />
            <p className="text-sm font-medium text-foreground">Catalog is empty</p>
        </Card>
    )
}