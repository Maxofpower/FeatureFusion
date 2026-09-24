'use client'

import { useState } from "react"
import Link from "next/link"
import {
    Breadcrumb,
    BreadcrumbItem,
    BreadcrumbLink,
    BreadcrumbList,
    BreadcrumbPage,
    BreadcrumbSeparator,
} from "@/components/ui/breadcrumb"
import { Button } from "@/components/ui/button"
import { Separator } from "@/components/ui/separator"
import { APP_ROUTES } from "@/lib/constants/routes"
import type { ProductDetail } from "@/models"
import { ArrowLeft, ShoppingBag } from "lucide-react"

interface Props {
    product: ProductDetail
}

const FALLBACK_IMAGE = "/image/apple.jpeg"

const resolveImage = (url: string | null | undefined) => url || FALLBACK_IMAGE

export const ProductPage = (props: Props) => {
    const { product } = props

    const images = [...product.images].sort((a, b) => a.displayOrder - b.displayOrder)
    const specifications = [...product.specifications].sort((a, b) => a.displayOrder - b.displayOrder)

    const [selectedImage, setSelectedImage] = useState(
        images.find((image) => image.isPrimary)?.url ?? images[0]?.url ?? FALLBACK_IMAGE
    )

    return (
        <div className="bg-background">
            <main className="px-4 py-8 sm:px-6 lg:px-8 flex flex-col gap-6">

                <div className="grid gap-x-8 gap-y-6 lg:grid-cols-2">

                    <div className="flex flex-col gap-4">
                        <div className="relative aspect-square overflow-hidden rounded-[1.25rem] bg-muted">
                            <img
                                src="/image/apple.jpeg"
                                alt={product.name}
                                className="h-full w-full object-cover"
                            />
                            {!product.inStock && (
                                <span className="absolute top-4 left-4 rounded-full bg-background/90 px-3 py-1 text-xs font-medium text-muted-foreground ring-1 ring-border">
                                    Out of stock
                                </span>
                            )}
                        </div>

                        {images.length > 1 && (
                            <div className="grid grid-cols-5 gap-3">
                                {images.map((image) => (
                                    <button
                                        key={image.url}
                                        type="button"
                                        onClick={() => setSelectedImage(image.url)}
                                        aria-label={image.altText}
                                        className={`relative aspect-square overflow-hidden rounded-xl bg-muted ring-2 transition-all outline-none focus-visible:ring-ring ${selectedImage === image.url
                                                ? "ring-foreground"
                                                : "ring-transparent hover:ring-border"
                                            }`}
                                    >
                                        <img
                                            src="/image/apple.jpeg"
                                            alt={image.altText}
                                            className="h-full w-full object-cover"
                                        />
                                    </button>
                                ))}
                            </div>
                        )}
                    </div>

                    {/* Details */}
                    <div className="flex flex-col gap-4">
                        <div className="flex items-center gap-2 text-xs uppercase tracking-[0.16em] text-muted-foreground">
                            <Link
                                href="#"
                                className="transition-colors hover:text-foreground"
                            >
                                {product.brandName}
                            </Link>
                            <span aria-hidden="true">/</span>
                            <Link
                                href="#"
                                className="transition-colors hover:text-foreground"
                            >
                                {product.categoryName}
                            </Link>
                        </div>

                        <div>
                            <h1 className="text-3xl font-bold tracking-tight text-foreground">
                                {product.name}
                            </h1>
                            <p className="mt-1 text-sm text-muted-foreground">
                                SKU: <span className="font-mono">{product.sku}</span>
                            </p>
                        </div>

                        <p className="text-3xl font-semibold tracking-tight">${product.price}</p>

                        <div className="flex items-center gap-2 text-sm">
                            <span
                                className={`inline-flex items-center gap-1.5 font-medium ${product.inStock ? "text-foreground" : "text-destructive"
                                    }`}
                            >
                                <span
                                    className={`size-2 rounded-full ${product.inStock ? "bg-emerald-500" : "bg-destructive"
                                        }`}
                                />
                                {product.inStock ? "In stock" : "Out of stock"}
                            </span>
                            <span className="text-muted-foreground">
                                · {product.stockQuantity} available
                            </span>
                        </div>

                        {product.shortDescription && (
                            <p className="text-sm leading-6 text-muted-foreground">
                                {product.shortDescription}
                            </p>
                        )}

                        {/* <Button size="lg" disabled={!product.inStock} className="flex-1">
                            <ShoppingBag className="size-4" />
                            Add to cart
                        </Button> */}

                        <Separator />

                        {product.fullDescription && (
                            <div>
                                <h2 className="text-lg font-medium tracking-[-0.02em]">
                                    Description
                                </h2>
                                <p className="mt-2 text-sm leading-6 whitespace-pre-line text-muted-foreground">
                                    {product.fullDescription}
                                </p>
                            </div>
                        )}

                        {specifications.length > 0 && (
                            <div>
                                <h2 className="text-lg font-medium tracking-[-0.02em]">
                                    Specifications
                                </h2>
                                <dl className="mt-2 divide-y divide-border rounded-xl ring-1 ring-border">
                                    {specifications.map((spec) => (
                                        <div
                                            key={spec.name}
                                            className="flex items-center justify-between gap-4 px-4 py-2.5 text-sm"
                                        >
                                            <dt className="text-muted-foreground">{spec.name}</dt>
                                            <dd className="text-right font-medium text-foreground">
                                                {spec.value}
                                            </dd>
                                        </div>
                                    ))}
                                </dl>
                            </div>
                        )}
                    </div>
                </div>

                {/* Related products */}
                {product.related.length > 0 && (
                    <section className="flex flex-col gap-4 pt-2">
                        <Separator />
                        <div>
                            <h2 className="text-xl font-bold tracking-tight text-foreground">
                                Related products
                            </h2>
                            <p className="mt-1 text-sm text-muted-foreground">
                                More items from {product.categoryName}.
                            </p>
                        </div>

                        <div className="grid gap-x-5 gap-y-12 sm:grid-cols-2 lg:grid-cols-4">
                            {product.related.map((item) => (
                                <Link
                                    key={item.id}
                                    href={APP_ROUTES.catalogProduct(item.slug)}
                                    className="group"
                                >
                                    <article>
                                        <div className="relative aspect-square overflow-hidden rounded-[1.25rem] bg-muted">
                                            <img
                                                src="/image/apple.jpeg"
                                                alt={item.name}
                                                className="h-full w-full object-cover transition-transform duration-500 ease-out group-hover:scale-[1.04]"
                                            />
                                        </div>
                                        <div className="mt-4 flex items-start justify-between gap-3">
                                            <h3 className="text-lg font-medium tracking-[-0.02em]">
                                                {item.name}
                                            </h3>
                                            <p className="text-sm font-medium">${item.price}</p>
                                        </div>
                                    </article>
                                </Link>
                            ))}
                        </div>
                    </section>
                )}
            </main>
        </div>
    )
}