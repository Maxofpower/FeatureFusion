'use client'

import { Card } from "@/components/ui/card"
import type { Categories } from "@/models"
import { CirclePile, Layers } from "lucide-react"

interface Props {
    categories: Categories[]
}

export const CategoriesPage = (props: Props) => {
    return (
        <div className="bg-background">
            <main className="px-4 py-8 sm:px-6 lg:px-8 flex flex-col gap-4">
                <div>
                    <h1 className="text-2xl font-bold tracking-tight text-foreground">Categories</h1>
                    <p className="mt-1 text-sm text-muted-foreground">
                        Browse all available categories filter in the catalog.
                    </p>
                </div>

                {
                    props.categories.length > 0 ?
                        <div className="grid gap-x-5 gap-y-12 sm:grid-cols-2 lg:grid-cols-4">
                            {
                                props.categories.map((category) => (
                                    <div key={category.id}>
                                        <article className="group">
                                            <div className="relative aspect-square overflow-hidden rounded-[1.25rem] bg-muted flex items-center justify-center">
                                                {category.slug ? (
                                                    <img src={`/image/category/${category.slug}.jpeg`} alt={category.name} className="h-full w-full object-cover transition-transform duration-500 ease-out group-hover:scale-[1.04]" />
                                                ) : (
                                                    <CirclePile className="size-12 text-muted-foreground/50" />
                                                )}
                                            </div>
                                            <div className="mt-4">
                                                <h2 className="text-lg font-medium tracking-[-0.02em]">{category.name}</h2>
                                                <p className="mt-1 text-sm text-muted-foreground">
                                                    {category.productCount} {category.productCount === 1 ? 'product' : 'products'}
                                                </p>
                                            </div>
                                        </article>
                                    </div>
                                ))
                            }
                        </div>
                        :
                        <EmptyState />
                }
            </main>
        </div>
    )
}

function EmptyState() {
    return (
        <Card className="flex flex-col items-center justify-center gap-3 p-12 text-center">
            <Layers className="mb-1 size-8 text-muted-foreground/50" />
            <p className="text-sm font-medium text-foreground">No categories available</p>
        </Card>
    )
}
